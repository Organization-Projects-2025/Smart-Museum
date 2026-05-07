"""
Gesture Recognition Service — Smart Museum
TCP socket server (port 5001) for C# integration.

Uses the SAME recognition pipeline as gesture_gui.py:
  - index-fingertip path (raw normalised coords)
  - 60-frame sliding window
  - dollarpy $N recogniser with deepcopy (non-mutating)
  - SharedCameraHub when running inside unified_museum_server

C# protocol (newline-delimited JSON over TCP):
  Commands  -> plain text strings (one per line)
  Responses <- JSON objects (one per line)

  START_TRACKING   begin filling the sliding window
  STOP_TRACKING    stop; release camera if locally owned
  RECOGNIZE        return last confirmed gesture (or null)
  STATUS           return buffer / cooldown / template info
  RESET            clear state
  PING             health check → {"status":"ok","message":"pong"}
"""

import os
import sys
import socket
import json
import threading
import time
import math
import pickle
from copy import deepcopy
from typing import Optional

import cv2

# ── path setup ────────────────────────────────────────────────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from dollarpy import Recognizer, Template, Point
import mediapipe_compat as mp

# ── Constants — MUST match gesture_gui.py ─────────────────────────────────────
TEMPLATES_FILE   = os.path.join(SCRIPT_DIR, "gesture_templates.pkl")
MAX_FRAMES       = 60      # sliding window (frames)
MIN_POINTS       = 10      # min pts before recognition attempt
MIN_MOTION       = 0.02    # min cumulative index-tip travel (lenient)
SCORE_THRESHOLD  = 0.20    # min score to confirm a gesture (lenient)
GESTURE_COOLDOWN = 1.5     # seconds before next gesture accepted
INDEX_TIP        = 8       # MediaPipe landmark id


# ── Shared helpers (identical to gesture_gui.py) ───────────────────────────────

def _extract_points(buf):
    """
    Index-fingertip path in raw normalised coords → list[Point].
    `buf` is a list of {"lm": hand_landmarks} dicts.
    Returns None when data is insufficient.
    """
    pts = []
    for fd in buf:
        lm = fd.get("lm")
        if lm is None:
            continue
        tip = lm.landmark[INDEX_TIP]
        pts.append(Point(tip.x, tip.y, stroke_id=0))

    if len(pts) < MIN_POINTS:
        return None

    motion = sum(
        math.hypot(pts[i].x - pts[i-1].x, pts[i].y - pts[i-1].y)
        for i in range(1, len(pts))
    )
    return pts if motion >= MIN_MOTION else None


def _recognize_points(templates, pts):
    """
    Run dollarpy on `pts` with a fresh (non-mutating) recogniser.
    Returns (gesture_name: str, score: float) or (None, 0.0).
    """
    if not templates or pts is None:
        return None, 0.0
    try:
        rec    = Recognizer(deepcopy(templates))
        result = rec.recognize(pts)
        if result and len(result) == 2:
            return result[0], float(result[1])
    except Exception:
        pass
    return None, 0.0


def _load_templates():
    """Load templates from gesture_templates.pkl; return list or []."""
    if not os.path.exists(TEMPLATES_FILE):
        print(f"[GESTURE] WARNING: templates not found at {TEMPLATES_FILE}")
        return []
    try:
        with open(TEMPLATES_FILE, "rb") as f:
            t = pickle.load(f)
        print(f"[GESTURE] Loaded {len(t)} templates from {TEMPLATES_FILE}")
        return t
    except Exception as e:
        print(f"[GESTURE] ERROR loading templates: {e}")
        return []


# ── Per-client state machine ───────────────────────────────────────────────────

