"""
TCP service (default localhost:5002) streaming gaze + 7-emotion estimates from the webcam.

Protocol (line-based UTF-8, newline-terminated):
  PING        -> {"status":"ok"}\n
  STREAM      -> {"status":"ok"}\n then repeated frame JSON lines until PAUSE or disconnect
  PAUSE       -> {"status":"ok"}\n  (stops streaming; camera loop may keep running idle)

Each frame line is a JSON object, for example:
  {"ok":true,"t_ms":12345,"gx":0.52,"gy":0.41,"dominant":"neutral",
   "emotions":{"angry":0.02,"disgust":0.01,"fear":0.03,"happy":0.15,
               "sad":0.04,"surprise":0.05,"neutral":0.70}}

gx, gy are normalized approximations of where the user is looking on a 1x1 "screen"
(derived from head pose + iris offset). When no face is visible, ok=false.

Dependencies: opencv-python, mediapipe (see python/requirements.txt).
Optional: pip install deepface tf-keras  — if available, emotions use DeepFace; else landmarks heuristic.

Env: GAZE_EMOTION_CAMERA (default 0), GAZE_EMOTION_MIRROR=1 to horizontal-flip frames,
     dollarpy gesture_service also uses a webcam — use GESTURE_CAMERA vs GAZE_EMOTION_CAMERA
     or stop gesture_service while testing gaze (only one process can use one USB camera reliably).
"""

import json
import os
import select
import socket
import sys
import threading
import time

import cv2
import numpy as np

try:
    import mediapipe as mp

    _MP_OK = True
except Exception as e:
    _MP_OK = False
    mp = None  # type: ignore
    print("gaze_emotion_service: mediapipe import failed:", e)

try:
    from mediapipe.tasks import python as _mp_tasks_python
    from mediapipe.tasks.python import vision as _mp_tasks_vision

    _MP_TASKS_VISION_OK = True
except Exception as e:
    _MP_TASKS_VISION_OK = False
    print("gaze_emotion_service: mediapipe tasks.vision import failed:", e)

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_FACE_MODEL_PATH = os.path.join(_SCRIPT_DIR, "face_landmarker.task")
_FACE_MODEL_URL = (
    "https://storage.googleapis.com/mediapipe-models/face_landmarker/"
    "face_landmarker/float16/1/face_landmarker.task"
)


def _ensure_face_landmarker_model():
    if os.path.isfile(_FACE_MODEL_PATH) and os.path.getsize(_FACE_MODEL_PATH) > 1000:
        return _FACE_MODEL_PATH
    import urllib.request

    print("gaze_emotion_service: downloading face_landmarker.task ...")
    urllib.request.urlretrieve(_FACE_MODEL_URL, _FACE_MODEL_PATH)
    print(f"gaze_emotion_service: face model saved to {_FACE_MODEL_PATH}")
    return _FACE_MODEL_PATH


class _FaceLandmarksList:
    """Mimics solutions FaceMesh multi_face_landmarks[0] (.landmark[i].x/y/z)."""

    __slots__ = ("landmark",)

    def __init__(self, landmarks):
        self.landmark = landmarks


class _FaceMeshTasksWrapper:
    """FaceMesh-like API using mediapipe.tasks FaceLandmarker (MediaPipe 0.10+)."""

    def __init__(self):
        if not _MP_TASKS_VISION_OK:
            raise RuntimeError("mediapipe.tasks.python.vision not available")
        model_path = _ensure_face_landmarker_model()
        base_opts = _mp_tasks_python.BaseOptions(model_asset_path=model_path)
        opts = _mp_tasks_vision.FaceLandmarkerOptions(
            base_options=base_opts,
            running_mode=_mp_tasks_vision.RunningMode.IMAGE,
            num_faces=1,
            min_face_detection_confidence=0.3,
            min_face_presence_confidence=0.3,
            min_tracking_confidence=0.3,
        )
        self._detector = _mp_tasks_vision.FaceLandmarker.create_from_options(opts)

    def process(self, rgb_image):
        class _Res:
            __slots__ = ("multi_face_landmarks",)

            def __init__(self):
                self.multi_face_landmarks = None

        out = _Res()
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_image)
        r = self._detector.detect(mp_image)
        if r.face_landmarks:
            out.multi_face_landmarks = [
                _FaceLandmarksList(list(lms)) for lms in r.face_landmarks
            ]
        return out

    def close(self):
        if self._detector is not None:
            self._detector.close()
            self._detector = None


