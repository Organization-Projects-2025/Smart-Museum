"""
yolo_server.py — Smart Museum watch/clock control for the circular menu.

TCP protocol matches gesture_service.py (port 5005, newline JSON).
Uses the shared CameraHub from main.py (same frames as gaze / hand services).

Gestures:
  close        — clock appeared → open menu
  close_menu   — no clock for IDLE_CLOSE_SEC → hide menu
  swipe_right / swipe_left / swipe_up / swipe_down
"""

from __future__ import annotations

import json
import os
import socket
import sys
import threading
import time
import traceback
from collections import deque
from typing import Optional

import cv2
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(os.path.dirname(SCRIPT_DIR))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)


def _resolve_model_path() -> str:
    for base in (PROJECT_ROOT, os.getcwd(), SCRIPT_DIR):
        path = os.path.join(base, MODEL)
        if os.path.isfile(path):
            return path
    return MODEL

try:
    from camera_hub import CameraHub
    _HUB_OK = True
except ImportError:
    _HUB_OK = False

HOST = os.environ.get("YOLO_SERVER_HOST", "127.0.0.1")
PORT = int(os.environ.get("YOLO_SERVER_PORT", "5005"))
MODEL = os.environ.get("YOLO_MODEL", "yolo11s.pt")
IMGSZ = int(os.environ.get("YOLO_IMGSZ", "416"))
INFER_MAX_W = int(os.environ.get("YOLO_INFER_WIDTH", "640"))
CLOCK_CLASS = 74
TRACK_CONF = 0.12
SWIPE_MIN_CONF = 0.10
VIS_HITS_ON = 1
VIS_MISS_OFF = 12
IDLE_CLOSE_SEC = float(os.environ.get("YOLO_IDLE_CLOSE_SEC", "10"))
GESTURE_COOLDOWN = float(os.environ.get("YOLO_GESTURE_COOLDOWN", "0.45"))
OPEN_MENU_COOLDOWN = 2.0
SWIPE_BLOCK_AFTER_OPEN_SEC = 1.0
LOG_EVERY_N = 60

SWIPE_WINDOW = 10
SWIPE_BASE_PX = 40
SWIPE_COOLDOWN_FRAMES = 8
GAP_MAX_SEC = 0.55

_hub: Optional["CameraHub"] = None
_owned_hub = False


def set_hub(hub):
    global _hub
    _hub = hub
    if hub is not None:
        print(f"[YOLO] CameraHub wired (index {getattr(hub, 'camera_index', '?')})")


class _PixelSwipe:
    def __init__(self):
        self._track: list[tuple[float, float]] = []
        self._cooldown = 0
        self._ghost = None
        self._ghost_at = 0.0

    def reset(self):
        self._track.clear()
        self._ghost = None

    def _thresh(self, box) -> float:
        x1, y1, x2, y2 = box
        size = max(x2 - x1, y2 - y1, 20)
        return SWIPE_BASE_PX * max(0.55, min(1.4, 120.0 / size))

    def _classify(self, dx: float, dy: float, t: float) -> str | None:
        ax, ay = abs(dx), abs(dy)
        if ax > t and ax > ay * 1.05:
            return "swipe_right" if dx > 0 else "swipe_left"
        if ay > t and ay > ax * 1.05:
            return "swipe_up" if dy < 0 else "swipe_down"
        return None

    def update(self, box, conf: float) -> str | None:
        now = time.perf_counter()
        if self._cooldown > 0:
            self._cooldown -= 1

        if box is None:
            if self._track:
                self._ghost = self._track[-1]
                self._ghost_at = now
            return None
        if conf < SWIPE_MIN_CONF:
            return None

        x1, y1, x2, y2 = box
        cx, cy = (x1 + x2) / 2.0, (y1 + y2) / 2.0
        t = self._thresh(box)

        if self._ghost and (now - self._ghost_at) <= GAP_MAX_SEC:
            dx, dy = cx - self._ghost[0], cy - self._ghost[1]
            self._ghost = None
            if self._cooldown == 0:
                g = self._classify(dx, dy, t * 0.85)
                if g:
                    self._track.clear()
                    self._cooldown = SWIPE_COOLDOWN_FRAMES
                    return g

        self._track.append((cx, cy))
        if len(self._track) > 60:
            self._track.pop(0)
        if len(self._track) < SWIPE_WINDOW or self._cooldown > 0:
            return None

        dx = self._track[-1][0] - self._track[-SWIPE_WINDOW][0]
        dy = self._track[-1][1] - self._track[-SWIPE_WINDOW][1]
        swipe = self._classify(dx, dy, t)
        if swipe:
            self._track.clear()
            self._cooldown = SWIPE_COOLDOWN_FRAMES
            return swipe
        return None


