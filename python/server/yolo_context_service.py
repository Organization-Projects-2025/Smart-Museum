"""
YOLOv8 tracking → JSON over TCP (default 127.0.0.1:5003).

Fits the museum table UX: infer *ambient* context from the webcam — phone (Bluetooth hint),
book/laptop ("reading" warmth + slightly larger body text), large *person* bbox (viewer stepped back).

Protocol (same style as gaze_emotion_service.py):
  PING   → {"status":"ok"}
  STREAM → {"status":"ok"} then one JSON line per frame (~5–10 Hz)
  PAUSE  → {"status":"ok"}

Each frame line:
  {"ok":true,"t_ms":...,"tracks":[{"id":1,"cls":"person","cx":0.5,"cy":0.4,"w":0.3,"h":0.55,"conf":0.82}, ...]}

Env:
  YOLO_CONTEXT_MOCK=1  — deterministic fake tracks (no GPU, no weights). Default if ultralytics unavailable.
  YOLO_CONTEXT_MOCK=0  — real YOLOv8n ByteTrack on camera 0 (requires: pip install ultralytics).
"""

import json
import os
import select
import socket
import threading
import time

PORT = int(os.environ.get("YOLO_CONTEXT_PORT", "5003"))
MOCK = os.environ.get("YOLO_CONTEXT_MOCK", "").strip() in ("1", "true", "yes")
USE_REAL = not MOCK

_t0 = None
_shared_hub = None


def set_shared_camera_hub(hub):
    """When set by museum_vision_server, real YOLO uses hub frames instead of opening camera 0."""
    global _shared_hub
    _shared_hub = hub


def clear_shared_camera_hub():
    global _shared_hub
    _shared_hub = None


def _mock_tracks(phase: int):
    """Rotate synthetic classes so C# can be tested without a camera."""
    phase = phase % 6
    if phase == 0:
        return [{"id": 1, "cls": "person", "cx": 0.5, "cy": 0.45, "w": 0.35, "h": 0.6, "conf": 0.88}]
    if phase == 1:
        return [{"id": 2, "cls": "cell phone", "cx": 0.7, "cy": 0.6, "w": 0.08, "h": 0.14, "conf": 0.77}]
    if phase == 2:
        return [
            {"id": 1, "cls": "person", "cx": 0.5, "cy": 0.5, "w": 0.2, "h": 0.35, "conf": 0.72},
            {"id": 3, "cls": "book", "cx": 0.25, "cy": 0.55, "w": 0.12, "h": 0.1, "conf": 0.68},
        ]
    if phase == 3:
        return [{"id": 4, "cls": "laptop", "cx": 0.4, "cy": 0.5, "w": 0.22, "h": 0.12, "conf": 0.66}]
    if phase == 4:
        return []
    return [{"id": 5, "cls": "person", "cx": 0.5, "cy": 0.5, "w": 0.12, "h": 0.22, "conf": 0.61}]


def _encode_frame(tracks):
    global _t0
    if _t0 is None:
        _t0 = time.time() * 1000.0
    t_ms = int(time.time() * 1000.0 - _t0)
    return json.dumps({"ok": True, "t_ms": t_ms, "tracks": tracks})


def _tracks_from_ultra_result(result):
    tracks = []
    boxes = getattr(result, "boxes", None)
    if boxes is not None and len(boxes) > 0:
        xywhn = boxes.xywhn.cpu().numpy()
        cls_arr = boxes.cls.cpu().numpy()
        conf_arr = boxes.conf.cpu().numpy()
        id_tensor = boxes.id
        id_arr = id_tensor.cpu().numpy() if id_tensor is not None else None
        names = result.names or {}
        for j in range(len(xywhn)):
            cx, cy, w, h = (float(xywhn[j][0]), float(xywhn[j][1]), float(xywhn[j][2]), float(xywhn[j][3]))
            ci = int(cls_arr[j])
            cname = str(names.get(ci, str(ci)))
            conf = float(conf_arr[j])
            tid = int(id_arr[j]) if id_arr is not None else j
            tracks.append(
                {"id": tid, "cls": cname, "cx": cx, "cy": cy, "w": w, "h": h, "conf": conf}
            )
    return tracks