def _create_face_mesh():
    """Legacy solutions API when present; else Tasks FaceLandmarker."""
    if _MP_OK and hasattr(mp, "solutions"):
        fm = getattr(getattr(mp.solutions, "face_mesh", None), "FaceMesh", None)
        if fm is not None:
            return fm(
                max_num_faces=1,
                refine_landmarks=True,
                min_detection_confidence=0.3,
                min_tracking_confidence=0.3,
            )
    return _FaceMeshTasksWrapper()

try:
    from deepface import DeepFace

    _DEEPFACE_OK = True
except Exception:
    _DEEPFACE_OK = False

# When set by museum_vision_server, _CameraLoop reads frames from hub instead of opening a camera.
_shared_hub = None


def set_shared_camera_hub(hub):
    """Use one SharedCameraHub for frames (museum_vision_server). Pass None to clear."""
    global _shared_hub
    _shared_hub = hub


def clear_shared_camera_hub():
    global _shared_hub
    _shared_hub = None


def stop_gaze_face_loop():
    """Stop the MediaPipe worker thread (used by museum_vision_server on shutdown)."""
    _loop.stop()


# MediaPipe FaceMesh landmark indices (with refine_landmarks=True)
_LM_LEFT_EYE_OUT = 33
_LM_LEFT_EYE_IN = 133
_LM_RIGHT_EYE_IN = 362
_LM_RIGHT_EYE_OUT = 263
_LM_NOSE_TIP = 1
_LM_MOUTH_LEFT = 61
_LM_MOUTH_RIGHT = 291
_LM_FOREHEAD = 10
_LM_CHIN = 152
_LM_LEFT_IRIS = 468
_LM_RIGHT_IRIS = 473


def _open_video_capture(index: int):
    """Prefer DirectShow on Windows; small buffer reduces stale/black frames when sharing USB bandwidth."""
    index = int(index)
    if sys.platform == "win32":
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if cap.isOpened():
            try:
                cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            except Exception:
                pass
            return cap
        try:
            cap.release()
        except Exception:
            pass
    cap = cv2.VideoCapture(index)
    if cap.isOpened():
        try:
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        except Exception:
            pass
    return cap


def _lm_pt(lms, idx, w, h):
    p = lms.landmark[idx]
    return np.array([p.x * w, p.y * h, p.z * w], dtype=np.float64)