class _ClientState:
    """All mutable state for a single connected C# client."""

    def __init__(self, client_id: str, templates, camera_hub=None):
        self.client_id  = client_id
        self.templates  = templates
        self.camera_hub = camera_hub

        # Per-client MediaPipe instance
        mp_h = mp.solutions.hands
        self.hands = mp_h.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )

        # Sliding-window buffer
        self.buf: list = []
        self.lock       = threading.Lock()

        # Tracking / recognition flags
        self.is_tracking     = False
        self.is_paused       = False   # PAUSE_DETECTION suspends frame collection
        self.camera_running  = False
        self.hub_acquired    = False
        self.cap             = None
        self.camera_thread: Optional[threading.Thread] = None

        # Last confirmed detection
        self.last_gesture     = None
        self.last_score       = 0.0
        self.last_gesture_time = 0.0

        # Continuous recognition timing
        self.last_recog_time  = 0.0
        self.recog_interval   = 0.05   # 50 ms → ~20 Hz

    # ── Camera pipeline ──────────────────────────────────────────────────────

    def start_camera(self) -> bool:
        """Start camera thread once per client session. Non-blocking after first call."""
        if self.camera_running:
            return True  # Already running — fast path, no blocking

        if self.camera_hub is not None:
            self.camera_hub.acquire(self.client_id)
            self.hub_acquired   = True
            self.camera_running = True
            self.camera_thread  = threading.Thread(
                target=self._camera_loop, daemon=True)
            self.camera_thread.start()
            return True

        # Fallback: open camera locally
        cam_idx = int(os.environ.get("GESTURE_CAMERA", "0"))
        cap = cv2.VideoCapture(cam_idx)
        if not cap.isOpened():
            print(f"[GESTURE:{self.client_id}] ERROR: cannot open camera {cam_idx}")
            return False
        self.cap            = cap
        self.camera_running = True
        self.camera_thread  = threading.Thread(
            target=self._camera_loop, daemon=True)
        self.camera_thread.start()
        return True

    def _full_stop_camera(self):
        """Blocking stop — only called from cleanup() on client disconnect."""
        self.camera_running = False
        if self.camera_thread is not None:
            self.camera_thread.join(timeout=2.5)
            self.camera_thread = None
        if self.hub_acquired and self.camera_hub is not None:
            try:
                self.camera_hub.release(self.client_id)
            except Exception:
                pass
            self.hub_acquired = False
        if self.cap is not None:
            try:
                self.cap.release()
            except Exception:
                pass
            self.cap = None

    def _camera_loop(self):
        """Read frames → MediaPipe → sliding window → continuous recognition."""
        while self.camera_running:
            # ── Get frame ────────────────────────────────────────────────────
            if self.camera_hub is not None:
                frame = self.camera_hub.get_latest_bgr_copy()
                if frame is None:
                    time.sleep(0.016)
                    continue
            else:
                if self.cap is None or not self.cap.isOpened():
                    break
                ret, frame = self.cap.read()
                if not ret:
                    time.sleep(0.016)
                    continue

            frame = cv2.resize(frame, (640, 480))
            rgb   = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            res   = self.hands.process(rgb)

            if not self.is_tracking or self.is_paused:
                time.sleep(0.016)
                continue

            # ── Sliding window ────────────────────────────────────────────────
            if res.multi_hand_landmarks:
                lm = res.multi_hand_landmarks[0]
                with self.lock:
                    self.buf.append({"lm": lm})
                    if len(self.buf) > MAX_FRAMES:
                        self.buf.pop(0)
            else:
                # Hand lost — slowly drain
                with self.lock:
                    if self.buf:
                        self.buf.pop(0)

            # ── Continuous recognition ────────────────────────────────────────
            now = time.time()
            in_cooldown = (now - self.last_gesture_time) < GESTURE_COOLDOWN

            if (not in_cooldown
                    and len(self.buf) >= MIN_POINTS
                    and (now - self.last_recog_time) >= self.recog_interval):

                with self.lock:
                    buf_snapshot = list(self.buf)

                pts = _extract_points(buf_snapshot)
                if pts is not None:
                    name, score = _recognize_points(self.templates, pts)
                    self.last_recog_time = now
                    print(f"[GESTURE] attempt: {name}  score={score:.3f}  buf={len(buf_snapshot)}")

                    if name and score >= SCORE_THRESHOLD:
                        print(f"[GESTURE:{self.client_id}] ✓ DETECTED: {name}  score={score:.3f}")
                        self.last_gesture      = name
                        self.last_score        = score
                        self.last_gesture_time = now
                        with self.lock:
                            self.buf.clear()   # fresh start after detection

            time.sleep(0.016)  # ~60 FPS

    # ── Command handlers ──────────────────────────────────────────────────────

    def cmd_start_tracking(self):
        """Start/resume tracking. Camera thread starts once and stays alive."""
        if not self.start_camera():
            return {"status": "error", "message": "Failed to start camera"}
        self.is_tracking = True
        self.is_paused   = False
        with self.lock:
            self.buf.clear()
        print(f"[GESTURE:{self.client_id}] START_TRACKING ok")
        return {"status": "ok", "message": "Tracking started"}

    def cmd_stop_tracking(self):
        """
        NON-BLOCKING. Just pauses frame collection — camera thread keeps running.
        This is the fix for the C# timeout: stop_camera() used to block 2.5s.
        """
        self.is_tracking = False
        self.is_paused   = False
        print(f"[GESTURE:{self.client_id}] STOP_TRACKING ok (camera still running)")
        return {"status": "ok", "message": "Tracking stopped"}

    def cmd_pause_detection(self):
        """Pause frame collection (keep camera running)."""
        self.is_paused = True
        with self.lock:
            self.buf.clear()
        return {"status": "ok", "message": "Detection paused"}

    def cmd_resume_detection(self):
        """Resume frame collection after pause."""
        self.is_paused = False
        if not self.camera_running:
            self.start_camera()
        self.is_tracking = True
        return {"status": "ok", "message": "Detection resumed"}

    def cmd_recognize(self):
        """Return last confirmed gesture and clear it so C# knows it was consumed."""
        now = time.time()
        in_cooldown = (now - self.last_gesture_time) < GESTURE_COOLDOWN
        if in_cooldown:
            remaining = round(GESTURE_COOLDOWN - (now - self.last_gesture_time), 1)
            return {
                "status": "cooldown",
                "gesture": None,
                "score": 0.0,
                "cooldown_remaining": remaining,
                "message": f"Cooldown active ({remaining}s remaining)"
            }
        if self.last_gesture is None:
            return {"status": "ok", "gesture": None, "score": 0.0,
                    "message": "No gesture detected yet"}
        if (now - self.last_gesture_time) > 4.0:
            # Stale — clear and return null
            self.last_gesture = None
            return {"status": "ok", "gesture": None, "score": 0.0,
                    "message": "Last gesture is stale (>4s)"}
        # Return and clear so next call won't double-fire
        name  = self.last_gesture
        score = self.last_score
        self.last_gesture = None
        self.last_score   = 0.0
        return {
            "status": "ok",
            "gesture": name,
            "score": round(score, 4),
            "confidence": (
                "high" if score >= 0.65
                else "medium" if score >= 0.45
                else "low"
            ),
            "message": f"Gesture: {name}"
        }

    def cmd_status(self):
        now = time.time()
        in_cooldown = (now - self.last_gesture_time) < GESTURE_COOLDOWN
        remaining   = max(0.0, GESTURE_COOLDOWN - (now - self.last_gesture_time))
        with self.lock:
            buf_len = len(self.buf)
        return {
            "status":              "ok",
            "tracking":            self.is_tracking and not self.is_paused,
            "paused":              self.is_paused,
            "buffer_frames":       buf_len,
            "frames_collected":    buf_len,   # alias C# reads
            "buffer_max":          MAX_FRAMES,
            "templates":           len(self.templates),
            "last_gesture":        self.last_gesture,
            "last_score":          round(self.last_score, 4),
            "in_cooldown":         in_cooldown,
            "cooldown_remaining":  round(remaining, 1),
            # Fields C# ServiceStatus model reads
            "waiting_for_motion":  self.is_tracking and not self.is_paused and buf_len < MIN_POINTS,
            "capturing":           self.is_tracking and not self.is_paused and buf_len >= MIN_POINTS,
        }

    def cmd_reset(self):
        """Clear state. Camera keeps running so next START_TRACKING is instant."""
        self.is_tracking = False
        self.is_paused   = False
        with self.lock:
            self.buf.clear()
        self.last_gesture      = None
        self.last_score        = 0.0
        self.last_gesture_time = 0.0
        # Restart tracking immediately so C# doesn't need to send START_TRACKING
        self.is_tracking = True
        if not self.camera_running:
            self.start_camera()
        return {"status": "ok", "message": "Reset complete"}

    def cleanup(self):
        """Called on client disconnect. Blocking stop is fine here."""
        self.is_tracking = False
        self._full_stop_camera()
        try:
            self.hands.__del__()
        except Exception:
            pass


