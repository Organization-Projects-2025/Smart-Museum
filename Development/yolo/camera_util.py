"""
Webcam capture — uses cap.read() (reliable on Windows; grab/retrieve often returns no frames).
Background thread keeps the newest frame for display and YOLO.
"""

from __future__ import annotations

import sys
import threading
import time

import cv2


def open_camera(
    camera_id: int = 0,
    width: int = 0,
    height: int = 0,
    fps: int = 0,
) -> cv2.VideoCapture | None:
    """Open webcam. Tries default backend first (matches working object-track scripts)."""
    cap = cv2.VideoCapture(camera_id)
    if not cap.isOpened() and sys.platform == "win32" and hasattr(cv2, "CAP_DSHOW"):
        cap = cv2.VideoCapture(camera_id, cv2.CAP_DSHOW)
    if not cap.isOpened():
        return None

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    if width > 0:
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, float(width))
    if height > 0:
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, float(height))
    if fps > 0:
        cap.set(cv2.CAP_PROP_FPS, float(fps))

    # Warm-up read — some drivers need one frame before streaming
    for _ in range(5):
        ok, _ = cap.read()
        if ok:
            break
        time.sleep(0.05)

    return cap if cap.isOpened() else None


def read_actual_specs(cap: cv2.VideoCapture) -> tuple[int, int, float]:
    w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH) or 0)
    h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT) or 0)
    f = float(cap.get(cv2.CAP_PROP_FPS) or 0.0)
    return w, h, f


class CameraReader:
    def __init__(self, camera_id: int = 0, width: int = 640, height: int = 480, fps: int = 30):
        self.camera_id = camera_id
        self.width = width
        self.height = height
        self.target_fps = fps
        self.cap: cv2.VideoCapture | None = None
        self.actual_width = 0
        self.actual_height = 0
        self.driver_fps = 0.0

        self._lock = threading.Lock()
        self._frame = None
        self._running = False
        self._thread: threading.Thread | None = None
        self.capture_fps = 0.0
        self.last_read_ok = False

    def open(self) -> bool:
        w = self.width if self.width > 0 else 0
        h = self.height if self.height > 0 else 0
        self.cap = open_camera(self.camera_id, w, h, self.target_fps)
        if self.cap is None:
            return False
        self.actual_width, self.actual_height, self.driver_fps = read_actual_specs(self.cap)
        return True

    def start(self):
        if self.cap is None or not self.cap.isOpened():
            return
        self._running = True
        self._thread = threading.Thread(target=self._loop, daemon=True)
        self._thread.start()

    def _loop(self):
        count = 0
        last_t = time.perf_counter()

        while self._running and self.cap is not None and self.cap.isOpened():
            frame = None
            for _ in range(3):
                ok, f = self.cap.read()
                self.last_read_ok = ok
                if ok and f is not None and f.size > 0:
                    frame = f

            if frame is None:
                time.sleep(0.01)
                continue

            with self._lock:
                self._frame = frame
            count += 1

            now = time.perf_counter()
            if now - last_t >= 0.5:
                self.capture_fps = count / (now - last_t)
                count = 0
                last_t = now

    def get_latest(self):
        with self._lock:
            return None if self._frame is None else self._frame.copy()

    def is_open(self) -> bool:
        return self.cap is not None and self.cap.isOpened()

    def stop(self):
        self._running = False
        if self._thread is not None:
            self._thread.join(timeout=1.0)
        if self.cap is not None:
            self.cap.release()
            self.cap = None