def _estimate_gaze_xy(lms, w, h):
    """Returns gx, gy in [0,1] approximating on-screen gaze."""
    le_o = _lm_pt(lms, _LM_LEFT_EYE_OUT, w, h)
    le_i = _lm_pt(lms, _LM_LEFT_EYE_IN, w, h)
    re_i = _lm_pt(lms, _LM_RIGHT_EYE_IN, w, h)
    re_o = _lm_pt(lms, _LM_RIGHT_EYE_OUT, w, h)

    left_eye_c = (le_o + le_i) * 0.5
    right_eye_c = (re_o + re_i) * 0.5
    left_w = max(1e-6, np.linalg.norm(le_o[:2] - le_i[:2]))
    right_w = max(1e-6, np.linalg.norm(re_o[:2] - re_i[:2]))

    li = _lm_pt(lms, _LM_LEFT_IRIS, w, h)
    ri = _lm_pt(lms, _LM_RIGHT_IRIS, w, h)

    off_lx = (li[0] - left_eye_c[0]) / left_w
    off_ly = (li[1] - left_eye_c[1]) / left_w
    off_rx = (ri[0] - right_eye_c[0]) / right_w
    off_ry = (ri[1] - right_eye_c[1]) / right_w

    off_x = (off_lx + off_rx) * 0.5
    off_y = (off_ly + off_ry) * 0.5

    nose = _lm_pt(lms, _LM_NOSE_TIP, w, h)
    forehead = _lm_pt(lms, _LM_FOREHEAD, w, h)
    chin = _lm_pt(lms, _LM_CHIN, w, h)
    face_h = max(1e-6, np.linalg.norm(forehead[:2] - chin[:2]))
    pitch = (nose[1] - (forehead[1] + chin[1]) * 0.5) / face_h
    yaw = (nose[0] - (le_o[0] + re_o[0]) * 0.5) / max(1e-6, abs(re_o[0] - le_o[0]))

    gx = 0.5 + 0.55 * off_x + 0.35 * yaw
    gy = 0.5 + 0.55 * off_y + 0.25 * pitch
    gx = float(max(0.0, min(1.0, gx)))
    gy = float(max(0.0, min(1.0, gy)))
    return gx, gy


def _heuristic_emotions(lms, w, h):
    """Rough 7-way distribution from facial geometry (no ML backend)."""
    ml = _lm_pt(lms, _LM_MOUTH_LEFT, w, h)
    mr = _lm_pt(lms, _LM_MOUTH_RIGHT, w, h)
    mouth_w = max(1e-6, np.linalg.norm(ml[:2] - mr[:2]))
    upper_lip = _lm_pt(lms, 13, w, h)
    lower_lip = _lm_pt(lms, 14, w, h)
    mouth_open = max(0.0, lower_lip[1] - upper_lip[1]) / mouth_w

    brow_l = _lm_pt(lms, 107, w, h)
    brow_r = _lm_pt(lms, 336, w, h)
    eye_l = _lm_pt(lms, 159, w, h)
    eye_r = _lm_pt(lms, 386, w, h)
    brow_drop = ((brow_l[1] + brow_r[1]) * 0.5 - (eye_l[1] + eye_r[1]) * 0.5) / mouth_w

    mouth_curve = (ml[1] + mr[1]) * 0.5 - (upper_lip[1] + lower_lip[1]) * 0.5
    smile = -mouth_curve / mouth_w

    scores = {
        "angry": max(0.0, brow_drop * 2.2 + mouth_open * 0.2),
        "disgust": max(0.0, -smile * 0.4 + mouth_open * 0.15),
        "fear": max(0.0, mouth_open * 1.4 + max(0.0, -brow_drop) * 0.8),
        "happy": max(0.0, smile * 2.0),
        "sad": max(0.0, -smile * 0.9 + brow_drop * 0.5),
        "surprise": max(0.0, mouth_open * 1.8 + max(0.0, -brow_drop) * 0.6),
        "neutral": 0.25,
    }
    s = sum(scores.values())
    if s <= 0:
        return {k: 1.0 / 7.0 for k in scores}
    return {k: float(v / s) for k, v in scores.items()}


def _deepface_emotions_bgr(frame_bgr):
    """Returns dict of 7 emotions 0..1 or None."""
    if not _DEEPFACE_OK:
        return None
    try:
        r = DeepFace.analyze(
            frame_bgr,
            actions=["emotion"],
            enforce_detection=False,
            silent=True,
        )
        if isinstance(r, list):
            r = r[0]
        emo = r.get("emotion") or {}
        out = {}
        for k in ("angry", "disgust", "fear", "happy", "sad", "surprise", "neutral"):
            key = None
            for ek in emo.keys():
                if str(ek).lower() == k:
                    key = ek
                    break
            if key is not None:
                v = float(emo[key])
                out[k] = v / 100.0 if v > 1.5 else v
        if not out:
            return None
        s = sum(max(0.0, v) for v in out.values()) or 1.0
        return {k: float(max(0.0, out.get(k, 0.0)) / s) for k in ("angry", "disgust", "fear", "happy", "sad", "surprise", "neutral")}
    except Exception:
        return None


