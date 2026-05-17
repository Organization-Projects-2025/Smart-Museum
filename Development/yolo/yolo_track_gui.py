"""
Development/yolo/yolo_track_gui.py
==================================
Real-time YOLO11 object detection + tracking GUI.
Uses a dedicated camera thread to maximize frame throughput; inference runs in parallel.

Usage:
    python Development/yolo/yolo_track_gui.py
    python Development/yolo/yolo_track_gui.py --camera 0 --model yolo11s.pt --fps 30
"""

import argparse
import os
import sys
import threading
import time
import tkinter as tk
from tkinter import ttk

import cv2
import numpy as np
from PIL import Image, ImageTk

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
if SCRIPT_DIR not in sys.path:
    sys.path.insert(0, SCRIPT_DIR)

from camera_util import CameraReader

BG      = "#0f0f1a"
PANEL   = "#1a1a2e"
ACCENT  = "#e94560"
GREEN   = "#00e676"
TEXT    = "#e0e0e0"
SUBTEXT = "#888899"

CAP_W, CAP_H = 640, 480

_BOX_COLORS = [
    (0, 255, 128), (255, 128, 0), (128, 0, 255), (0, 200, 255),
    (255, 0, 128), (200, 255, 0), (255, 255, 0), (0, 128, 255),
]


def _color_for_class(cls_id: int):
    return _BOX_COLORS[cls_id % len(_BOX_COLORS)]


def _draw_detections(frame, result, names):
    if result is None or result.boxes is None or len(result.boxes) == 0:
        return frame

    out = frame.copy()
    boxes = result.boxes
    xyxy = boxes.xyxy.cpu().numpy()
    clss = boxes.cls.cpu().numpy().astype(int)
    confs = boxes.conf.cpu().numpy()
    ids = boxes.id.cpu().numpy().astype(int) if boxes.id is not None else None

    for i, (x1, y1, x2, y2) in enumerate(xyxy):
        cls_id = int(clss[i])
        label = names.get(cls_id, str(cls_id))
        if ids is not None:
            label = f"{label} #{int(ids[i])}"
        label = f"{label} {confs[i]:.0%}"

        color = _color_for_class(cls_id)
        cv2.rectangle(out, (int(x1), int(y1)), (int(x2), int(y2)), color, 2)
        (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.55, 2)
        ty = max(int(y1) - 8, th + 4)
        cv2.rectangle(out, (int(x1), ty - th - 6), (int(x1) + tw + 8, ty + 4), color, -1)
        cv2.putText(out, label, (int(x1) + 4, ty),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (0, 0, 0), 2, cv2.LINE_AA)
    return out


