#!/usr/bin/env python3
"""
Object Tracking Service — port 5005

Mirrors app.py exactly:
  - Opens frames from CameraHub (shared, no camera conflict)
  - YOLO11s class 74 (watches/clocks), ByteTrack
  - Sliding-window swipe detection (same constants as app.py)
  - TCP server for C# ObjectTrackingClient

TCP commands:
  START_TRACKING  → start processing frames
  STOP_TRACKING   → pause
  RECOGNIZE       → return {gesture, object_visible}, clears gesture
  STATUS          → return tracking state
  RESET / PING    → {"status":"ok"}
"""

import threading
import time
import socket
import json
import os

# ── Shared state ──────────────────────────────────────────────────────────────
_camera_hub      = None
_lock            = threading.Lock()
_obj_gesture     = None   # cleared on RECOGNIZE
_obj_visible     = False  # True while watch is in frame
_tracking_active = False  # set by START_TRACKING / STOP_TRACKING
_tracking_lock   = threading.Lock()

# ── Swipe tuning — same as app.py ─────────────────────────────────────────────
SWIPE_WINDOW    = 15    # frames in sliding window
SWIPE_THRESHOLD = 80    # pixels displacement to trigger swipe
SWIPE_COOLDOWN  = 0.8   # seconds between swipes
MIN_CONF        = 0.40  # minimum YOLO confidence to track position


def set_hub(hub):
    global _camera_hub
    _camera_hub = hub


# ── TCP server ────────────────────────────────────────────────────────────────

def _handle_client(conn, addr):
    global _tracking_active, _obj_gesture, _obj_visible
    print(f"[ObjectTracking] Client connected: {addr}")
    buf = ""
    try:
        while True:
            try:
                data = conn.recv(4096)
            except (ConnectionResetError, ConnectionAbortedError, OSError):
                break
            if not data:
                break

            buf += data.decode("utf-8", errors="ignore")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                cmd = line.strip()
                if not cmd:
                    continue

                if cmd == "START_TRACKING":
                    with _tracking_lock:
                        _tracking_active = True
                    print("[ObjectTracking] Tracking STARTED")
                    resp = {"status": "ok"}

                elif cmd == "STOP_TRACKING":
                    with _tracking_lock:
                        _tracking_active = False
                    print("[ObjectTracking] Tracking STOPPED")
                    resp = {"status": "ok"}

                elif cmd == "RECOGNIZE":
                    with _lock:
                        g   = _obj_gesture
                        _obj_gesture = None
                        vis = _obj_visible
                    resp = {
                        "status":         "ok",
                        "gesture":        g,
                        "score":          1.0,
                        "confidence":     "high",
                        "object_visible": vis,
                    }

                elif cmd == "STATUS":
                    with _lock:
                        g   = _obj_gesture
                        vis = _obj_visible
                    with _tracking_lock:
                        active = _tracking_active
                    resp = {
                        "status":             "ok",
                        "tracking":           active,
                        "last_gesture":       g,
                        "frames_collected":   60,
                        "templates":          4,
                        "waiting_for_motion": False,
                        "capturing":          active,
                        "object_visible":     vis,
                    }

                elif cmd == "RESET":
                    with _lock:
                        _obj_gesture = None
                    resp = {"status": "ok"}

                else:  # PING and anything unknown
                    resp = {"status": "ok"}

                try:
                    conn.sendall((json.dumps(resp) + "\n").encode("utf-8"))
                except (BrokenPipeError, ConnectionError, OSError):
                    return

    except Exception as e:
        print(f"[ObjectTracking] Client error {addr}: {e}")
    finally:
        try:
            conn.close()
        except Exception:
            pass
        print(f"[ObjectTracking] Client disconnected: {addr}")


def _tcp_server():
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        srv.bind(("127.0.0.1", 5005))
        srv.listen(5)
        print("[ObjectTracking] TCP server listening on port 5005")
    except Exception as e:
        print(f"[ObjectTracking] Could not bind port 5005: {e}")
        return
    while True:
        try:
            conn, addr = srv.accept()
            threading.Thread(target=_handle_client, args=(conn, addr),
                             daemon=True).start()
        except Exception as e:
            print(f"[ObjectTracking] Accept error: {e}")
            time.sleep(0.1)