class _CameraLoop:
    def __init__(self):
        self.lock = threading.Lock()
        self.latest = None  # dict or None
        self.running = False
        self.cap = None
        self.face_mesh = None
        self._thread = None
        self._t0 = None
        self._frame_i = 0

    def start(self):
        with self.lock:
            if self.running:
                return
            self.running = True
            self._t0 = time.time() * 1000.0
            self._thread = threading.Thread(target=self._run, daemon=True)
            self._thread.start()

    def stop(self):
        with self.lock:
            self.running = False
        if self._thread is not None:
            self._thread.join(timeout=2.0)
            self._thread = None
        if self.cap is not None:
            self.cap.release()
            self.cap = None
        if self.face_mesh is not None:
            self.face_mesh.close()
            self.face_mesh = None

    def _process_frame(self, frame, t_ms):
        """BGR frame → updates self.latest under lock."""
        h, w = frame.shape[:2]
        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        res = self.face_mesh.process(rgb)
        self._frame_i += 1

        if not res.multi_face_landmarks:
            with self.lock:
                self.latest = {"ok": False, "t_ms": t_ms, "reason": "no_face"}
            return

        lms = res.multi_face_landmarks[0]
        gx, gy = _estimate_gaze_xy(lms, w, h)

        emotions = None
        if _DEEPFACE_OK and self._frame_i % 4 == 0:
            emotions = _deepface_emotions_bgr(frame)
        if emotions is None:
            emotions = _heuristic_emotions(lms, w, h)

        dominant = max(emotions.items(), key=lambda kv: kv[1])[0]
        with self.lock:
            self.latest = {
                "ok": True,
                "t_ms": t_ms,
                "gx": gx,
                "gy": gy,
                "emotions": emotions,
                "dominant": dominant,
            }

    def _run(self):
        global _shared_hub
        if not _MP_OK:
            with self.lock:
                self.latest = {"ok": False, "t_ms": 0, "reason": "mediapipe_missing"}
            print("gaze_emotion_service: mediapipe not available — pip install mediapipe")
            return

        hub = _shared_hub
        mirror = os.environ.get("GAZE_EMOTION_MIRROR", "").strip().lower() in (
            "1",
            "true",
            "yes",
        )
        try:
            self.face_mesh = _create_face_mesh()
        except Exception as e:
            print("gaze_emotion_service: failed to init face mesh / landmarker:", e)
            with self.lock:
                self.latest = {
                    "ok": False,
                    "t_ms": 0,
                    "reason": "mediapipe_init_failed",
                    "detail": str(e)[:240],
                }
            return

        if hub is not None:
            print("gaze_emotion_service: face loop using SharedCameraHub (no local VideoCapture)")
            while True:
                with self.lock:
                    if not self.running:
                        break
                frame = hub.get_latest_bgr_copy()
                if frame is None:
                    time.sleep(0.012)
                    continue
                # Hub already applied GAZE_EMOTION_MIRROR when configured there.
                t_ms = int(time.time() * 1000.0 - (self._t0 or 0.0))
                self._process_frame(frame, t_ms)
            if self.face_mesh is not None:
                self.face_mesh.close()
                self.face_mesh = None
            return

        cam_idx = int(os.environ.get("GAZE_EMOTION_CAMERA", "0"))
        self.cap = _open_video_capture(cam_idx)
        if not self.cap.isOpened():
            with self.lock:
                self.latest = {
                    "ok": False,
                    "t_ms": 0,
                    "reason": "camera_failed",
                    "detail": cam_idx,
                }
            print(
                f"gaze_emotion_service: cannot open camera index {cam_idx} "
                f"(try GAZE_EMOTION_CAMERA=1 or close other apps using the webcam)"
            )
            if self.face_mesh is not None:
                self.face_mesh.close()
                self.face_mesh = None
            return
        print(
            f"gaze_emotion_service: camera index {cam_idx} opened "
            f"(if gesture_service.py uses the same index, stop gestures or set a different GAZE_EMOTION_CAMERA)"
        )
        while True:
            with self.lock:
                if not self.running:
                    break
            ok, frame = self.cap.read()
            if not ok:
                time.sleep(0.02)
                continue
            if mirror:
                frame = cv2.flip(frame, 1)
            t_ms = int(time.time() * 1000.0 - (self._t0 or 0.0))
            self._process_frame(frame, t_ms)
        if self.cap is not None:
            self.cap.release()
            self.cap = None
        if self.face_mesh is not None:
            self.face_mesh.close()
            self.face_mesh = None