class YoloTrackApp:
    def __init__(self, camera: int, model_name: str, conf: float, imgsz: int, cap_fps: int):
        self.camera = camera
        self.model_name = model_name
        self.conf = conf
        self.imgsz = imgsz
        self.cap_fps = cap_fps

        self.model = None
        self.names = {}
        self._device = "cpu"
        self._half = False
        self._model_ready = threading.Event()
        self._model_error = None
        self._running = True
        self._cam: CameraReader | None = None

        self._result_lock = threading.Lock()
        self._last_result = None
        self._n_objects = 0

        self._disp_fps = 0.0
        self._infer_fps = 0.0
        self._photo = None

        self.root = tk.Tk()
        self.root.title("YOLO11 — Real-Time Tracking")
        self.root.configure(bg=BG)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

        header = tk.Frame(self.root, bg=PANEL, padx=12, pady=8)
        header.pack(fill=tk.X)
        tk.Label(header, text="YOLO11 Tracker", bg=PANEL, fg=TEXT,
                 font=("Segoe UI", 14, "bold")).pack(side=tk.LEFT)
        self.status_lbl = tk.Label(header, text="Opening camera…", bg=PANEL, fg=SUBTEXT,
                                   font=("Segoe UI", 10))
        self.status_lbl.pack(side=tk.RIGHT)

        self.video_lbl = tk.Label(self.root, bg=BG)
        self.video_lbl.pack(padx=8, pady=8)

        footer = tk.Frame(self.root, bg=PANEL, padx=12, pady=6)
        footer.pack(fill=tk.X)
        self.info_lbl = tk.Label(footer, text="", bg=PANEL, fg=SUBTEXT, font=("Consolas", 10))
        self.info_lbl.pack(side=tk.LEFT)
        ttk.Button(footer, text="Quit", command=self._on_close).pack(side=tk.RIGHT)

        self._open_camera()
        threading.Thread(target=self._load_model, daemon=True).start()
        threading.Thread(target=self._infer_loop, daemon=True).start()
        self._tick()

    def _open_camera(self):
        self._cam = CameraReader(self.camera, CAP_W, CAP_H, self.cap_fps)
        if not self._cam.open():
            self.status_lbl.config(text=f"Cannot open camera {self.camera}", fg=ACCENT)
            return
        self._cam.start()
        self.status_lbl.config(text="Camera live — loading model…", fg=SUBTEXT)

    def _load_model(self):
        try:
            import torch
            from ultralytics import YOLO

            self._device = 0 if torch.cuda.is_available() else "cpu"
            self._half = torch.cuda.is_available()
            self.model = YOLO(self.model_name)
            self.names = self.model.names or {}

            dummy = np.zeros((CAP_H, CAP_W, 3), dtype=np.uint8)
            self.model.track(
                dummy, persist=True, verbose=False, conf=self.conf, imgsz=self.imgsz,
                device=self._device, half=self._half,
            )
            self._model_ready.set()
        except Exception as e:
            self._model_error = str(e)
            self._model_ready.set()

    def _infer_loop(self):
        infer_fps = 0.0
        while self._running:
            if self._model_error or not self._model_ready.is_set():
                time.sleep(0.02)
                continue

            frame = None if self._cam is None else self._cam.get_latest()
            if frame is None:
                time.sleep(0.002)
                continue

            t0 = time.perf_counter()
            try:
                results = self.model.track(
                    frame,
                    persist=True,
                    verbose=False,
                    conf=self.conf,
                    imgsz=self.imgsz,
                    device=self._device,
                    half=self._half,
                )
                result = results[0]
            except Exception as e:
                self._model_error = str(e)
                time.sleep(0.05)
                continue

            now = time.perf_counter()
            infer_fps = 0.85 * infer_fps + 0.15 * (1.0 / (now - t0) if now > t0 else 0)
            self._infer_fps = infer_fps

            with self._result_lock:
                self._last_result = result
                self._n_objects = len(result.boxes) if result.boxes is not None else 0

    def _on_close(self):
        self._running = False
        if self._cam is not None:
            self._cam.stop()
        self.root.destroy()

    def _tick(self):
        if not self._running:
            return

        t0 = time.perf_counter()
        frame = None if self._cam is None else self._cam.get_latest()

        if frame is not None:
            h, w = frame.shape[:2]
            n_obj = 0

            if self._model_error:
                cv2.putText(frame, f"Model error: {self._model_error}", (12, 32),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
                self.status_lbl.config(text="Model failed", fg=ACCENT)
            elif not self._model_ready.is_set():
                cv2.putText(frame, "Loading YOLO11…", (12, 32),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 220, 255), 2)
                self.status_lbl.config(text="Camera live — loading model…", fg=SUBTEXT)
            else:
                with self._result_lock:
                    result = self._last_result
                    n_obj = self._n_objects
                frame = _draw_detections(frame, result, self.names)
                self.status_lbl.config(text="Tracking", fg=GREEN)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            self._photo = ImageTk.PhotoImage(image=Image.fromarray(rgb))
            self.video_lbl.config(image=self._photo)

            dt = time.perf_counter() - t0
            self._disp_fps = 0.9 * self._disp_fps + 0.1 * (1.0 / dt if dt > 0 else 0)
            cap_fps = self._cam.capture_fps if self._cam else 0.0
            drv = self._cam.driver_fps if self._cam else 0.0
            dev = "GPU" if self._half else "CPU"
            self.info_lbl.config(
                text=(
                    f"{w}x{h}  |  cam {cap_fps:.0f}  display {self._disp_fps:.0f}  "
                    f"infer {self._infer_fps:.0f} fps  |  drv {drv:.0f}  |  {dev}  |  "
                    f"{n_obj if self._model_ready.is_set() else 0} obj  |  {self.model_name}"
                )
            )

        self.root.after(1, self._tick)

    def run(self):
        self.root.mainloop()


def main():
    parser = argparse.ArgumentParser(description="YOLO11 real-time tracking GUI")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default: 0)")
    parser.add_argument("--model", default="yolo11s.pt", help="YOLO11 weights (default: yolo11s.pt)")
    parser.add_argument("--conf", type=float, default=0.35, help="Confidence threshold")
    parser.add_argument("--imgsz", type=int, default=640, help="Inference size")
    parser.add_argument("--fps", type=int, default=30, help="Requested camera FPS (default: 30)")
    args = parser.parse_args()

    app = YoloTrackApp(args.camera, args.model, args.conf, args.imgsz, args.fps)
    if app._cam is None or not app._cam.is_open():
        print(f"ERROR: Cannot open camera {args.camera}", file=sys.stderr)
        sys.exit(1)
    app.run()


if __name__ == "__main__":
    main()
