"""
Smart Museum — Real-Time Gesture Recognition
Simple, single-purpose: show camera + detect gestures live.
"""

import os, sys, time, math, pickle, threading, cv2
import tkinter as tk
from tkinter import ttk, messagebox
from PIL import Image, ImageTk
from copy import deepcopy
from dollarpy import Recognizer, Template, Point

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, SCRIPT_DIR)

import mediapipe_compat as mp

# ── Tunable constants ──────────────────────────────────────────────────────────
TEMPLATES_FILE   = os.path.join(SCRIPT_DIR, "gesture_templates.pkl")
VIDEOS_DIR       = os.path.join(SCRIPT_DIR, "gesture_videos")
MAX_FRAMES       = 60      # sliding window size (frames)
MIN_POINTS       = 10      # min points to attempt recognition
MIN_MOTION       = 0.03    # min cumulative index-tip travel (0–1 scale)
SCORE_THRESHOLD  = 0.30    # min score to show a green "detected" label
FRAME_DELAY_MS   = 16      # ~60 FPS tkinter loop
INDEX_TIP        = 8       # MediaPipe landmark id


# ── Colours ────────────────────────────────────────────────────────────────────
BG       = "#0f0f1a"
PANEL    = "#1a1a2e"
ACCENT   = "#e94560"
GREEN    = "#00e676"
ORANGE   = "#ffb300"
GRAY     = "#555577"
TEXT     = "#e0e0e0"
SUBTEXT  = "#888899"


# ── Helpers ────────────────────────────────────────────────────────────────────

def _extract_points(frames_buffer):
    """
    Index-fingertip path in raw normalised coords → list[Point].
    Returns None when there is not enough data or motion.
    """
    pts = []
    for fd in frames_buffer:
        lm = fd.get("lm")
        if lm is None:
            continue
        tip = lm.landmark[INDEX_TIP]
        pts.append(Point(tip.x, tip.y, stroke_id=0))

    if len(pts) < MIN_POINTS:
        return None

    # Cumulative Euclidean motion
    motion = sum(
        math.hypot(pts[i].x - pts[i-1].x, pts[i].y - pts[i-1].y)
        for i in range(1, len(pts))
    )
    return pts if motion >= MIN_MOTION else None


def _recognize_points(templates, pts):
    """
    Run dollarpy recognition on `pts` using a fresh (non-mutating) recogniser.
    Returns (gesture_name: str, score: float) or (None, 0.0).
    """
    if not templates or pts is None:
        return None, 0.0
    try:
        rec    = Recognizer(deepcopy(templates))
        result = rec.recognize(pts)
        if result and len(result) == 2:
            name, score = result
            return name, float(score)
    except Exception:
        pass
    return None, 0.0


def _build_templates_from_videos(videos_dir, progress_cb=None):
    """
    Process every video in every sub-folder of `videos_dir`.
    Returns list[Template].  Skips the 'archive' folder.
    """
    mp_hands_module = mp.solutions.hands
    hands = mp_hands_module.Hands(
        static_image_mode=False,
        max_num_hands=1,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5,
    )

    all_templates = []
    video_exts    = ('.mp4', '.avi', '.mov', '.mkv', '.flv')

    folders = [
        (item, os.path.join(videos_dir, item))
        for item in os.listdir(videos_dir)
        if item != "archive" and os.path.isdir(os.path.join(videos_dir, item))
    ]

    for folder_name, folder_path in folders:
        gesture_name = folder_name.lower().replace(" ", "_").replace("-", "_")
        videos = [f for f in os.listdir(folder_path)
                  if f.lower().endswith(video_exts)]

        if not videos:
            continue

        for vfile in videos:
            vpath = os.path.join(folder_path, vfile)
            if progress_cb:
                progress_cb(f"Processing {gesture_name} — {vfile}")

            cap   = cv2.VideoCapture(vpath)
            fps   = cap.get(cv2.CAP_PROP_FPS) or 60.0
            step  = max(1, round(fps / 60.0))
            idx   = 0
            fbuf  = []

            while cap.isOpened():
                ret, frame = cap.read()
                if not ret:
                    break
                idx += 1
                if (idx - 1) % step != 0:
                    continue
                frame = cv2.resize(frame, (640, 480))
                rgb   = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                res   = hands.process(rgb)
                if res.multi_hand_landmarks:
                    fbuf.append({"lm": res.multi_hand_landmarks[0]})
            cap.release()

            if not fbuf:
                continue

            # ── Augment: generate multiple overlapping windows ─────────────
            windows = [fbuf]  # full sequence always included
            total   = len(fbuf)
            for frac in (0.60, 0.75, 0.85):
                win = max(15, int(total * frac))
                for s_frac in (0.0, 0.15, 0.30):
                    s = min(int(total * s_frac), total - win)
                    windows.append(fbuf[s : s + win])

            created = 0
            for win in windows:
                pts = _extract_points(win)
                if pts:
                    all_templates.append(Template(gesture_name, pts))
                    created += 1

            if progress_cb:
                progress_cb(f"  ✓ {gesture_name}: {created} templates from {vfile}")

    hands.__del__()
    return all_templates


