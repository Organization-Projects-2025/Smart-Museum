"""
Development/yolo/yolo_spoon_gui.py
==================================
Low-latency YOLO11 spoon detector (webcam) with swipe gestures.
Same pipeline as yolo_clock_photo_gui.py but filters COCO class spoon only (id 44).

Usage:
    python Development/yolo/yolo_spoon_gui.py
    python Development/yolo/yolo_spoon_gui.py --camera 0 --imgsz 416
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
from watch_swipe import GAP_MAX_SEC, SWIPE_LABELS, WatchSwipeDetector

# ── Theme ───────────────────────────────────────────────────────────────────────
BG      = "#0f0f1a"
PANEL   = "#1a1a2e"
ACCENT  = "#e94560"
GREEN   = "#00e676"
ORANGE  = "#ffb300"
TEXT    = "#e0e0e0"
SUBTEXT = "#888899"

OBJECT_NAME = "spoon"
COCO_FALLBACK_ID = 44
MIN_CONF = 0.2
BOX_COLOR = (0, 200, 255)
BOX_COLOR_HELD = (0, 160, 200)
ACCENT_BGR = (96, 69, 233)

HOLD_SEC = 0.45
SMOOTH_ALPHA = 0.65
CAP_W, CAP_H = 640, 480


def _object_class_id(names: dict) -> int:
    for k, v in names.items():
        if str(v).lower() == OBJECT_NAME:
            return int(k)
    return COCO_FALLBACK_ID


def _best_detection(result, class_id: int, min_conf: float):
    if result is None or result.boxes is None or len(result.boxes) == 0:
        return None

    boxes = result.boxes
    xyxy = boxes.xyxy.cpu().numpy()
    clss = boxes.cls.cpu().numpy().astype(int)
    confs = boxes.conf.cpu().numpy()

    best_conf = min_conf
    best_box = None

    for i, conf in enumerate(confs):
        if int(clss[i]) != class_id or conf <= min_conf:
            continue
        if conf > best_conf:
            best_conf = float(conf)
            best_box = tuple(int(v) for v in xyxy[i])

    if best_box is None:
        return None
    return best_box, best_conf


def _ema_box(prev, new, alpha: float):
    if prev is None:
        return new
    return tuple(int(alpha * n + (1.0 - alpha) * p) for p, n in zip(prev, new))


def _draw_object(frame, box, conf, held: bool = False):
    x1, y1, x2, y2 = box
    color = BOX_COLOR_HELD if held else BOX_COLOR
    tag = OBJECT_NAME if not held else f"{OBJECT_NAME} (held)"
    label = f"{tag} {conf:.0%}"
    cv2.rectangle(frame, (x1, y1), (x2, y2), color, 3)
    (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.7, 2)
    ty = max(y1 - 10, th + 8)
    cv2.rectangle(frame, (x1, ty - th - 8), (x1 + tw + 12, ty + 6), color, -1)
    cv2.putText(frame, label, (x1 + 6, ty), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 0), 2, cv2.LINE_AA)


def _draw_swipe_overlay(frame, swipe: str):
    label = SWIPE_LABELS.get(swipe, swipe.upper())
    h, w = frame.shape[:2]
    scale = max(w, h) / 640.0
    font = cv2.FONT_HERSHEY_DUPLEX
    fs = 1.1 * scale
    th = max(2, int(3 * scale))
    (tw, th_txt), _ = cv2.getTextSize(label, font, fs, th)
    cx, cy = w // 2, h // 2
    x1, y1 = cx - tw // 2 - 20, cy - th_txt // 2 - 16
    x2, y2 = cx + tw // 2 + 20, cy + th_txt // 2 + 16
    overlay = frame.copy()
    cv2.rectangle(overlay, (x1, y1), (x2, y2), (20, 20, 40), -1)
    cv2.addWeighted(overlay, 0.72, frame, 0.28, 0, frame)
    cv2.rectangle(frame, (x1, y1), (x2, y2), ACCENT_BGR, 3)
    cv2.putText(frame, label, (cx - tw // 2, cy + th_txt // 2),
                font, fs, (80, 80, 255), th, cv2.LINE_AA)


class SpoonCameraApp:
    def __init__(self, camera: int, model_name: str, imgsz: int, cap_fps: int):
        self.camera = camera
        self.model_name = model_name
        self.imgsz = imgsz
        self.cap_fps = cap_fps
        self.min_conf = MIN_CONF

        self.model = None
        self._class_id = COCO_FALLBACK_ID
        self._device = "cpu"
        self._half = False
        self._model_ready = threading.Event()
        self._model_error = None
        self._running = True
        self._cam: CameraReader | None = None

        self._det_lock = threading.Lock()
        self._smooth_box = None
        self._smooth_conf = None
        self._det_seen_at = 0.0
        self._det_fresh = False

        self._disp_fps = 0.0
        self._infer_fps = 0.0
        self._photo = None
        self._swipe = WatchSwipeDetector()
        self._swipe_history: list[str] = []

        self.root = tk.Tk()
        self.root.title("YOLO11 — Spoon + swipes")
        self.root.configure(bg=BG)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

        header = tk.Frame(self.root, bg=PANEL, padx=12, pady=8)
        header.pack(fill=tk.X)
        tk.Label(header, text="Spoon detector", bg=PANEL, fg=TEXT,
                 font=("Segoe UI", 14, "bold")).pack(side=tk.LEFT)
        self.status_lbl = tk.Label(header, text="Opening camera…", bg=PANEL, fg=SUBTEXT,
                                   font=("Segoe UI", 10))
        self.status_lbl.pack(side=tk.RIGHT)

        swipe_row = tk.Frame(self.root, bg=PANEL, padx=12, pady=10)
        swipe_row.pack(fill=tk.X)
        tk.Label(swipe_row, text="Swipe", bg=PANEL, fg=SUBTEXT,
                 font=("Segoe UI", 10)).pack(side=tk.LEFT, padx=(0, 12))
        self.swipe_lbl = tk.Label(swipe_row, text="—", bg=PANEL, fg=SUBTEXT,
                                  font=("Segoe UI", 22, "bold"))
        self.swipe_lbl.pack(side=tk.LEFT)
        self.swipe_hint_lbl = tk.Label(
            swipe_row,
            text="Move the spoon: bigger in frame = shorter swipe needed",
            bg=PANEL, fg=SUBTEXT, font=("Segoe UI", 9),
        )
        self.swipe_hint_lbl.pack(side=tk.RIGHT)

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
            self._class_id = _object_class_id(self.model.names or {})

            dummy = np.zeros((CAP_H, CAP_W, 3), dtype=np.uint8)
            self.model.predict(
                dummy,
                verbose=False,
                conf=self.min_conf,
                imgsz=self.imgsz,
                classes=[self._class_id],
                device=self._device,
                half=self._half,
                max_det=3,
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
                results = self.model.predict(
                    frame,
                    verbose=False,
                    conf=self.min_conf,
                    imgsz=self.imgsz,
                    classes=[self._class_id],
                    device=self._device,
                    half=self._half,
                    max_det=3,
                )
                hit = _best_detection(results[0], self._class_id, self.min_conf)
            except Exception as e:
                self._model_error = str(e)
                time.sleep(0.05)
                continue

            now = time.perf_counter()
            infer_fps = 0.85 * infer_fps + 0.15 * (1.0 / (now - t0) if now > t0 else 0)
            self._infer_fps = infer_fps

            with self._det_lock:
                if hit is not None:
                    box, conf = hit
                    self._smooth_box = _ema_box(self._smooth_box, box, SMOOTH_ALPHA)
                    self._smooth_conf = conf
                    self._det_seen_at = time.perf_counter()
                    self._det_fresh = True
                else:
                    self._det_fresh = False
                    if time.perf_counter() - self._det_seen_at > HOLD_SEC:
                        self._smooth_box = None
                        self._smooth_conf = None

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
            held = False
            conf = None

            if self._model_error:
                cv2.putText(frame, f"Model error: {self._model_error}", (12, 32),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
                self.status_lbl.config(text="Model failed", fg=ACCENT)
            elif not self._model_ready.is_set():
                cv2.putText(frame, "Loading YOLO11…", (12, 32),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 220, 255), 2)
                self.status_lbl.config(text="Camera live — loading model…", fg=SUBTEXT)
            else:
                with self._det_lock:
                    box = self._smooth_box
                    conf = self._smooth_conf
                    fresh = self._det_fresh
                    age = time.perf_counter() - self._det_seen_at if box else 999.0

                track_box = box if (box is not None and conf is not None) else None
                if track_box is not None:
                    held = not fresh and age < HOLD_SEC
                    _draw_object(frame, track_box, conf, held=held)

                new_swipe = self._swipe.update(track_box, w, h)
                if new_swipe:
                    self._swipe_history.append(new_swipe)
                    self._swipe_history = self._swipe_history[-8:]
                    self.swipe_lbl.config(text=SWIPE_LABELS[new_swipe], fg=GREEN)
                elif not self._swipe.swipe_visible():
                    self.swipe_lbl.config(text="—", fg=SUBTEXT)

                if self._swipe.swipe_visible() and self._swipe.last_swipe:
                    _draw_swipe_overlay(frame, self._swipe.last_swipe)

                trig_pct = int(self._swipe.last_trigger * 100)
                self.swipe_hint_lbl.config(
                    text=f"swipe ~{trig_pct}% frame  |  gap≤{int(GAP_MAX_SEC * 1000)}ms  |  last: "
                    + (", ".join(self._swipe_history[-4:]) if self._swipe_history else "none")
                )

                if track_box is not None:
                    self.status_lbl.config(
                        text=f"Spoon {conf:.1%}" + (" · held" if held else ""),
                        fg=GREEN,
                    )
                else:
                    self.status_lbl.config(text="No spoon (conf > 0.2)", fg=ORANGE)

            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            self._photo = ImageTk.PhotoImage(image=Image.fromarray(rgb))
            self.video_lbl.config(image=self._photo)

            dt = time.perf_counter() - t0
            self._disp_fps = 0.9 * self._disp_fps + 0.1 * (1.0 / dt if dt > 0 else 0)
            conf_txt = f"{conf:.3f}" if conf is not None else "—"
            dev = "GPU" if self._half else "CPU"
            cap_fps = self._cam.capture_fps if self._cam else 0.0
            drv = self._cam.driver_fps if self._cam else 0.0
            self.info_lbl.config(
                text=(
                    f"{w}x{h}  |  cam {cap_fps:.0f}  display {self._disp_fps:.0f}  "
                    f"infer {self._infer_fps:.0f} fps  |  drv {drv:.0f}  |  {dev}  |  "
                    f"class={self._class_id}  |  best={conf_txt}  |  {self.model_name}"
                )
            )

        self.root.after(1, self._tick)

    def run(self):
        self.root.mainloop()


def main():
    parser = argparse.ArgumentParser(description="YOLO11 spoon-only camera + swipe detector")
    parser.add_argument("--camera", type=int, default=0, help="Camera index (default: 0)")
    parser.add_argument("--model", default="yolo11s.pt", help="YOLO11 weights")
    parser.add_argument("--imgsz", type=int, default=416, help="Inference size (default: 416)")
    parser.add_argument("--fps", type=int, default=30, help="Requested camera FPS (default: 30)")
    args = parser.parse_args()

    app = SpoonCameraApp(args.camera, args.model, args.imgsz, args.fps)
    if app._cam is None or not app._cam.is_open():
        print(f"ERROR: Cannot open camera {args.camera}", file=sys.stderr)
        sys.exit(1)
    app.run()


if __name__ == "__main__":
    main()
