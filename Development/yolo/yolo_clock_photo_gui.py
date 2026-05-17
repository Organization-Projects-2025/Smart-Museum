"""
Development/yolo/yolo_clock_photo_gui.py
========================================
YOLO11 clock/watch tracker + pixel swipes + optional TCP bridge (port 5005).
Camera uses cap.read() in a background thread (reliable on Windows).

Usage:
    python Development/yolo/yolo_clock_photo_gui.py
    python Development/yolo/yolo_clock_photo_gui.py --camera 0 --no-tcp
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
from object_swipe_server import (
    set_object_gesture,
    set_object_visible,
    start_object_swipe_server,
)
from pixel_swipe import C_SHARP_GESTURES, SWIPE_LABELS, PixelSwipeTracker

BG      = "#0f0f1a"
PANEL   = "#1a1a2e"
ACCENT  = "#e94560"
GREEN   = "#00e676"
ORANGE  = "#ffb300"
TEXT    = "#e0e0e0"
SUBTEXT = "#888899"

CLOCK_NAME = "clock"
TRACK_CONF = 0.15
SWIPE_MIN_CONF = 0.1
BOX_COLOR = (0, 255, 128)
BOX_COLOR_HELD = (0, 200, 100)
ACCENT_BGR = (96, 69, 233)

HOLD_SEC = 0.45
SMOOTH_ALPHA = 0.65


def _clock_class_id(names: dict) -> int:
    for k, v in names.items():
        if str(v).lower() == CLOCK_NAME:
            return int(k)
    return 74


def _best_clock(result, clock_id: int):
    if result is None or result.boxes is None or len(result.boxes) == 0:
        return None
    boxes = result.boxes
    xyxy = boxes.xyxy.cpu().numpy()
    clss = boxes.cls.cpu().numpy().astype(int)
    confs = boxes.conf.cpu().numpy()
    best_conf = -1.0
    best_box = None
    for i, conf in enumerate(confs):
        if int(clss[i]) != clock_id:
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


def _draw_clock(frame, box, conf, held: bool = False):
    x1, y1, x2, y2 = box
    color = BOX_COLOR_HELD if held else BOX_COLOR
    tag = "clock" if not held else "clock (held)"
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


class ClockCameraApp:
    def __init__(
        self,
        camera: int,
        model_name: str,
        imgsz: int,
        cap_fps: int,
        enable_tcp: bool,
        tcp_port: int,
    ):
        self.camera = camera
        self.model_name = model_name
        self.imgsz = imgsz
        self.cap_fps = cap_fps
        self.enable_tcp = enable_tcp

        self.model = None
        self._clock_id = 74
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
        self._swipe = PixelSwipeTracker(min_conf=SWIPE_MIN_CONF)
        self._swipe_history: list[str] = []

        if enable_tcp:
            start_object_swipe_server(port=tcp_port)

        self.root = tk.Tk()
        self.root.title("YOLO11 — Clock + swipes")
        self.root.configure(bg=BG)
        self.root.minsize(680, 520)
        self.root.protocol("WM_DELETE_WINDOW", self._on_close)

        header = tk.Frame(self.root, bg=PANEL, padx=12, pady=8)
        header.pack(fill=tk.X)
        tk.Label(header, text="Clock detector", bg=PANEL, fg=TEXT,
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
            text="ByteTrack + pixel swipes · TCP 5005" if enable_tcp else "ByteTrack + pixel swipes",
            bg=PANEL, fg=SUBTEXT, font=("Segoe UI", 9),
        )
        self.swipe_hint_lbl.pack(side=tk.RIGHT)

        self.video_lbl = tk.Label(self.root, bg=BG, text="Starting camera…", fg=SUBTEXT,
                                  font=("Segoe UI", 12))
        self.video_lbl.pack(expand=True, fill=tk.BOTH, padx=8, pady=8)

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
        self._cam = CameraReader(self.camera, 640, 480, self.cap_fps)
        if not self._cam.open():
            self.status_lbl.config(text=f"Cannot open camera {self.camera}", fg=ACCENT)
            self.video_lbl.config(text=f"Cannot open camera {self.camera}", fg=ACCENT)
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
            self._clock_id = _clock_class_id(self.model.names or {})

            dummy = np.zeros((480, 640, 3), dtype=np.uint8)
            self.model.track(
                dummy,
                persist=True,
                verbose=False,
                conf=TRACK_CONF,
                classes=[self._clock_id],
                tracker="bytetrack.yaml",
                imgsz=self.imgsz,
                device=self._device,
                half=self._half,
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
                set_object_visible(False)
                time.sleep(0.01)
                continue

            t0 = time.perf_counter()
            try:
                results = self.model.track(
                    frame,
                    persist=True,
                    verbose=False,
                    conf=TRACK_CONF,
                    classes=[self._clock_id],
                    tracker="bytetrack.yaml",
                    imgsz=self.imgsz,
                    device=self._device,
                    half=self._half,
                )
                hit = _best_clock(results[0], self._clock_id)
            except Exception as e:
                self._model_error = str(e)
                time.sleep(0.05)
                continue

            now = time.perf_counter()
            infer_fps = 0.85 * infer_fps + 0.15 * (1.0 / (now - t0) if now > t0 else 0)
            self._infer_fps = infer_fps

            visible = hit is not None
            set_object_visible(visible)

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
        set_object_visible(False)
        if self._cam is not None:
            self._cam.stop()
        self.root.destroy()

    def _tick(self):
        if not self._running:
            return

        t0 = time.perf_counter()
        frame = None if self._cam is None else self._cam.get_latest()

        if frame is None:
            self.status_lbl.config(
                text="Waiting for camera frames…" if self._cam and self._cam.is_open() else "No camera",
                fg=ORANGE,
            )
            self.root.after(30, self._tick)
            return

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
            swipe_conf = conf if conf is not None else 0.0

            if track_box is not None:
                held = not fresh and age < HOLD_SEC
                _draw_clock(frame, track_box, conf, held=held)

            new_swipe = self._swipe.update(track_box, swipe_conf)
            if new_swipe:
                self._swipe_history.append(new_swipe)
                self._swipe_history = self._swipe_history[-8:]
                self.swipe_lbl.config(text=SWIPE_LABELS[new_swipe], fg=GREEN)
                if self.enable_tcp:
                    set_object_gesture(C_SHARP_GESTURES.get(new_swipe))
            elif not self._swipe.swipe_visible():
                self.swipe_lbl.config(text="—", fg=SUBTEXT)

            if self._swipe.swipe_visible() and self._swipe.last_swipe:
                _draw_swipe_overlay(frame, self._swipe.last_swipe)

            self.swipe_hint_lbl.config(
                text=f"thresh ~{int(self._swipe.last_threshold_px)}px  |  last: "
                + (", ".join(self._swipe_history[-4:]) if self._swipe_history else "none")
            )

            if track_box is not None:
                self.status_lbl.config(
                    text=f"Clock {conf:.1%}" + (" · held" if held else ""),
                    fg=GREEN,
                )
            else:
                self.status_lbl.config(text=f"No clock (track conf ≥ {TRACK_CONF:.0%})", fg=ORANGE)

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        self._photo = ImageTk.PhotoImage(image=Image.fromarray(rgb))
        self.video_lbl.config(image=self._photo, text="")

        dt = time.perf_counter() - t0
        self._disp_fps = 0.9 * self._disp_fps + 0.1 * (1.0 / dt if dt > 0 else 0)
        conf_txt = f"{conf:.3f}" if conf is not None else "—"
        dev = "GPU" if self._half else "CPU"
        cap_fps = self._cam.capture_fps if self._cam else 0.0
        self.info_lbl.config(
            text=(
                f"{w}x{h}  |  cam {cap_fps:.0f}  display {self._disp_fps:.0f}  "
                f"infer {self._infer_fps:.0f}  |  {dev}  |  best={conf_txt}  |  {self.model_name}"
            )
        )

        self.root.after(1, self._tick)

    def run(self):
        self.root.mainloop()


def main():
    parser = argparse.ArgumentParser(description="YOLO11 clock tracker + object swipes")
    parser.add_argument("--camera", type=int, default=0)
    parser.add_argument("--model", default="yolo11s.pt")
    parser.add_argument("--imgsz", type=int, default=416)
    parser.add_argument("--fps", type=int, default=30)
    parser.add_argument("--tcp-port", type=int, default=5005)
    parser.add_argument("--no-tcp", action="store_true", help="Disable C# TCP bridge")
    args = parser.parse_args()

    app = ClockCameraApp(
        args.camera, args.model, args.imgsz, args.fps,
        enable_tcp=not args.no_tcp, tcp_port=args.tcp_port,
    )
    if app._cam is None or not app._cam.is_open():
        print(f"ERROR: Cannot open camera {args.camera}", file=sys.stderr)
        sys.exit(1)
    app.run()


if __name__ == "__main__":
    main()
