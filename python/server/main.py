#!/usr/bin/env python3
"""
server.py — Smart Museum unified server entry point.

1. Finds the best real webcam (auto-detect or MUSEUM_CAMERA env override)
2. Starts a single CameraHub that distributes frames to all services
3. Starts all TCP services in isolated daemon threads

Services:
  5000  auth_server        — Face ID + Bluetooth
  5001  gesture            — Gesture recognition (dollarpy-service)
  5002  gaze_emotion_server — Gaze tracking + emotion detection
  5003  yolo_server        — YOLOv8 object context
  5004  hand_server        — Hand pose tracking

Usage:
    python python/server/server.py

Env overrides:
    MUSEUM_CAMERA=<n>       Force camera index (skip auto-detect)
    DISABLE_GESTURE=1       Skip gesture service
    DISABLE_YOLO=1          Skip YOLO service
    DISABLE_HAND=1          Skip hand tracking service
    YOLO_CONTEXT_MOCK=1     Use fake YOLO tracks (no GPU needed)
"""

import os
import sys
import threading
import time

# Suppress noisy driver/framework stderr output before any imports trigger them
os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")       # TensorFlow
os.environ.setdefault("TF_ENABLE_ONEDNN_OPTS", "0")       # oneDNN ops warning
os.environ.setdefault("GLOG_minloglevel", "3")             # glog (mediapipe)
os.environ.setdefault("MEDIAPIPE_DISABLE_GPU", "1")        # mediapipe GPU warnings

# Redirect stderr to suppress VCAMDS / NVIDIA virtual camera driver spam
# (these come from the OS camera driver, not our code)
import io
class _FilteredStderr(io.TextIOWrapper):
    _SKIP = (b"VCAMDS", b"NvMxn", b"NBX hive", b"oneDNN", b"absl::",
             b"inference_feedback", b"landmark_projection", b"XNNPACK")
    def __init__(self):
        super().__init__(sys.stderr.buffer, line_buffering=True)
    def write(self, s):
        if any(k in s.encode("utf-8", errors="ignore") for k in self._SKIP):
            return len(s)
        return super().write(s)

sys.stderr = _FilteredStderr()
from datetime import datetime

# ── Path setup ────────────────────────────────────────────────────────────────
THIS_DIR     = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(os.path.dirname(THIS_DIR))
DOLLARPY_DIR = os.path.join(PROJECT_ROOT, "dollarpy-service")

for p in (THIS_DIR, DOLLARPY_DIR):
    if p not in sys.path:
        sys.path.insert(0, p)

os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")

# ── Imports ───────────────────────────────────────────────────────────────────
import camera_hub as _cam_mod
import auth_service
import demographics_service
import gaze_emotion_service
import yolo_service
import hand_service


# ── Logging ───────────────────────────────────────────────────────────────────
def _log(service: str, msg: str):
    ts = datetime.now().strftime("%H:%M:%S.%f")[:-3]
    print(f"[{ts}] {service:14s} {msg}")


# ── Service runner ────────────────────────────────────────────────────────────
def _run(name: str, fn, *args):
    """Start fn(*args) in a daemon thread with basic fault isolation."""
    def _body():
        try:
            fn(*args)
        except Exception as e:
            _log(name, f"ERROR: {e}")
    t = threading.Thread(target=_body, name=name, daemon=True)
    t.start()
    return t


# ── Main ──────────────────────────────────────────────────────────────────────
def main():
    _log("SERVER", "Starting Smart Museum Server...")

    # 1. Camera hub — index 0 by default, override with MUSEUM_CAMERA env var
    hub = _cam_mod.CameraHub()
    hub.start()
    time.sleep(0.5)  # let first frame arrive

    # 2. Wire hub into every service that needs frames
    auth_service.set_hub(hub)
    gaze_emotion_service.set_hub(hub)
    yolo_service.set_hub(hub)
    hand_service.set_hub(hub)

    # 3. Start services
    _log("SERVER", "Starting services...")

    _run("AUTH",       auth_service.start)
    _run("GAZE_EMO",   gaze_emotion_service.start)
    _run("YOLO",       yolo_service.start)
    _run("HAND",       hand_service.start)

    # 4. Pre-download DeepFace models in background (first-time only)
    _run("DEMOGRAPHICS", demographics_service.warmup)

    # Gesture service (dollarpy) — optional, failures are isolated
    if os.environ.get("DISABLE_GESTURE", "").strip() not in ("1","true","yes"):
        try:
            from gesture_service_refactored import GestureRecognitionService
            svc = GestureRecognitionService(host="127.0.0.1", port=5001, camera_hub=hub)
            _run("GESTURE", svc.start_server)
        except Exception:
            try:
                from gesture_service import GestureRecognitionService
                svc = GestureRecognitionService(host="127.0.0.1", port=5001, camera_hub=hub)
                _run("GESTURE", svc.start_server)
            except Exception as e:
                _log("GESTURE", f"Unavailable: {e}")

    _log("SERVER", "All services started.")
    _log("SERVER", "  Face Auth:    127.0.0.1:5000")
    _log("SERVER", "  Gesture:      127.0.0.1:5001")
    _log("SERVER", "  Gaze+Emotion: 127.0.0.1:5002")
    _log("SERVER", "  YOLO Context: 127.0.0.1:5003")
    _log("SERVER", "  Hand Track:   127.0.0.1:5004")
    _log("SERVER", "Press Ctrl+C to stop.")

    try:
        while True:
            time.sleep(1.0)
    except KeyboardInterrupt:
        _log("SERVER", "Shutting down...")
        hub.stop()


if __name__ == "__main__":
    main()