# ── TCP Server ─────────────────────────────────────────────────────────────────

class GestureRecognitionService:
    """
    TCP socket server — one thread per C# client.
    Compatible with SharedCameraHub from unified_museum_server.
    """

    def __init__(self, host: str = "127.0.0.1", port: int = 5001,
                 camera_hub=None):
        self.host       = host
        self.port       = port
        self.camera_hub = camera_hub
        self.is_running = False
        self.server_socket = None
        self.templates  = _load_templates()

    def start_server(self):
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(5)
        self.is_running = True

        print(f"[GESTURE] Listening on {self.host}:{self.port}")
        print(f"[GESTURE] Templates: {len(self.templates)}")
        print(f"[GESTURE] Camera hub: {'shared' if self.camera_hub else 'local'}")

        try:
            while self.is_running:
                try:
                    client_sock, addr = self.server_socket.accept()
                except OSError:
                    break
                print(f"[GESTURE] Client connected: {addr}")
                t = threading.Thread(
                    target=self._handle_client,
                    args=(client_sock, addr),
                    daemon=True)
                t.start()
        finally:
            self.cleanup()

    def _handle_client(self, client_sock, addr):
        client_id = f"gesture:{addr[0]}:{addr[1]}"
        state     = _ClientState(client_id, self.templates, self.camera_hub)

        def send(obj):
            try:
                client_sock.sendall((json.dumps(obj) + "\n").encode("utf-8"))
            except Exception as e:
                print(f"[GESTURE:{addr}] send error: {e}")

        # Receive full lines (C# sends one command per line)
        buf = b""
        try:
            while self.is_running:
                chunk = client_sock.recv(1024)
                if not chunk:
                    break
                buf += chunk
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    cmd = line.decode("utf-8").strip().upper()
                    if not cmd:
                        continue
                    print(f"[GESTURE:{addr}] CMD: {cmd}")
                    if   cmd == "START_TRACKING":    send(state.cmd_start_tracking())
                    elif cmd == "STOP_TRACKING":     send(state.cmd_stop_tracking())
                    elif cmd == "RECOGNIZE":          send(state.cmd_recognize())
                    elif cmd == "STATUS":             send(state.cmd_status())
                    elif cmd == "RESET":              send(state.cmd_reset())
                    elif cmd == "PAUSE_DETECTION":   send(state.cmd_pause_detection())
                    elif cmd == "RESUME_DETECTION":  send(state.cmd_resume_detection())
                    elif cmd == "PING":               send({"status": "ok", "message": "pong"})
                    else:
                        # Return ok for unknown commands so C# doesn't disconnect
                        print(f"[GESTURE:{addr}] unknown cmd: {cmd}")
                        send({"status": "ok", "message": f"Unknown command ignored: {cmd}"})
        except Exception as e:
            print(f"[GESTURE:{addr}] connection error: {e}")
        finally:
            state.cleanup()
            try:
                client_sock.close()
            except Exception:
                pass
            print(f"[GESTURE:{addr}] disconnected")

    def cleanup(self):
        self.is_running = False
        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception:
                pass
        print("[GESTURE] Service stopped")


# ── Standalone entry point ─────────────────────────────────────────────────────

def main():
    print("=" * 55)
    print("Smart Museum — Gesture Recognition Service")
    print("=" * 55)
    service = GestureRecognitionService(host="127.0.0.1", port=5001)
    try:
        service.start_server()
    except KeyboardInterrupt:
        print("\n[GESTURE] Shutting down…")
        service.cleanup()


if __name__ == "__main__":
    main()
