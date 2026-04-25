#!/usr/bin/env python3
"""
Single process: shared webcam hub + all museum Python TCP services.

  127.0.0.1:5000  Face ID + Bluetooth (python_server — same as running python_server.py alone)
  127.0.0.1:5001  dollarpy gesture (GestureRecognitionService)
  127.0.0.1:5002  gaze + emotion (gaze_emotion_service)
  127.0.0.1:5003  YOLO context (yolo_context_service)

C# connects to 5000 (AuthIntegration) and 5001–5003 (TuioDemo) as before — no second terminal for Face ID.

Camera index: MUSEUM_CAMERA, or GAZE_EMOTION_CAMERA, or GESTURE_CAMERA (first set wins).
Mirror: GAZE_EMOTION_MIRROR=1 flips frames inside the hub for all consumers.

Run from repo root (recommended so ultralytics can cache weights):

  python python/server/museum_vision_server.py
"""

from __future__ import annotations

import os
import sys
import threading
import time

THIS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(os.path.dirname(THIS_DIR))
DOLLARPY_DIR = os.path.join(PROJECT_ROOT, "dollarpy-service")

if DOLLARPY_DIR not in sys.path:
    sys.path.insert(0, DOLLARPY_DIR)
if THIS_DIR not in sys.path:
    sys.path.insert(0, THIS_DIR)

from shared_camera_hub import SharedCameraHub, default_camera_index, default_mirror


def main():
    idx = default_camera_index()
    mirror = default_mirror()
    hub = SharedCameraHub(camera_index=idx, mirror=mirror)

    import gaze_emotion_service as ge
    import yolo_context_service as yc

    ge.set_shared_camera_hub(hub)
    yc.set_shared_camera_hub(hub)

    from gesture_service import GestureRecognitionService
    import python_server as face_bt_server

    face_port = int(os.environ.get("PYTHON_SERVER_PORT", "5000"))

    def run_face_bluetooth():
        face_bt_server.start_server("127.0.0.1", face_port)

    threading.Thread(target=run_face_bluetooth, name="faceid-tcp", daemon=True).start()

    def run_gesture():
        GestureRecognitionService(host="127.0.0.1", port=5001, camera_hub=hub).start_server()

    threading.Thread(target=run_gesture, name="gesture-tcp", daemon=True).start()
    threading.Thread(
        target=lambda: ge.run_tcp_server("127.0.0.1", 5002),
        name="gaze-tcp",
        daemon=True,
    ).start()
    threading.Thread(
        target=lambda: yc.run_tcp_server("127.0.0.1", 5003),
        name="yolo-tcp",
        daemon=True,
    ).start()

    print(
        "museum_vision_server: camera %s | Face ID/Bluetooth :%s | gesture :5001 | gaze :5002 | yolo :5003"
        % (idx, face_port)
    )
    print("Ctrl+C to exit (stops all listeners and releases the webcam).")
    try:
        while True:
            time.sleep(1.0)
    except KeyboardInterrupt:
        print("\nmuseum_vision_server: shutting down…")
    finally:
        try:
            ge.stop_gaze_face_loop()
        except Exception:
            pass
        time.sleep(0.25)
        ge.clear_shared_camera_hub()
        yc.clear_shared_camera_hub()
        hub.shutdown()


if __name__ == "__main__":
    os.chdir(PROJECT_ROOT)
    main()
