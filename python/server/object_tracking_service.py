"""
object_tracking_service.py — TCP server on port 5005.

Detection logic is IDENTICAL to app.py (background thread, model.track with
ByteTrack, same lock, same global_track, same THRESHOLD=195).
Only difference: uses _hub.get_frame() instead of cv2.VideoCapture (no UI).
"""

import json
import os
import socket
import threading

# ── Camera hub reference (set by main.py) ─────────────────────────────────────
_hub = None

def set_hub(hub):
    global _hub
    _hub = hub

# ── Shared state — identical names to app.py ──────────────────────────────────
_obj_gesture      = None
_obj_visible      = False
_tracking_active  = False        # only True after C# sends START_TRACKING (post-login)
_obj_gesture_lock = threading.Lock()

def set_object_gesture(name):
    global _obj_gesture
    with _obj_gesture_lock:
        _obj_gesture = name

# ── YOLO model ────────────────────────────────────────────────────────────────
_model = None

def _load_model():
    global _model
    try:
        from ultralytics import YOLO
        # Use the model already present in 'object tracking and detection' folder
        candidate = os.path.join(
            os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
            "object tracking and detection", "yolo11s.pt",
        )
        path = candidate if os.path.exists(candidate) else "yolo11s.pt"
        _model = YOLO(path)
        print(f"[ObjectTrack] YOLO11s loaded ({path})")
    except Exception as e:
        print(f"[ObjectTrack] Could not load YOLO: {e} — service will idle.")

# ── Detection loop — identical logic to app.py ────────────────────────────────
def _detection_loop():
    """
    Background thread. Uses _hub.get_frame() instead of cv2.VideoCapture.
    Sleeps until C# sends START_TRACKING (which only happens after login),
    so YOLO is completely idle during face ID authentication.
    """
    global _obj_visible
    import time

    global_track = []

    while True:
        # ── Gate: idle until C# has sent START_TRACKING ───────────────────────
        if not _tracking_active:
            time.sleep(0.1)
            continue

        # Lazy-load YOLO only when tracking starts, avoiding startup collisions
        if _model is None:
            # Delay YOLO initialization to give MediaPipe/Face ID time to initialize 
            # their GPU contexts safely at startup without race conditions.
            import time
            time.sleep(10.0) 
            _load_model()
            if _model is None:
                time.sleep(1.0)
                continue

        try:
            if _hub is None:
                time.sleep(0.1)
                continue

            frame = _hub.get_frame()
            if frame is None:
                import time; time.sleep(0.05)
                continue

            # ── Identical to app.py line 104 ──────────────────────────────────
            results = _model.track(
                frame,
                persist=True,
                verbose=False,
                classes=[74],
                conf=0.15,
                tracker="bytetrack.yaml",
            )

            # ── Identical to app.py lines 111-113 ─────────────────────────────
            object_detected = results[0].boxes is not None and len(results[0].boxes) > 0
            with _obj_gesture_lock:
                _obj_visible = object_detected

            # ── Identical to app.py lines 116-159 ─────────────────────────────
            if object_detected:
                box = results[0].boxes.xywh.cpu()[0]
                center_x = float(box[0])
                center_y = float(box[1])

                global_track.append((center_x, center_y))

                if len(global_track) > 60:
                    global_track.pop(0)

                if len(global_track) >= 5:
                    dx = global_track[-1][0] - global_track[0][0]
                    dy = global_track[-1][1] - global_track[0][1]

                    THRESHOLD = 195

                    if abs(dx) > abs(dy):
                        if dx > THRESHOLD:
                            print("[ObjectTrack] Swipe RIGHT")
                            set_object_gesture("objectswiperight")
                            global_track.clear()
                        elif dx < -THRESHOLD:
                            print("[ObjectTrack] Swipe LEFT")
                            set_object_gesture("objectswipeleft")
                            global_track.clear()
                    else:
                        if dy > THRESHOLD:
                            print("[ObjectTrack] Swipe DOWN")
                            set_object_gesture("objectswipedown")
                            global_track.clear()
                        elif dy < -THRESHOLD:
                            print("[ObjectTrack] Swipe UP")
                            set_object_gesture("objectswipeup")
                            global_track.clear()

        except Exception as e:
            print(f"[ObjectTrack] Detection error: {e}")
            import time; time.sleep(0.5)

# ── TCP server — identical protocol to app.py's _obj_tcp_server() ─────────────
def _handle_client(conn, addr):
    global _tracking_active          # declared here so all branches can assign it
    print(f"[ObjectTrack] Client: {addr}")
    buf = ""
    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break
            buf += data.decode("utf-8", errors="ignore")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                cmd = line.strip()
                if not cmd:
                    continue

                if cmd == "STATUS":
                    # Identical to app.py lines 47-54
                    with _obj_gesture_lock:
                        g   = _obj_gesture
                        vis = _obj_visible
                    resp = {
                        "status": "ok",
                        "tracking": _tracking_active,   # False until START_TRACKING
                        "last_gesture": g, "frames_collected": 60,
                        "templates": 4, "waiting_for_motion": False,
                        "capturing": True, "object_visible": vis,
                    }

                elif cmd == "RECOGNIZE":
                    # Identical to app.py lines 56-60
                    with _obj_gesture_lock:
                        g = _obj_gesture
                        _obj_gesture = None           # clear after read
                    resp = {"status": "ok", "gesture": g,
                            "score": 1.0, "confidence": "high"}

                elif cmd == "START_TRACKING":
                    _tracking_active = True
                    resp = {"status": "ok"}

                elif cmd == "STOP_TRACKING":
                    _tracking_active = False
                    resp = {"status": "ok"}

                else:
                    # PING, RESET, PAUSE_DETECTION, RESUME_DETECTION, etc.
                    resp = {"status": "ok"}

                try:
                    conn.sendall((json.dumps(resp) + "\n").encode("utf-8"))
                except Exception:
                    return

    except Exception as e:
        print(f"[ObjectTrack] Client error {addr}: {e}")
    finally:
        try:
            conn.close()
        except Exception:
            pass
        print(f"[ObjectTrack] Disconnected: {addr}")


def start(host="127.0.0.1", port=5005):
    if os.environ.get("DISABLE_OBJ_TRACK", "").strip() in ("1", "true", "yes"):
        print("[ObjectTrack] Disabled via DISABLE_OBJ_TRACK env var.")
        return

    # Background detection thread (same architecture as app.py)
    # YOLO is lazy-loaded inside this thread when tracking actually begins.
    threading.Thread(target=_detection_loop,
                     name="ObjTrack-Detect", daemon=True).start()

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(8)
    print(f"[ObjectTrack] Listening on {host}:{port}")

    try:
        while True:
            conn, addr = srv.accept()
            threading.Thread(target=_handle_client, args=(conn, addr),
                             daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