class _YoloEngine:
    def __init__(self, camera_hub=None):
        self.hub = camera_hub
        self.cap = None
        self._local_running = False
        self._local_thread: Optional[threading.Thread] = None
        self.model = None
        self._device = "cpu"
        self._half = False
        self._swipe = _PixelSwipe()
        self._lock = threading.Lock()
        self._gesture_queue: deque = deque(maxlen=16)
        self.last_gesture: str | None = None
        self.last_score = 0.0
        self.last_gesture_time = 0.0
        self._last_event_time = 0.0
        self.object_visible = False
        self._last_seen_clock = 0.0
        self._was_visible = False
        self._idle_close_sent = False
        self._last_open_menu_time = 0.0
        self.is_tracking = False
        self._infer_fps = 0.0
        self._infer_thread: Optional[threading.Thread] = None
        self._frames_processed = 0
        self._no_frame_warn_at = 0.0
        self._vis_hits = 0
        self._vis_miss = 0
        self._hold_box: tuple[int, int, int, int] | None = None
        self._hold_conf = 0.0
        self._ambient = {"phone": False, "book": False, "large_person": False}
        self._ambient_scan_tick = 0

    def _prep_frame(self, frame: np.ndarray) -> np.ndarray:
        h, w = frame.shape[:2]
        if w <= INFER_MAX_W:
            return frame
        scale = INFER_MAX_W / w
        return cv2.resize(frame, (INFER_MAX_W, int(h * scale)), interpolation=cv2.INTER_AREA)

    def _scan_ambient(self, frame_bgr: np.ndarray) -> None:
        """Periodic full-class scan for phone/book/person (replaces legacy port 5003 context stream)."""
        if self.model is None:
            return
        self._ambient_scan_tick += 1
        if self._ambient_scan_tick % 15 != 0:
            return
        infer = self._prep_frame(frame_bgr)
        fh, fw = frame_bgr.shape[:2]
        try:
            results = self.model.predict(
                infer,
                verbose=False,
                conf=0.35,
                imgsz=IMGSZ,
                device=self._device,
                half=self._half,
            )
        except Exception as e:
            print(f"[YOLO] ambient scan error: {e}")
            return
        phone = book = large = False
        res = results[0]
        if res.boxes is None:
            with self._lock:
                self._ambient = {"phone": phone, "book": book, "large_person": large}
            return
        names = res.names or {}
        xyxy = res.boxes.xyxy.cpu().numpy()
        confs = res.boxes.conf.cpu().numpy()
        clss = res.boxes.cls.cpu().numpy()
        for i in range(len(clss)):
            if float(confs[i]) < 0.35:
                continue
            label = str(names.get(int(clss[i]), "")).lower()
            x1, y1, x2, y2 = xyxy[i]
            area = ((x2 - x1) / fw) * ((y2 - y1) / fh)
            if "phone" in label:
                phone = True
            if label in ("book", "laptop") or "laptop" in label:
                book = True
            if label == "person" and area >= 0.10:
                large = True
        with self._lock:
            self._ambient = {"phone": phone, "book": book, "large_person": large}

    def _open_local_camera(self):
        idx = int(os.environ.get("MUSEUM_CAMERA", os.environ.get("YOLO_CAMERA", "0")))
        self.cap = cv2.VideoCapture(idx)
        if not self.cap.isOpened() and sys.platform == "win32":
            self.cap = cv2.VideoCapture(idx, cv2.CAP_DSHOW)
        if self.cap.isOpened():
            self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            for _ in range(5):
                if self.cap.read()[0]:
                    break
                time.sleep(0.05)
        return self.cap is not None and self.cap.isOpened()

    def _local_loop(self):
        while self._local_running and self.cap and self.cap.isOpened():
            frame = None
            for _ in range(3):
                ok, f = self.cap.read()
                if ok and f is not None:
                    frame = f
            if frame is not None:
                self._process_frame(frame)
            else:
                time.sleep(0.01)

    def start_camera(self):
        if self.hub is not None:
            return True
        if self.cap and self.cap.isOpened():
            return True
        if not self._open_local_camera():
            return False
        self._local_running = True
        self._local_thread = threading.Thread(target=self._local_loop, daemon=True, name="YoloLocalCam")
        self._local_thread.start()
        return True

    def stop_camera(self):
        self._local_running = False
        if self._local_thread:
            self._local_thread.join(timeout=2.0)
        if self.cap:
            self.cap.release()
            self.cap = None

    def load_model(self):
        import torch
        from ultralytics import YOLO

        self._device = 0 if torch.cuda.is_available() else "cpu"
        self._half = torch.cuda.is_available()
        model_path = _resolve_model_path()
        print(f"[YOLO] Loading model: {model_path}")
        self.model = YOLO(model_path)
        warm_h = max(64, int(round(IMGSZ * 480 / 640)))
        dummy = np.zeros((warm_h, IMGSZ, 3), dtype=np.uint8)
        self.model.track(
            dummy, persist=True, verbose=False, conf=TRACK_CONF,
            classes=[CLOCK_CLASS], tracker="bytetrack.yaml",
            imgsz=IMGSZ, device=self._device, half=self._half,
        )
        print(f"[YOLO] Model {MODEL} ready ({'GPU' if self._half else 'CPU'}) imgsz={IMGSZ}")

    def _queue_gesture(self, name: str, score: float = 1.0, detail: str = ""):
        with self._lock:
            self._gesture_queue.append((name, float(score), detail))
            self.last_gesture = name
            self.last_score = score
            self.last_gesture_time = time.time()
        self._last_event_time = time.time()
        extra = f"  {detail}" if detail else ""
        q_len = len(self._gesture_queue)
        print(f"[YOLO] ✓ {name}  score={score:.2f}{extra}  → queued for C# (q={q_len})")

    def _clock_priority_active(self, now: float | None = None) -> bool:
        now = now or time.time()
        if self.object_visible:
            return True
        if self._last_seen_clock <= 0:
            return False
        return (now - self._last_seen_clock) < IDLE_CLOSE_SEC

    def _process_frame(self, frame: np.ndarray):
        if self.model is None or not self.is_tracking:
            return

        infer_frame = self._prep_frame(frame)
        t0 = time.perf_counter()
        try:
            results = self.model.track(
                infer_frame,
                persist=True,
                verbose=False,
                conf=TRACK_CONF,
                classes=[CLOCK_CLASS],
                tracker="bytetrack.yaml",
                imgsz=IMGSZ,
                device=self._device,
                half=self._half,
            )
        except Exception as e:
            print(f"[YOLO] track error: {e}")
            traceback.print_exc()
            return

        dt = time.perf_counter() - t0
        if dt > 0:
            self._infer_fps = 0.85 * self._infer_fps + 0.15 * (1.0 / dt)

        self._frames_processed += 1
        res = results[0]
        visible = res.boxes is not None and len(res.boxes) > 0
        now = time.time()

        box = None
        conf = 0.0
        if visible:
            xyxy = res.boxes.xyxy.cpu().numpy()
            confs = res.boxes.conf.cpu().numpy()
            best_i = int(np.argmax(confs))
            box = tuple(int(v) for v in xyxy[best_i])
            conf = float(confs[best_i])

        raw_visible = visible and conf >= SWIPE_MIN_CONF
        if raw_visible:
            self._vis_hits = min(self._vis_hits + 1, 30)
            self._vis_miss = 0
        else:
            self._vis_miss = min(self._vis_miss + 1, 30)
            if self._vis_miss >= VIS_MISS_OFF:
                self._vis_hits = 0

        was_stable = self.object_visible
        if self._vis_hits >= VIS_HITS_ON:
            self.object_visible = True
        elif self._vis_miss >= VIS_MISS_OFF:
            self.object_visible = False

        if self._frames_processed == 1:
            src = f"hub:{self.hub.camera_index}" if self.hub else "local"
            print(f"[YOLO] Inference running ({src}) frame={frame.shape[1]}x{frame.shape[0]}")

        if self._frames_processed % LOG_EVERY_N == 0:
            print(
                f"[YOLO] frames={self._frames_processed}  visible={self.object_visible}  "
                f"raw={raw_visible}  conf={conf:.2f}  infer_fps={self._infer_fps:.1f}"
            )

        opened_this_frame = False
        if self.object_visible and not was_stable:
            print(f"[YOLO] clock detected  conf={conf:.2f}")
            if (now - self._last_open_menu_time) >= OPEN_MENU_COOLDOWN:
                self._queue_gesture("close", conf, "open menu")
                self._last_open_menu_time = now
                opened_this_frame = True

        if self.object_visible:
            self._last_seen_clock = now
            self._idle_close_sent = False
            self._was_visible = True
            if raw_visible and box is not None:
                self._hold_box = box
                self._hold_conf = conf
            swipe_box = box if (raw_visible and box is not None) else self._hold_box
            swipe_conf = conf if (raw_visible and box is not None) else self._hold_conf
            swipe = self._swipe.update(swipe_box, swipe_conf if swipe_box else 0.0)
            if (
                swipe
                and (now - self._last_event_time) >= GESTURE_COOLDOWN
                and (now - self._last_open_menu_time) >= SWIPE_BLOCK_AFTER_OPEN_SEC
            ):
                self._queue_gesture(swipe, swipe_conf or conf, f"pixel {swipe}")
        else:
            if was_stable:
                print("[YOLO] clock lost (stable)")
            self._hold_box = None
            self._hold_conf = 0.0
            self._swipe.update(None, 0.0)
            if self._was_visible and self._last_seen_clock > 0:
                if (now - self._last_seen_clock) >= IDLE_CLOSE_SEC and not self._idle_close_sent:
                    self._queue_gesture("close_menu", 1.0, f"idle {IDLE_CLOSE_SEC:.0f}s")
                    self._idle_close_sent = True
                    self._was_visible = False

        self._scan_ambient(frame)

    def inference_loop(self):
        print("[YOLO] Hub inference thread started")
        while self.is_tracking:
            try:
                if self.hub is None:
                    time.sleep(0.02)
                    continue
                frame = self.hub.get_frame()
                if frame is None:
                    now = time.time()
                    if now - self._no_frame_warn_at > 5.0:
                        print("[YOLO] WARNING: CameraHub returned no frame (waiting…)")
                        self._no_frame_warn_at = now
                    time.sleep(0.01)
                    continue
                self._no_frame_warn_at = 0.0
                self._process_frame(frame)
            except Exception as e:
                print(f"[YOLO] inference_loop error: {e}")
                traceback.print_exc()
                time.sleep(0.05)
        print("[YOLO] Hub inference thread stopped")

    def _ensure_infer_thread(self):
        if self._infer_thread is not None and self._infer_thread.is_alive():
            return
        if self.hub is not None:
            self._infer_thread = threading.Thread(
                target=self.inference_loop, daemon=True, name="YoloHubInfer",
            )
            self._infer_thread.start()

    def begin_tracking(self):
        """Start processing — call at server boot (hub) and on C# START_TRACKING."""
        if self.hub is None and not self.start_camera():
            return False
        self.is_tracking = True
        self._ensure_infer_thread()
        return True

    def cmd_start_tracking(self):
        if not self.begin_tracking():
            return {"status": "error", "message": "Cannot open camera"}
        return {"status": "ok", "message": "Tracking started"}

    def cmd_stop_tracking(self):
        self.is_tracking = False
        self.stop_camera()
        return {"status": "ok", "message": "Tracking stopped"}

    def cmd_recognize(self):
        with self._lock:
            if not self._gesture_queue:
                return {"status": "ok", "gesture": None, "score": 0.0}
            name, score, _detail = self._gesture_queue.popleft()
            if not self._gesture_queue:
                self.last_gesture = None
                self.last_score = 0.0

        action = {
            "close": "open_menu",
            "close_menu": "close_menu",
            "swipe_right": "navigate_right",
            "swipe_left": "navigate_left",
            "swipe_up": "confirm",
            "swipe_down": "cancel",
        }.get(name, name)

        return {
            "status": "ok",
            "gesture": name,
            "action": action,
            "score": round(score, 4),
            "confidence": "high",
        }

    def cmd_status(self):
        now = time.time()
        with self._lock:
            visible = self.object_visible
            last_seen = self._last_seen_clock
            q_len = len(self._gesture_queue)
            last_g = self.last_gesture
            amb = dict(self._ambient)
        since = (now - last_seen) if last_seen > 0 else -1.0
        return {
            "status": "ok",
            "tracking": self.is_tracking,
            "object_visible": visible,
            "clock_priority_active": visible or (last_seen > 0 and since < IDLE_CLOSE_SEC),
            "seconds_since_clock": round(since, 2) if since >= 0 else None,
            "ambient_phone": amb.get("phone", False),
            "ambient_book": amb.get("book", False),
            "ambient_large_person": amb.get("large_person", False),
            "gesture_queue_len": q_len,
            "last_gesture": last_g,
            "infer_fps": round(self._infer_fps, 1),
            "frames_processed": self._frames_processed,
            "idle_close_sec": IDLE_CLOSE_SEC,
            "frames_collected": 60,
            "templates": 4,
            "waiting_for_motion": False,
            "capturing": self.object_visible,
            "camera": "hub" if self.hub else "local",
        }

    def cmd_reset(self):
        with self._lock:
            self._gesture_queue.clear()
            self.last_gesture = None
            self.last_score = 0.0
        self._swipe.reset()
        self._was_visible = False
        self._idle_close_sent = False
        self.begin_tracking()
        return {"status": "ok", "message": "Reset complete"}

    def cleanup(self):
        self.is_tracking = False
        self.stop_camera()