_loop = _CameraLoop()


def _drain_commands(conn, buf):
    """Non-blocking read; returns (updated_buf, last_command_or_none). command is upper stripped line."""
    try:
        r, _, _ = select.select([conn], [], [], 0)
    except (ValueError, OSError):
        return buf, None
    if not r:
        return buf, None
    try:
        chunk = conn.recv(4096)
    except (BlockingIOError, ConnectionError, OSError):
        return buf, None
    if not chunk:
        return buf, "__CLOSED__"
    buf += chunk
    last_cmd = None
    while b"\n" in buf:
        line, buf = buf.split(b"\n", 1)
        s = line.decode("utf-8", errors="ignore").strip().upper()
        if s:
            last_cmd = s
    return buf, last_cmd


def handle_client(conn, addr):
    global _shared_hub
    print("gaze_emotion client:", addr)
    streaming = False
    buf = b""
    hub_gaze_id = f"gaze:{addr}"
    hub_acquired = False
    try:
        conn.setblocking(True)
        while True:
            if not streaming:
                chunk = conn.recv(4096)
                if not chunk:
                    break
                buf += chunk
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    cmd = line.decode("utf-8", errors="ignore").strip().upper()
                    if not cmd:
                        continue
                    if cmd == "PING":
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                    elif cmd == "STREAM":
                        streaming = True
                        if _shared_hub is not None:
                            _shared_hub.acquire(hub_gaze_id)
                            hub_acquired = True
                        _loop.start()
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                        conn.setblocking(False)
                    elif cmd == "PAUSE":
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                    elif cmd == "QUIT":
                        conn.sendall((json.dumps({"status": "bye"}) + "\n").encode("utf-8"))
                        return
            else:
                buf, cmd = _drain_commands(conn, buf)
                if cmd == "__CLOSED__":
                    break
                if cmd == "PAUSE":
                    streaming = False
                    conn.setblocking(True)
                    conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                    continue
                if cmd == "QUIT":
                    conn.setblocking(True)
                    conn.sendall((json.dumps({"status": "bye"}) + "\n").encode("utf-8"))
                    return

                with _loop.lock:
                    if _loop.latest is not None:
                        snap = dict(_loop.latest)
                    else:
                        snap = {"ok": False, "t_ms": 0, "reason": "warmup"}
                try:
                    conn.sendall((json.dumps(snap) + "\n").encode("utf-8"))
                except (BrokenPipeError, ConnectionError, OSError):
                    break
                time.sleep(0.066)

    except Exception as e:
        print("gaze_emotion client error:", e)
    finally:
        if hub_acquired and _shared_hub is not None:
            try:
                _shared_hub.release(hub_gaze_id)
            except Exception:
                pass
        try:
            conn.close()
        except Exception:
            pass
        print("gaze_emotion disconnected:", addr)


def run_tcp_server(host, port):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(8)
    print(f"gaze_emotion_service listening on {host}:{port}")
    try:
        while True:
            c, a = srv.accept()
            threading.Thread(target=handle_client, args=(c, a), daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
        _loop.stop()


if __name__ == "__main__":
    run_tcp_server("127.0.0.1", 5002)
