"""
TCP bridge for object swipes (port 5005) — GestureClient-compatible with C#.
Separate from hand-gesture service on port 5001.
"""

from __future__ import annotations

import json
import socket
import threading

_obj_gesture: str | None = None
_obj_visible = False
_lock = threading.Lock()


def set_object_gesture(name: str | None):
    global _obj_gesture
    with _lock:
        _obj_gesture = name


def set_object_visible(visible: bool):
    global _obj_visible
    with _lock:
        _obj_visible = visible


def start_object_swipe_server(host: str = "127.0.0.1", port: int = 5005):
    threading.Thread(target=_serve, args=(host, port), daemon=True).start()


def _serve(host: str, port: int):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        srv.bind((host, port))
        srv.listen(5)
        print(f"[ObjectSwipe] TCP server on {host}:{port}")
    except OSError as e:
        print(f"[ObjectSwipe] Could not bind {host}:{port}: {e}")
        return

    while True:
        try:
            conn, _ = srv.accept()
            _handle_client(conn)
        except Exception:
            pass


def _handle_client(conn: socket.socket):
    buf = ""
    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break
            buf += data.decode("utf-8", errors="replace")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                cmd = line.strip()
                if not cmd:
                    continue
                resp = _dispatch(cmd)
                conn.sendall((json.dumps(resp) + "\n").encode("utf-8"))
    except Exception:
        pass
    finally:
        conn.close()


def _dispatch(cmd: str) -> dict:
    global _obj_gesture
    if cmd == "STATUS":
        with _lock:
            g = _obj_gesture
            vis = _obj_visible
        return {
            "status": "ok",
            "tracking": True,
            "last_gesture": g,
            "frames_collected": 60,
            "templates": 4,
            "waiting_for_motion": False,
            "capturing": True,
            "object_visible": vis,
        }
    if cmd == "RECOGNIZE":
        with _lock:
            g = _obj_gesture
            _obj_gesture = None
        return {"status": "ok", "gesture": g, "score": 1.0, "confidence": "high"}
    return {"status": "ok"}