class YoloObjectService:
    def __init__(self, host: str = HOST, port: int = PORT, camera_hub=None):
        self.host = host
        self.port = port
        self.camera_hub = camera_hub
        self.is_running = False
        self.server_socket = None
        self._engine: Optional[_YoloEngine] = None

    def start_server(self):
        global _owned_hub, _hub

        hub = self.camera_hub if self.camera_hub is not None else _hub
        if hub is None and _HUB_OK:
            try:
                idx = int(os.environ.get("MUSEUM_CAMERA", "0"))
                hub = CameraHub(camera_index=idx)
                hub.start()
                _hub = hub
                _owned_hub = True
                print(f"[YOLO] Standalone CameraHub started (camera={idx})")
            except Exception as e:
                print(f"[YOLO] CameraHub failed: {e}")

        if hub is not None:
            print(f"[YOLO] Using shared CameraHub (camera index {hub.camera_index})")
        else:
            print("[YOLO] WARNING: no CameraHub — will use local camera on START_TRACKING")

        try:
            eng = _YoloEngine(camera_hub=hub)
            eng.load_model()
            self._engine = eng
            if hub is not None:
                eng.begin_tracking()
        except Exception as e:
            print(f"[YOLO] ERROR loading model: {e}")
            traceback.print_exc()
            return

        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        try:
            self.server_socket.bind((self.host, self.port))
        except OSError as e:
            print(f"[YOLO] ERROR bind {self.host}:{self.port}: {e}")
            return

        self.server_socket.listen(5)
        self.is_running = True
        print(f"[YOLO] Listening on {self.host}:{self.port}  model={MODEL}")
        print(f"[YOLO] idle_close={IDLE_CLOSE_SEC}s  clock_class={CLOCK_CLASS}")

        try:
            while self.is_running:
                try:
                    client_sock, addr = self.server_socket.accept()
                    print(f"[YOLO] C# connected from {addr}")
                except OSError:
                    break
                threading.Thread(
                    target=self._handle_client,
                    args=(client_sock,),
                    daemon=True,
                ).start()
        finally:
            self.cleanup()

    def _handle_client(self, client_sock: socket.socket):
        eng = self._engine
        if eng is None:
            client_sock.close()
            return

        def send(obj):
            try:
                client_sock.sendall((json.dumps(obj) + "\n").encode("utf-8"))
            except Exception:
                pass

        buf = b""
        try:
            eng.begin_tracking()
            while self.is_running:
                chunk = client_sock.recv(1024)
                if not chunk:
                    break
                buf += chunk
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    cmd = line.decode("utf-8", errors="replace").strip().upper()
                    if not cmd:
                        continue
                    if cmd == "START_TRACKING":
                        send(eng.cmd_start_tracking())
                    elif cmd == "STOP_TRACKING":
                        send(eng.cmd_stop_tracking())
                    elif cmd == "RECOGNIZE":
                        send(eng.cmd_recognize())
                    elif cmd == "STATUS":
                        send(eng.cmd_status())
                    elif cmd == "RESET":
                        send(eng.cmd_reset())
                    elif cmd in ("PAUSE_DETECTION", "RESUME_DETECTION"):
                        send({"status": "ok"})
                    elif cmd == "PING":
                        send({"status": "ok", "message": "pong"})
                    else:
                        send({"status": "ok", "message": f"Unknown: {cmd}"})
        except Exception as e:
            print(f"[YOLO] client error: {e}")
        finally:
            try:
                client_sock.close()
            except Exception:
                pass
            print("[YOLO] C# disconnected")

    def cleanup(self):
        self.is_running = False
        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception:
                pass
        if self._engine:
            self._engine.cleanup()
        global _owned_hub, _hub
        if _owned_hub and _hub is not None:
            try:
                _hub.stop()
            except Exception:
                pass
            _hub = None
            _owned_hub = False
        print("[YOLO] Service stopped")


def start(host: str = HOST, port: int = PORT, camera_hub=None):
    """Entry point for main.py — pass the shared hub explicitly."""
    global _hub
    # main.py used to call start(hub) positionally; accept CameraHub as first arg.
    if camera_hub is None and host is not None and not isinstance(host, str):
        camera_hub = host
        host = HOST
    if camera_hub is not None:
        _hub = camera_hub
    if _hub is None and camera_hub is None:
        print("[YOLO] WARNING: start() called without CameraHub")
    svc = YoloObjectService(host=host, port=port, camera_hub=camera_hub or _hub)
    svc.start_server()


def main():
    print("=" * 55)
    print("Smart Museum — YOLO Watch / Object Swipe Server")
    print("=" * 55)
    start()


if __name__ == "__main__":
    main()