def _run_real_tracker(conn, hub=None):
    try:
        from ultralytics import YOLO
    except Exception as e:
        conn.sendall(
            (json.dumps({"status": "error", "msg": "ultralytics missing: " + str(e)}) + "\n").encode("utf-8")
        )
        return

    model = YOLO("yolov8n.pt")
    try:
        if hub is None:
            for result in model.track(source=0, stream=True, persist=True, verbose=False):
                tracks = _tracks_from_ultra_result(result)
                line = _encode_frame(tracks) + "\n"
                try:
                    conn.sendall(line.encode("utf-8"))
                except (BrokenPipeError, ConnectionError, OSError):
                    break
                time.sleep(0.12)
        else:
            print("yolo_context_service: YOLO track using SharedCameraHub frames")
            drain_buf = b""
            while True:
                drain_buf, cmd = _drain_cmd(conn, drain_buf)
                if cmd == "__CLOSED__":
                    break
                if cmd == "PAUSE":
                    break
                if cmd == "QUIT":
                    break
                frame = hub.get_latest_bgr_copy()
                if frame is None:
                    time.sleep(0.02)
                    continue
                for result in model.track(
                    source=frame, stream=True, persist=True, verbose=False
                ):
                    tracks = _tracks_from_ultra_result(result)
                    line = _encode_frame(tracks) + "\n"
                    try:
                        conn.sendall(line.encode("utf-8"))
                    except (BrokenPipeError, ConnectionError, OSError):
                        return
                    break
                time.sleep(0.12)
    except Exception as ex:
        try:
            conn.sendall((json.dumps({"ok": False, "error": str(ex)}) + "\n").encode("utf-8"))
        except Exception:
            pass


def _drain_cmd(conn, buf):
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
    last = None
    while b"\n" in buf:
        line, buf = buf.split(b"\n", 1)
        s = line.decode("utf-8", errors="ignore").strip().upper()
        if s:
            last = s
    return buf, last


def handle_client(conn, addr):
    global _shared_hub
    print("yolo_context client:", addr)
    streaming = False
    buf = b""
    mock_i = 0
    hub_yolo_id = f"yolo:{addr}"
    hub_acquired = False
    try:
        conn.setblocking(True)
        while True:
            if not streaming:
                data = conn.recv(4096)
                if not data:
                    break
                buf += data
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    cmd = line.decode("utf-8", errors="ignore").strip().upper()
                    if not cmd:
                        continue
                    if cmd == "PING":
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                    elif cmd == "STREAM":
                        streaming = True
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                        if USE_REAL:
                            if _shared_hub is not None:
                                _shared_hub.acquire(hub_yolo_id)
                                hub_acquired = True
                            _run_real_tracker(conn, _shared_hub)
                            return
                        conn.setblocking(False)
                    elif cmd == "PAUSE":
                        conn.sendall((json.dumps({"status": "ok"}) + "\n").encode("utf-8"))
                    elif cmd == "QUIT":
                        conn.sendall((json.dumps({"status": "bye"}) + "\n").encode("utf-8"))
                        return
            else:
                if USE_REAL:
                    break
                buf, cmd = _drain_cmd(conn, buf)
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
                tracks = _mock_tracks(mock_i)
                mock_i += 1
                try:
                    conn.sendall((_encode_frame(tracks) + "\n").encode("utf-8"))
                except (BrokenPipeError, ConnectionError, OSError):
                    break
                time.sleep(0.18)
    except Exception as e:
        print("yolo_context client error:", e)
    finally:
        if hub_acquired and _shared_hub is not None:
            try:
                _shared_hub.release(hub_yolo_id)
            except Exception:
                pass
        try:
            conn.close()
        except Exception:
            pass
        print("yolo_context disconnected:", addr)


def _configure_mode_from_env():
    global USE_REAL, MOCK
    if os.environ.get("YOLO_CONTEXT_MOCK", "").strip() in ("1", "true", "yes"):
        MOCK = True
        USE_REAL = False
    elif os.environ.get("YOLO_CONTEXT_MOCK", "").strip() in ("0", "false", "no"):
        MOCK = False
        USE_REAL = True

    if not MOCK:
        try:
            import ultralytics  # noqa: F401
        except Exception:
            print("ultralytics not installed — falling back to YOLO_CONTEXT_MOCK mode.")
            USE_REAL = False


def run_tcp_server(host=None, port=None):
    global PORT
    _configure_mode_from_env()
    if port is not None:
        PORT = int(port)
    if host is None:
        host = os.environ.get("YOLO_CONTEXT_HOST", "127.0.0.1")
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, PORT))
    srv.listen(8)
    mode = "MOCK" if not USE_REAL else "REAL"
    print(f"yolo_context_service [{mode}] on {host}:{PORT}")
    try:
        while True:
            c, a = srv.accept()
            threading.Thread(target=handle_client, args=(c, a), daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()


def main():
    run_tcp_server()


if __name__ == "__main__":
    main()
