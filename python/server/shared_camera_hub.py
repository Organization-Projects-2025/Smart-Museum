"""
Single-process shared webcam: one capture thread, refcounted consumers.

Used by museum_vision_server.py so gesture (5001), gaze (5002), and YOLO context (5003)
do not open VideoCapture(0) independently (Windows cannot reliably share one USB camera).
"""

from __future__ import annotations

import os
import sys
import threading
import time
from typing import Dict, Optional

import cv2
import numpy as np


def _open_video_capture(index: int):
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


class SharedCameraHub:
    def __init__(self, camera_index: int = 0, mirror: bool = False):
        self.camera_index = int(camera_index)
        self.mirror = bool(mirror)
        self._lock = threading.Lock()
        self._refs: Dict[str, bool] = {}
        self._cap: Optional[cv2.VideoCapture] = None
        self._thread: Optional[threading.Thread] = None
        self._stop_capture = threading.Event()
        self._latest_bgr: Optional[np.ndarray] = None
        self._latest_mono = 0.0

    def acquire(self, consumer_id: str) -> None:
        """Register a consumer; starts capture thread on first acquire."""
        with self._lock:
            self._refs[str(consumer_id)] = True
            start = self._thread is None or not self._thread.is_alive()
        if start:
            self._start_capture_thread()

    def release(self, consumer_id: str) -> None:
        """Unregister; stops capture thread when last consumer is gone."""
        with self._lock:
            self._refs.pop(str(consumer_id), None)
            empty = len(self._refs) == 0
        if empty:
            self._stop_capture_thread()

    def get_latest_bgr_copy(self) -> Optional[np.ndarray]:
        """Thread-safe copy of the last frame (BGR), or None before first frame."""
        with self._lock:
            if self._latest_bgr is None:
                return None
            return self._latest_bgr.copy()

    def _capture_loop(self):
        cap = _open_video_capture(self.camera_index)
        if not cap.isOpened():
            print(
                f"shared_camera_hub: failed to open camera index {self.camera_index} "
                f"(set MUSEUM_CAMERA or GAZE_EMOTION_CAMERA)"
            )
            with self._lock:
                self._refs.clear()
            return
        self._cap = cap
        print(f"shared_camera_hub: capture started (camera {self.camera_index})")
        try:
            while not self._stop_capture.is_set():
                with self._lock:
                    if not self._refs:
                        break
                ok, frame = cap.read()
                if not ok:
                    time.sleep(0.02)
                    continue
                if self.mirror:
                    frame = cv2.flip(frame, 1)
                with self._lock:
                    self._latest_bgr = frame
                    self._latest_mono = time.monotonic()
        finally:
            try:
                cap.release()
            except Exception:
                pass
            self._cap = None
            with self._lock:
                self._latest_bgr = None
            print("shared_camera_hub: capture stopped")

    def _start_capture_thread(self):
        self._stop_capture.clear()
        t = threading.Thread(target=self._capture_loop, name="SharedCameraHub", daemon=True)
        self._thread = t
        t.start()

    def _stop_capture_thread(self):
        self._stop_capture.set()
        th = self._thread
        if th is not None:
            th.join(timeout=3.0)
        self._thread = None

    def shutdown(self) -> None:
        with self._lock:
            self._refs.clear()
        self._stop_capture_thread()


def default_camera_index() -> int:
    for key in ("MUSEUM_CAMERA", "GAZE_EMOTION_CAMERA", "GESTURE_CAMERA"):
        v = os.environ.get(key)
        if v is not None and str(v).strip() != "":
            try:
                return int(v)
            except ValueError:
                pass
    return 0


def default_mirror() -> bool:
    return os.environ.get("GAZE_EMOTION_MIRROR", "").strip().lower() in ("1", "true", "yes")
