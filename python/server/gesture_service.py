"""
Gesture Recognition Service — Smart Museum
TCP socket server (port 5001) for C# integration.

Uses the SAME recognition pipeline as gesture_gui.py:
  - centroid of index+middle+pinky fingertips (landmarks 8, 12, 20)
  - 3-frame moving-average smoothing on centroid
  - 30-frame sliding window (buffer)
  - Recognition every 7 frames (RECO_EVERY_N)
  - dollarpy $N recogniser with deepcopy (non-mutating)
  - buffer cleared on any detection ≥ CLEAR_THRESHOLD (0.40)
  - CameraHub (camera_hub.py) when running inside main.py

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
import collections
import time
import math
import pickle
from copy import deepcopy
from typing import Optional

import cv2

# ── path setup ────────────────────────────────────────────────────────────────
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(os.path.dirname(SCRIPT_DIR))
DATA_DIR = os.path.join(os.path.dirname(SCRIPT_DIR), "data")

from dollarpy import Recognizer, Template, Point

# ── Inline mediapipe_compat ───────────────────────────────────────────────────
import mediapipe
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

MODEL_PATH = os.path.join(DATA_DIR, "hand_landmarker.task")

if not os.path.exists(MODEL_PATH):
    print(f"Downloading hand_landmarker.task model...")
    import urllib.request
    url = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
    urllib.request.urlretrieve(url, MODEL_PATH)
    print(f"Model downloaded to {MODEL_PATH}")

class HandLandmarks:
    def __init__(self, landmarks):
        self.landmark = landmarks

class HandsResults:
    def __init__(self):
        self.multi_hand_landmarks = None

class Hands:
    HAND_CONNECTIONS = [
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (0, 9), (9, 10), (10, 11), (11, 12),
        (0, 13), (13, 14), (14, 15), (15, 16),
        (0, 17), (17, 18), (18, 19), (19, 20),
        (5, 9), (9, 13), (13, 17)
    ]
    
    def __init__(self, static_image_mode=False, max_num_hands=1,
                 min_detection_confidence=0.5, min_tracking_confidence=0.5):
        base_options = mp_python.BaseOptions(model_asset_path=MODEL_PATH)
        options = vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.IMAGE,
            num_hands=max_num_hands,
            min_hand_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence
        )
        self.detector = vision.HandLandmarker.create_from_options(options)
    
    def process(self, image):
        mp_image = mediapipe.Image(image_format=mediapipe.ImageFormat.SRGB, data=image)
        detection_result = self.detector.detect(mp_image)
        results = HandsResults()
        if detection_result.hand_landmarks:
            results.multi_hand_landmarks = [
                HandLandmarks(landmarks)
                for landmarks in detection_result.hand_landmarks
            ]
        return results
    
    def __del__(self):
        if hasattr(self, 'detector'):
            self.detector.close()

class DrawingUtils:
    @staticmethod
    def draw_landmarks(image, hand_landmarks, connections):
        if not hand_landmarks: return
        h, w, _ = image.shape
        landmarks = hand_landmarks.landmark
        for connection in connections:
            start_idx, end_idx = connection
            if start_idx < len(landmarks) and end_idx < len(landmarks):
                start = landmarks[start_idx]
                end = landmarks[end_idx]
                start_point = (int(start.x * w), int(start.y * h))
                end_point = (int(end.x * w), int(end.y * h))
                cv2.line(image, start_point, end_point, (0, 200, 255), 2)
        for landmark in landmarks:
            x = int(landmark.x * w)
            y = int(landmark.y * h)
            cv2.circle(image, (x, y), 4, (255, 255, 255), -1)
            cv2.circle(image, (x, y), 4, (0, 150, 255), 1)

class Solutions:
    class hands:
        Hands = Hands
        HAND_CONNECTIONS = Hands.HAND_CONNECTIONS
    drawing_utils = DrawingUtils()

mp = Solutions()

# ── CameraHub (same camera as main.py server) ────────────────────────────────
# Import from python/server/camera_hub.py so standalone runs use the same
# camera index (MUSEUM_CAMERA env var) and DSHOW logic as the full server.
_SERVER_DIR = os.path.join(os.path.dirname(SCRIPT_DIR), "python", "server")
if _SERVER_DIR not in sys.path:
    sys.path.insert(0, _SERVER_DIR)

try:
    from camera_hub import CameraHub
    _HUB_AVAILABLE = True
except ImportError:
    _HUB_AVAILABLE = False
    print("[GESTURE] camera_hub not found — will fall back to direct VideoCapture")

# ── Constants — MUST match gesture_gui.py ─────────────────────────────────────
TEMPLATES_FILE   = os.path.join(DATA_DIR, "gesture_templates.pkl")
MAX_FRAMES       = 30      # sliding window size (frames)
MIN_POINTS       = 10      # min pts before recognition attempt
MIN_MOTION       = 0.02    # min cumulative centroid travel
SCORE_THRESHOLD  = 0.20    # min score to confirm a gesture
GESTURE_COOLDOWN = 1.0     # seconds before next gesture accepted
TRACK_TIPS       = (8, 12, 20)  # index, middle, pinky tip landmark IDs
CLEAR_THRESHOLD  = 0.40   # clear buffer on detection at/above this score
SMOOTH_WIN       = 3       # frames to average for centroid smoothing
RECO_EVERY_N     = 7       # run recognition every N frames (7 ≈ every ~230 ms at 30 fps)

# ── Circular menu gestures (C# integration) ────────────────────────────────────
CIRCULAR_MENU_GESTURES = {"swipe_left", "swipe_right", "close"}  # Match template names exactly


# ── Shared helpers (identical to gesture_gui.py) ───────────────────────────────

def _extract_points(buf):
    """
    Centroid of index+middle+pinky fingertips → list[Point].
    Accepts two frame formats:
      {"x": float, "y": float}  — pre-smoothed live capture (preferred)
      {"lm": hand_landmarks}    — raw MediaPipe object (legacy / template build)
    Returns None when data is insufficient.
    """
    pts = []
    for fd in buf:
        if "x" in fd and "y" in fd:
            pts.append(Point(fd["x"], fd["y"], stroke_id=0))
        else:
            lm = fd.get("lm")
            if lm is None:
                continue
            # Centroid of index, middle, pinky tips
            tips = [lm.landmark[i] for i in TRACK_TIPS]
            tx = sum(t.x for t in tips) / len(tips)
            ty = sum(t.y for t in tips) / len(tips)
            pts.append(Point(tx, ty, stroke_id=0))

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
        gesture_names = {}
        for tmpl in t:
            gesture_names[tmpl.name] = gesture_names.get(tmpl.name, 0) + 1
        names_str = ", ".join(f"{n}({c})" for n, c in sorted(gesture_names.items()))
        print(f"[GESTURE] {len(t)} templates: {names_str}")
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
        mp_h = mp.hands
        self.hands = mp_h.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )

        # Sliding-window buffer
        self.buf: list = []
        self.lock       = threading.Lock()

        # 3-frame centroid smoothing
        self._raw_tip_buf = collections.deque(maxlen=SMOOTH_WIN)

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
        self.recog_interval   = 0.23   # ~230 ms → every ~7 frames at 30 fps
        self._new_frames      = 0      # frame counter for throttled logging

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
        """Read frames → MediaPipe → sliding window → recognition every RECO_EVERY_N frames."""
        frame_count = 0
        
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

            frame_count += 1

            frame = cv2.resize(frame, (640, 480))
            rgb   = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            res   = self.hands.process(rgb)

            if not self.is_tracking or self.is_paused:
                time.sleep(0.016)
                continue

            # ── Sliding window — 3-finger centroid + smoothing ────────────────
            if res.multi_hand_landmarks:
                self._new_frames += 1
                lm = res.multi_hand_landmarks[0]
                # Centroid of index, middle, pinky tips
                tips3  = [lm.landmark[i] for i in TRACK_TIPS]
                raw_x  = sum(t.x for t in tips3) / len(tips3)
                raw_y  = sum(t.y for t in tips3) / len(tips3)
                # 3-frame moving-average smoothing
                self._raw_tip_buf.append((raw_x, raw_y))
                sx = sum(p[0] for p in self._raw_tip_buf) / len(self._raw_tip_buf)
                sy = sum(p[1] for p in self._raw_tip_buf) / len(self._raw_tip_buf)
                with self.lock:
                    self.buf.append({"x": sx, "y": sy})
                    if len(self.buf) > MAX_FRAMES:
                        self.buf.pop(0)
            else:
                # Hand lost — slowly drain, reset smoothing
                self._raw_tip_buf.clear()
                self._new_frames = 0  # Reset frame counter when hand is lost
                with self.lock:
                    if self.buf:
                        self.buf.pop(0)

            # ── Recognition — every RECO_EVERY_N new hand frames ──────────────
            now = time.time()
            in_cooldown = (now - self.last_gesture_time) < GESTURE_COOLDOWN

            if (not in_cooldown
                    and len(self.buf) >= MIN_POINTS
                    and self._new_frames >= RECO_EVERY_N):

                self._new_frames = 0
                with self.lock:
                    buf_snapshot = list(self.buf)

                pts = _extract_points(buf_snapshot)
                if pts is not None:
                    name, score = _recognize_points(self.templates, pts)

                    # Log every attempt that exceeds the confidence threshold
                    if score >= SCORE_THRESHOLD:
                        print(f"[GESTURE] {name}  score={score:.2f}  buf={len(buf_snapshot)}")

                    if name and score >= SCORE_THRESHOLD:
                        if name in CIRCULAR_MENU_GESTURES:
                            self.last_gesture      = name
                            self.last_score        = score
                            self.last_gesture_time = now
                            if score >= CLEAR_THRESHOLD:
                                with self.lock:
                                    self.buf.clear()
                                self._raw_tip_buf.clear()
                            print(f"[GESTURE] ✓ {name}  score={score:.2f}  → queued for C#")

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
        return {"status": "ok", "message": "Tracking started"}

    def cmd_stop_tracking(self):
        """NON-BLOCKING — pauses frame collection; camera thread keeps running."""
        self.is_tracking = False
        self.is_paused   = False
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
        if (now - self.last_gesture_time) < GESTURE_COOLDOWN:
            return {"status": "cooldown", "gesture": None, "score": 0.0}
        if self.last_gesture is None:
            return {"status": "ok", "gesture": None, "score": 0.0}
        if (now - self.last_gesture_time) > 4.0:
            self.last_gesture = None
            return {"status": "ok", "gesture": None, "score": 0.0}
        name  = self.last_gesture
        score = self.last_score
        self.last_gesture = None
        self.last_score   = 0.0
        action = {"close": "open_menu", "swipe_right": "navigate_right",
                  "swipe_left": "navigate_left"}.get(name, name)
        return {
            "status": "ok",
            "gesture": name,
            "action": action,
            "score": round(score, 4),
            "confidence": "high" if score >= 0.65 else "medium" if score >= 0.45 else "low"
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
            "recognized_gestures": list(CIRCULAR_MENU_GESTURES),
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
        self._raw_tip_buf.clear()
        self.last_gesture      = None
        self.last_score        = 0.0
        self.last_gesture_time = 0.0
        # Restart tracking immediately so C# doesn't need to re-send START_TRACKING
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
        self.is_running = False
        self.server_socket = None
        self.templates  = _load_templates()

        # Auto-create CameraHub with server default settings when none
        # is injected (i.e. running standalone, not from main.py server).
        self._owned_hub = False
        if camera_hub is None and _HUB_AVAILABLE:
            try:
                cam_idx    = int(os.environ.get("MUSEUM_CAMERA", "0"))
                camera_hub = CameraHub(camera_index=cam_idx)
                camera_hub.start()          # CameraHub requires explicit start()
                self._owned_hub = True
                print(f"[GESTURE] CameraHub created and started (camera={cam_idx})")
            except Exception as e:
                print(f"[GESTURE] CameraHub init failed: {e} — using direct VideoCapture")
        elif camera_hub is not None:
            print("[GESTURE] Using injected CameraHub (server mode)")
        else:
            print("[GESTURE] CameraHub unavailable — using direct VideoCapture")

        self.camera_hub = camera_hub

    def start_server(self):
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            self.server_socket.bind((self.host, self.port))
        except Exception as e:
            print(f"[GESTURE] ERROR: Failed to bind {self.host}:{self.port} — {e}")
            return

        self.server_socket.listen(5)
        self.is_running = True
        hub_mode = 'shared' if self.camera_hub else 'local'
        print(f"[GESTURE] Listening on {self.host}:{self.port}  ({len(self.templates)} templates, camera={hub_mode})")
        print(f"[GESTURE] cooldown={GESTURE_COOLDOWN}s  reco_every={RECO_EVERY_N}fr  window={MAX_FRAMES}fr")

        try:
            while self.is_running:
                try:
                    client_sock, addr = self.server_socket.accept()
                    print(f"[GESTURE] C# connected from {addr}")
                except OSError as e:
                    print(f"[GESTURE] Socket error: {e}")
                    break
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
                print(f"[GESTURE] send error: {e}")

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
                    if   cmd == "START_TRACKING":   send(state.cmd_start_tracking())
                    elif cmd == "STOP_TRACKING":    send(state.cmd_stop_tracking())
                    elif cmd == "RECOGNIZE":         send(state.cmd_recognize())
                    elif cmd == "STATUS":            send(state.cmd_status())
                    elif cmd == "RESET":             send(state.cmd_reset())
                    elif cmd == "PAUSE_DETECTION":  send(state.cmd_pause_detection())
                    elif cmd == "RESUME_DETECTION": send(state.cmd_resume_detection())
                    elif cmd == "PING":              send({"status": "ok", "message": "pong"})
                    else:
                        send({"status": "ok", "message": f"Unknown command: {cmd}"})
        except Exception as e:
            print(f"[GESTURE] client error: {e}")
        finally:
            state.cleanup()
            try: client_sock.close()
            except Exception: pass
            print(f"[GESTURE] C# disconnected")

    def cleanup(self):
        self.is_running = False
        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception:
                pass
        # Shut down the hub only if this service created it (standalone mode).
        # When injected from main.py server, the server owns the lifecycle.
        if self._owned_hub and self.camera_hub is not None:
            try:
                self.camera_hub.stop()      # CameraHub uses stop(), not shutdown()
                print("[GESTURE] CameraHub stopped")
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
