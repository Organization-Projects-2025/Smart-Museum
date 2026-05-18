"""
camera_hub.py — Single shared camera capture thread.

Opens camera index 0 (or MUSEUM_CAMERA env override).
All services call get_frame() to receive the latest BGR frame.
"""

import os
import sys
import threading
import time
from typing import Optional

import cv2
import numpy as np


def _open_cap(index: int) -> cv2.VideoCapture:
    if sys.platform == "win32":
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        if cap.isOpened():
            cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
            return cap
        try: cap.release()
        except Exception:
            pass
    cap = cv2.VideoCapture(index)
    if cap.isOpened():
        cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    return cap


class CameraHub:
    def __init__(self, camera_index: Optional[int] = None):
        if camera_index is None:
            camera_index = int(os.environ.get("MUSEUM_CAMERA", "0"))
        self.camera_index = camera_index
        os.environ["MUSEUM_CAMERA"] = str(camera_index)
        self._lock    = threading.Lock()
        self._frame: Optional[np.ndarray] = None
        self._running = False
        self._thread: Optional[threading.Thread] = None

    def start(self):
        if self._running:
            return
        self._running = True
        self._thread  = threading.Thread(target=self._loop, daemon=True, name="CameraHub")
        self._thread.start()
        print(f"[Camera] Hub started on index {self.camera_index}")

    def stop(self):
        self._running = False
        if self._thread:
            self._thread.join(timeout=3.0)
        self._thread = None
        print("[Camera] Hub stopped")

    def get_frame(self) -> Optional[np.ndarray]:
        with self._lock:
            return self._frame.copy() if self._frame is not None else None

    # Compatibility shims for gesture_service_refactored (SharedCameraHub API)
    def acquire(self, consumer_id: str = "") -> None:  pass
    def release(self, consumer_id: str = "") -> None:  pass
    def get_latest_bgr_copy(self) -> Optional[np.ndarray]: return self.get_frame()

    def _loop(self):
        cap = None
        for attempt in range(20):
            cap = _open_cap(self.camera_index)
            if cap.isOpened():
                ok, frame = cap.read()
                if ok and frame is not None:
                    break
                cap.release()
                cap = None
            else:
                cap.release()
                cap = None
            if attempt == 0:
                print(f"[Camera] Waiting for camera {self.camera_index}...")
            time.sleep(0.5)

        if cap is None:
            print(f"[Camera] ERROR: cannot open camera {self.camera_index}")
            self._running = False
            return

        w, h = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)), int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        print(f"[Camera] Capture started (index {self.camera_index}, {w}x{h})")
        try:
            while self._running:
                ok, frame = cap.read()
                if ok and frame is not None:
                    with self._lock:
                        self._frame = frame
                else:
                    time.sleep(0.02)
        finally:
            cap.release()
            print("[Camera] Capture stopped")