# ── Detection loop ────────────────────────────────────────────────────────────

def start():
    global _obj_gesture, _obj_visible

    threading.Thread(target=_tcp_server, daemon=True).start()

    # Resolve model path: project root is two levels above this file
    this_dir     = os.path.dirname(os.path.abspath(__file__))
    project_root = os.path.dirname(os.path.dirname(this_dir))
    model_path   = os.path.join(project_root, "yolo11s.pt")
    if not os.path.exists(model_path):
        model_path = "yolo11s.pt"  # fallback to cwd

    print(f"[ObjectTracking] Loading YOLO11s from: {model_path}")
    try:
        from ultralytics import YOLO
        model = YOLO(model_path)
        print("[ObjectTracking] YOLO model loaded OK")
    except Exception as e:
        print(f"[ObjectTracking] ERROR loading model: {e}")
        return

    print("[ObjectTracking] Ready — waiting for START_TRACKING from C#")

    track_history   = []
    last_swipe_time = 0.0
    prev_visible    = False

    while True:
        try:
            # Wait for C# to enable tracking
            with _tracking_lock:
                active = _tracking_active
            if not active:
                time.sleep(0.05)
                continue

            # Get frame from hub — same as app.py's cap.read()
            if _camera_hub is None:
                time.sleep(0.1)
                continue
            frame = _camera_hub.get_frame()
            if frame is None:
                time.sleep(0.01)
                continue

            # YOLO inference — identical to app.py
            results = model.track(
                frame,
                persist=True,
                verbose=False,
                classes=[74],
                conf=0.15,
                tracker="bytetrack.yaml",
            )

            object_detected = (
                results[0].boxes is not None and len(results[0].boxes) > 0
            )

            # Update shared visibility flag
            with _lock:
                _obj_visible = object_detected

            # Log detection transitions
            if object_detected and not prev_visible:
                conf = float(results[0].boxes.conf.cpu()[0])
                print(f"[ObjectTracking] Watch DETECTED (conf={conf:.2f})")
            elif not object_detected and prev_visible:
                print("[ObjectTracking] Watch LOST")
            prev_visible = object_detected

            # Swipe detection — identical to app.py
            if object_detected:
                box    = results[0].boxes.xywh.cpu()[0]
                conf   = float(results[0].boxes.conf.cpu()[0])
                cx, cy = float(box[0]), float(box[1])

                if conf >= MIN_CONF:
                    track_history.append((cx, cy))
                    if len(track_history) > 60:
                        track_history.pop(0)

                    now = time.time()
                    if (len(track_history) >= SWIPE_WINDOW
                            and (now - last_swipe_time) > SWIPE_COOLDOWN):

                        dx = track_history[-1][0] - track_history[-SWIPE_WINDOW][0]
                        dy = track_history[-1][1] - track_history[-SWIPE_WINDOW][1]

                        gesture = None
                        if abs(dx) > abs(dy) and abs(dx) > SWIPE_THRESHOLD:
                            gesture = "objectswiperight" if dx > 0 else "objectswipeleft"
                        elif abs(dy) > abs(dx) and abs(dy) > SWIPE_THRESHOLD:
                            gesture = "objectswipedown" if dy > 0 else "objectswipeup"

                        if gesture:
                            with _lock:
                                _obj_gesture = gesture
                            track_history.clear()
                            last_swipe_time = now
                            print(f"[ObjectTracking] SWIPE: {gesture}  dx={dx:.0f}  dy={dy:.0f}")
            # Note: track_history is NOT cleared when watch disappears —
            # only cleared when a swipe fires, so mid-swipe detection gaps don't reset it.

        except Exception as e:
            import traceback
            print(f"[ObjectTracking] Loop error: {e}")
            traceback.print_exc()
            time.sleep(0.1)


if __name__ == "__main__":
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from camera_hub import CameraHub
    hub = CameraHub()
    hub.start()
    set_hub(hub)
    with _tracking_lock:
        _tracking_active = True
    start()