# ── Main GUI ───────────────────────────────────────────────────────────────────

class GestureGUI:
    def __init__(self, root: tk.Tk):
        self.root      = root
        self.templates = []
        self.cap       = None
        self.running   = False

        # Sliding window
        self.buf = []         # list of {"lm": hand_landmarks}

        # Last recognition result
        self.last_name  = None
        self.last_score = 0.0
        self.last_time  = 0.0   # time of last confident detection
        self.cooldown   = 1.5   # seconds before accepting next gesture

        # MediaPipe
        mp_h = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.hands = mp_h.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5,
        )

        self._build_ui()
        self._load_templates(silent=True)
        self._start_camera()

    # ── UI construction ──────────────────────────────────────────────────────

    def _build_ui(self):
        self.root.title("Smart Museum — Gesture Recognition")
        self.root.geometry("1100x680")
        self.root.configure(bg=BG)
        self.root.resizable(False, False)

        # ── Top bar ──────────────────────────────────────────────────────────
        top = tk.Frame(self.root, bg=PANEL, height=56)
        top.pack(fill=tk.X)
        top.pack_propagate(False)

        tk.Label(top, text="🤚  Smart Museum — Gesture Recognition",
                 bg=PANEL, fg=TEXT, font=("Segoe UI", 13, "bold")
                 ).pack(side=tk.LEFT, padx=18, pady=14)

        # Buttons (right-aligned)
        btn_kw = dict(bg=ACCENT, fg="white", font=("Segoe UI", 9, "bold"),
                      relief=tk.FLAT, cursor="hand2", padx=14, pady=6,
                      activebackground="#c73652", activeforeground="white")

        self.rebuild_btn = tk.Button(top, text="⟳  Rebuild Templates",
                                     command=self._on_rebuild, **btn_kw)
        self.rebuild_btn.pack(side=tk.RIGHT, padx=10, pady=10)

        self.load_btn = tk.Button(top, text="📂  Load Templates",
                                  command=self._on_load, **btn_kw)
        self.load_btn.pack(side=tk.RIGHT, padx=4, pady=10)

        # ── Main area ─────────────────────────────────────────────────────────
        main = tk.Frame(self.root, bg=BG)
        main.pack(fill=tk.BOTH, expand=True, padx=14, pady=10)

        # ── Camera feed (left) ────────────────────────────────────────────────
        cam_frame = tk.Frame(main, bg=PANEL, bd=0)
        cam_frame.pack(side=tk.LEFT, fill=tk.BOTH)

        self.video_label = tk.Label(cam_frame, bg="black")
        self.video_label.pack(padx=2, pady=2)

        # ── Info panel (right) ────────────────────────────────────────────────
        right = tk.Frame(main, bg=BG, width=320)
        right.pack(side=tk.RIGHT, fill=tk.BOTH, expand=True, padx=(12, 0))
        right.pack_propagate(False)

        # Detected gesture — big card
        card = tk.Frame(right, bg=PANEL, bd=0)
        card.pack(fill=tk.X, pady=(0, 10))

        tk.Label(card, text="DETECTED GESTURE",
                 bg=PANEL, fg=SUBTEXT,
                 font=("Segoe UI", 8, "bold")).pack(pady=(14, 2))

        self.gesture_label = tk.Label(
            card, text="—", bg=PANEL, fg=GRAY,
            font=("Segoe UI", 32, "bold"), wraplength=290)
        self.gesture_label.pack(pady=(0, 6))

        self.score_bar_canvas = tk.Canvas(card, bg=PANEL, height=8,
                                           highlightthickness=0, width=260)
        self.score_bar_canvas.pack(pady=(0, 4))

        self.score_label = tk.Label(card, text="Score: 0.000",
                                    bg=PANEL, fg=SUBTEXT,
                                    font=("Segoe UI", 9))
        self.score_label.pack(pady=(0, 14))

        # Status card
        stat_card = tk.Frame(right, bg=PANEL)
        stat_card.pack(fill=tk.X, pady=(0, 10))

        tk.Label(stat_card, text="STATUS",
                 bg=PANEL, fg=SUBTEXT,
                 font=("Segoe UI", 8, "bold")).pack(pady=(10, 4))

        self.status_label = tk.Label(stat_card, text="Starting…",
                                     bg=PANEL, fg=ORANGE,
                                     font=("Segoe UI", 10))
        self.status_label.pack(pady=(0, 4))

        self.templates_label = tk.Label(stat_card, text="Templates: 0",
                                        bg=PANEL, fg=SUBTEXT,
                                        font=("Segoe UI", 9))
        self.templates_label.pack(pady=(0, 4))

        self.frames_label = tk.Label(stat_card, text="Buffer: 0/60 frames",
                                     bg=PANEL, fg=SUBTEXT,
                                     font=("Segoe UI", 9))
        self.frames_label.pack(pady=(0, 10))

        # Log panel
        log_card = tk.Frame(right, bg=PANEL)
        log_card.pack(fill=tk.BOTH, expand=True)

        tk.Label(log_card, text="RECOGNITION LOG",
                 bg=PANEL, fg=SUBTEXT,
                 font=("Segoe UI", 8, "bold")).pack(pady=(10, 4))

        self.log_text = tk.Text(log_card, bg="#111122", fg=TEXT,
                                font=("Consolas", 8), height=12,
                                relief=tk.FLAT, state=tk.DISABLED,
                                wrap=tk.WORD, insertbackground=TEXT)
        self.log_text.pack(fill=tk.BOTH, expand=True, padx=8, pady=(0, 8))

    # ── Camera ───────────────────────────────────────────────────────────────

    def _start_camera(self):
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            self._set_status("Camera not found", ACCENT)
            return
        self.running = True
        self._set_status("Camera running", GREEN)
        self._update_frame()

    def _update_frame(self):
        if not self.running:
            return

        ret, frame = self.cap.read()
        if not ret:
            self.root.after(FRAME_DELAY_MS, self._update_frame)
            return

        frame = cv2.resize(frame, (640, 480))
        rgb   = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        res   = self.hands.process(rgb)

        hand_detected = bool(res.multi_hand_landmarks)

        if hand_detected:
            lm = res.multi_hand_landmarks[0]
            # Draw skeleton
            self.mp_drawing.draw_landmarks(
                frame, lm, mp.solutions.hands.Hands.HAND_CONNECTIONS)

            # Push into sliding window
            self.buf.append({"lm": lm})
            if len(self.buf) > MAX_FRAMES:
                self.buf.pop(0)

            # Highlight index fingertip
            h, w, _ = frame.shape
            tip = lm.landmark[INDEX_TIP]
            cx, cy = int(tip.x * w), int(tip.y * h)
            cv2.circle(frame, (cx, cy), 10, (0, 200, 255), -1)
            cv2.circle(frame, (cx, cy), 12, (255, 255, 255), 2)
        else:
            # Hand lost — slowly drain buffer
            if self.buf:
                self.buf.pop(0)

        # OSD overlay
        self._draw_osd(frame, hand_detected)

        # Run recognition if we have enough data
        if self.templates and len(self.buf) >= MIN_POINTS:
            self._do_recognition()

        # Update sidebar stats
        self.frames_label.config(
            text=f"Buffer: {len(self.buf)}/{MAX_FRAMES} frames")

        # Show frame in GUI
        img    = Image.fromarray(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        imgtk  = ImageTk.PhotoImage(image=img)
        self.video_label.imgtk = imgtk
        self.video_label.config(image=imgtk)

        self.root.after(FRAME_DELAY_MS, self._update_frame)

    def _draw_osd(self, frame, hand_detected):
        """Draw on-screen debug info on the camera frame."""
        buf_len = len(self.buf)
        # Background strip
        cv2.rectangle(frame, (0, 0), (640, 95), (0, 0, 0), -1)
        cv2.rectangle(frame, (0, 0), (640, 95), (30, 30, 60), 1)

        # Hand status
        hand_color  = (0, 230, 118) if hand_detected else (200, 60, 60)
        hand_text   = "HAND: DETECTED" if hand_detected else "HAND: NOT FOUND"
        cv2.putText(frame, hand_text, (12, 26),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.65, hand_color, 2)

        # Buffer bar
        bar_w = int((buf_len / MAX_FRAMES) * 280)
        cv2.rectangle(frame, (12, 36), (292, 50), (40, 40, 60), -1)
        bar_color = (0, 200, 100) if buf_len >= MIN_POINTS else (200, 130, 0)
        if bar_w > 0:
            cv2.rectangle(frame, (12, 36), (12 + bar_w, 50), bar_color, -1)
        cv2.putText(frame, f"Buffer {buf_len}/{MAX_FRAMES}", (12, 70),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (180, 180, 200), 1)

        # Templates
        cv2.putText(frame, f"Templates: {len(self.templates)}", (310, 26),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (180, 180, 200), 1)

        # Last result on camera
        if self.last_name and (time.time() - self.last_time) < 2.0:
            label = self.last_name.replace("_", " ").upper()
            score_color = (0, 230, 118) if self.last_score >= SCORE_THRESHOLD else (255, 176, 0)
            cv2.putText(frame, label, (12, 460),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.2, score_color, 3)
            cv2.putText(frame, f"{self.last_score:.3f}", (12, 430),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, score_color, 2)

    # ── Recognition ──────────────────────────────────────────────────────────

    def _do_recognition(self):
        pts = _extract_points(self.buf)
        if pts is None:
            return

        name, score = _recognize_points(self.templates, pts)
        if name is None:
            return

        self.last_name  = name
        self.last_score = score

        now = time.time()
        in_cooldown = (now - self.last_time) < self.cooldown

        # Update score bar
        self._update_score_bar(score)
        self.score_label.config(text=f"Score: {score:.3f}")

        if score >= SCORE_THRESHOLD and not in_cooldown:
            display = name.replace("_", " ").title()
            self.gesture_label.config(text=display, fg=GREEN)
            self.last_time = now
            self.buf.clear()   # reset after confirmed detection
            self._log(f"✓ {display}  [{score:.3f}]", GREEN)
        elif score >= SCORE_THRESHOLD * 0.6:
            # Low-confidence hint
            display = name.replace("_", " ").title()
            self.gesture_label.config(text=f"≈ {display}", fg=ORANGE)
            self._log(f"≈ {display}  [{score:.3f}]", ORANGE)
        else:
            self.gesture_label.config(
                text=name.replace("_", " ").title(), fg=GRAY)

    def _update_score_bar(self, score):
        c = self.score_bar_canvas
        c.delete("all")
        w = 260
        filled = int(score * w)
        c.create_rectangle(0, 0, w, 8, fill="#222233", outline="")
        color = GREEN if score >= SCORE_THRESHOLD else ORANGE if score >= 0.15 else ACCENT
        if filled > 0:
            c.create_rectangle(0, 0, filled, 8, fill=color, outline="")

    # ── Template management ───────────────────────────────────────────────────

    def _load_templates(self, silent=False):
        if not os.path.exists(TEMPLATES_FILE):
            if not silent:
                messagebox.showwarning(
                    "No Templates",
                    "gesture_templates.pkl not found.\n"
                    "Use 'Rebuild Templates' to build from videos.")
            self._set_status("No templates loaded", ACCENT)
            return False
        try:
            with open(TEMPLATES_FILE, "rb") as f:
                self.templates = pickle.load(f)
            counts = {}
            for t in self.templates:
                counts[t.name] = counts.get(t.name, 0) + 1
            summary = ", ".join(f"{n}×{k}" for k, n in sorted(counts.items()))
            self.templates_label.config(
                text=f"Templates: {len(self.templates)}  ({summary})")
            self._set_status(f"Loaded {len(self.templates)} templates", GREEN)
            self._log(f"Loaded {len(self.templates)} templates: {summary}", TEXT)
            return True
        except Exception as e:
            self._set_status("Failed to load templates", ACCENT)
            self._log(f"Load error: {e}", ACCENT)
            return False

    def _on_load(self):
        self._load_templates(silent=False)

    def _on_rebuild(self):
        """Rebuild templates from gesture_videos in a background thread."""
        if not os.path.isdir(VIDEOS_DIR):
            messagebox.showerror("Error", f"Videos folder not found:\n{VIDEOS_DIR}")
            return

        self.rebuild_btn.config(state=tk.DISABLED, text="Building…")
        self._set_status("Building templates…", ORANGE)
        self._log("Starting template rebuild…", ORANGE)

        def _worker():
            def _cb(msg):
                self.root.after(0, lambda m=msg: self._log(m, SUBTEXT))

            templates = _build_templates_from_videos(VIDEOS_DIR, progress_cb=_cb)

            if templates:
                try:
                    with open(TEMPLATES_FILE, "wb") as f:
                        pickle.dump(templates, f)
                except Exception as e:
                    self.root.after(0, lambda: self._log(f"Save error: {e}", ACCENT))
                    self.root.after(0, self._rebuild_done_error)
                    return
                self.root.after(0, lambda: self._rebuild_done(templates))
            else:
                self.root.after(0, self._rebuild_done_error)

        threading.Thread(target=_worker, daemon=True).start()

    def _rebuild_done(self, templates):
        self.templates = templates
        counts = {}
        for t in templates:
            counts[t.name] = counts.get(t.name, 0) + 1
        summary = ", ".join(f"{n}×{k}" for k, n in sorted(counts.items()))
        self.templates_label.config(
            text=f"Templates: {len(templates)}  ({summary})")
        self._set_status(f"Built {len(templates)} templates", GREEN)
        self._log(f"Done! {len(templates)} templates: {summary}", GREEN)
        self.rebuild_btn.config(state=tk.NORMAL, text="⟳  Rebuild Templates")

    def _rebuild_done_error(self):
        self._set_status("Rebuild failed — check videos folder", ACCENT)
        self._log("Rebuild failed. No templates created.", ACCENT)
        self.rebuild_btn.config(state=tk.NORMAL, text="⟳  Rebuild Templates")

    # ── Helpers ───────────────────────────────────────────────────────────────

    def _set_status(self, msg, color=TEXT):
        self.status_label.config(text=msg, fg=color)

    def _log(self, msg, color=TEXT):
        ts = time.strftime("%H:%M:%S")
        self.log_text.config(state=tk.NORMAL)
        self.log_text.insert(tk.END, f"[{ts}] {msg}\n")
        # Colour the last line
        last_line = f"{int(self.log_text.index(tk.END).split('.')[0]) - 1}.0"
        tag = f"c{time.time_ns()}"
        self.log_text.tag_add(tag, last_line, f"{last_line} lineend")
        self.log_text.tag_config(tag, foreground=color)
        self.log_text.see(tk.END)
        self.log_text.config(state=tk.DISABLED)

    def on_close(self):
        self.running = False
        if self.cap:
            self.cap.release()
        try:
            self.hands.__del__()
        except Exception:
            pass
        self.root.destroy()


# ── Entry point ────────────────────────────────────────────────────────────────

def main():
    root = tk.Tk()
    app  = GestureGUI(root)
    root.protocol("WM_DELETE_WINDOW", app.on_close)
    root.mainloop()


if __name__ == "__main__":
    main()
