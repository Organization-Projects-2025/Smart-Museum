"""
Smart Museum — Real-Time Gesture Recognition
Simple, single-purpose: show camera + detect gestures live.
"""

import os, sys, time, math, pickle, threading, collections, cv2
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
VIDEOS_DIR       = os.path.join(SCRIPT_DIR, "gesture_videos")   # legacy subfolder dir
MOVES_DIR        = os.path.join(SCRIPT_DIR, "moves")            # flat .mp4 files
MAX_FRAMES       = 30      # sliding window size (frames)
MIN_POINTS       = 7       # min frames before attempting recognition (must match gesture_service)
MIN_MOTION       = 0.04    # min cumulative centroid travel (0–1 scale; matches gesture_service)
SCORE_THRESHOLD  = 0.30    # min score to show a green "detected" label
FRAME_DELAY_MS   = 30      # ~33 FPS tkinter loop (was 16)
INDEX_TIP        = 8       # MediaPipe landmark id
RECO_EVERY_N     = 7       # run dollarpy every N new hand frames
TRACK_TIPS       = (8, 12, 20)   # index, middle, pinky fingertip IDs — centroid = pen
CLEAR_THRESHOLD  = 0.40   # clear buffer on any detection at/above this score
MP_W, MP_H       = 320, 240  # resolution fed to MediaPipe (smaller = faster)
DISP_W, DISP_H   = 480, 360  # resolution shown in the GUI label
SMOOTH_WIN       = 3       # frames to average for tip-position smoothing


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

def _is_pointing(lm):
    """
    Return True when ONLY the index finger is clearly extended (pointing pose).
    Uses MediaPipe landmark y-coords: tip.y < pip.y means finger is up
    (y increases downward in image space).
    """
    index_up  = lm.landmark[8].y  < lm.landmark[6].y   # index tip above PIP
    middle_up = lm.landmark[12].y < lm.landmark[10].y  # middle curled?
    ring_up   = lm.landmark[16].y < lm.landmark[14].y  # ring curled?
    pinky_up  = lm.landmark[20].y < lm.landmark[18].y  # pinky curled?
    return index_up and not middle_up and not ring_up and not pinky_up


def _extract_points(frames_buffer):
    """
    Index-fingertip path in raw normalised coords → list[Point].
    Returns None when there is not enough data or motion.
    """
    pts = []
    for fd in frames_buffer:
        # New live-capture format: pre-smoothed (x, y)
        if "x" in fd and "y" in fd:
            pts.append(Point(fd["x"], fd["y"], stroke_id=0))
        else:
            # Legacy / template-build format: raw MediaPipe landmark object
            lm = fd.get("lm")
            if lm is None:
                continue
            # Centroid of index, middle, ring tips
            tips = [lm.landmark[i] for i in TRACK_TIPS]
            tx = sum(t.x for t in tips) / len(tips)
            ty = sum(t.y for t in tips) / len(tips)
            pts.append(Point(tx, ty, stroke_id=0))

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


def _build_templates_from_moves(moves_dir, progress_cb=None):
    """
    Process every flat video file directly inside `moves_dir`.

    Gesture name is derived from the filename stem:
        circle.mp4  → "circle"
        left.mp4    → "left"
        top.mp4     → "top"

    Only landmark 8 (index fingertip) is used — single-stroke path.
    Multiple overlapping windows are generated per video for augmentation.

    Returns list[Template].
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

    # Collect flat video files (no subdirectory walk)
    videos = [
        f for f in os.listdir(moves_dir)
        if f.lower().endswith(video_exts)
        and os.path.isfile(os.path.join(moves_dir, f))
    ]

    if not videos:
        if progress_cb:
            progress_cb(f"No video files found in {moves_dir}")
        hands.__del__()
        return all_templates

    for vfile in sorted(videos):
        # Gesture name = filename without extension, lowercased
        gesture_name = os.path.splitext(vfile)[0].lower().replace(" ", "_").replace("-", "_")
        vpath = os.path.join(moves_dir, vfile)

        if progress_cb:
            progress_cb(f"Processing {gesture_name} — {vfile}")

        cap  = cv2.VideoCapture(vpath)
        fps  = cap.get(cv2.CAP_PROP_FPS) or 60.0
        step = max(1, round(fps / 60.0))
        idx  = 0
        fbuf = []

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
            if progress_cb:
                progress_cb(f"  ✗ {gesture_name}: no hand detected in {vfile}")
            continue

        # ── Augment: generate multiple overlapping windows ─────────────────
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


def _build_templates_from_videos(videos_dir, progress_cb=None):
    """
    (Legacy) Process every video in every sub-folder of `videos_dir`.
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

        # Per-frame counters for throttled recognition
        self._new_frames   = 0   # new hand frames since last reco run
        self._reco_running = False  # guard against overlapping reco threads

        # Pointing-pose gate + tip smoothing
        self._raw_tip_buf = collections.deque(maxlen=SMOOTH_WIN)  # last N raw tips
        self._pointing    = False   # True when pointing pose is active

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

        # ── Run MediaPipe on a small frame for speed ──────────────────────
        small = cv2.resize(frame, (MP_W, MP_H))
        rgb   = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
        res   = self.hands.process(rgb)

        # Display frame (slightly larger than MP input, less than full 640×480)
        disp  = cv2.resize(frame, (DISP_W, DISP_H))

        hand_detected = bool(res.multi_hand_landmarks)

        if hand_detected:
            lm = res.multi_hand_landmarks[0]

            # Draw skeleton scaled to display frame
            import copy
            lm_disp = copy.deepcopy(lm)
            self.mp_drawing.draw_landmarks(
                disp, lm_disp, mp.solutions.hands.Hands.HAND_CONNECTIONS)

            # ── Always record — centroid of 3 fingertips, smoothed ────────────
            self._pointing = True
            h, w, _ = disp.shape

            # Centroid of index, middle, ring tips
            tips3 = [lm.landmark[i] for i in TRACK_TIPS]
            raw_x = sum(t.x for t in tips3) / len(tips3)
            raw_y = sum(t.y for t in tips3) / len(tips3)

            # ── 3-frame moving-average smoothing ──────────────────────────────
            self._raw_tip_buf.append((raw_x, raw_y))
            sx = sum(p[0] for p in self._raw_tip_buf) / len(self._raw_tip_buf)
            sy = sum(p[1] for p in self._raw_tip_buf) / len(self._raw_tip_buf)

            # Push smoothed centroid into sliding window
            self.buf.append({"x": sx, "y": sy})
            if len(self.buf) > MAX_FRAMES:
                self.buf.pop(0)
            self._new_frames += 1

            # Draw small dots on each of the 3 tracked tips
            for tid in TRACK_TIPS:
                t = lm.landmark[tid]
                tcx, tcy = int(t.x * w), int(t.y * h)
                cv2.circle(disp, (tcx, tcy), 5, (0, 160, 220), -1)
            # Draw centroid (larger, white ring)
            cx, cy = int(sx * w), int(sy * h)
            cv2.circle(disp, (cx, cy), 9, (0, 200, 255), -1)
            cv2.circle(disp, (cx, cy), 12, (255, 255, 255), 2)
        else:
            # Hand lost — slowly drain buffer
            self._pointing = False
            self._raw_tip_buf.clear()
            if self.buf:
                self.buf.pop(0)
            self._new_frames = 0

        # OSD overlay on display frame
        self._draw_osd(disp, hand_detected)

        # ── Throttled recognition: every RECO_EVERY_N new hand frames ─────
        if (self.templates
                and len(self.buf) >= MIN_POINTS
                and self._new_frames >= RECO_EVERY_N
                and not self._reco_running):
            self._new_frames = 0
            self._reco_running = True
            buf_snap = list(self.buf)   # snapshot so camera loop can keep going
            threading.Thread(
                target=self._reco_worker, args=(buf_snap,), daemon=True
            ).start()

        # Update sidebar stats
        self.frames_label.config(
            text=f"Buffer: {len(self.buf)}/{MAX_FRAMES} frames")

        # Show frame in GUI
        img   = Image.fromarray(cv2.cvtColor(disp, cv2.COLOR_BGR2RGB))
        imgtk = ImageTk.PhotoImage(image=img)
        self.video_label.imgtk = imgtk
        self.video_label.config(image=imgtk)

        self.root.after(FRAME_DELAY_MS, self._update_frame)

    def _draw_osd(self, frame, hand_detected):
        """Draw on-screen debug info on the camera frame."""
        buf_len  = len(self.buf)
        fh, fw   = frame.shape[:2]
        bar_max  = fw // 2 - 20

        # Background strip (full width, compact height)
        cv2.rectangle(frame, (0, 0), (fw, 80), (0, 0, 0), -1)
        cv2.rectangle(frame, (0, 0), (fw, 80), (30, 30, 60), 1)

        # Hand status
        hand_color = (0, 230, 118) if hand_detected else (200, 60, 60)
        hand_text  = "HAND: DETECTED" if hand_detected else "HAND: NOT FOUND"
        cv2.putText(frame, hand_text, (10, 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, hand_color, 2)

        # ── Tracking indicator ────────────────────────────────────────────────
        if hand_detected:
            rec_color = (0, 200, 255)
            rec_text  = "● TRACKING"
        else:
            rec_color = (120, 120, 140)
            rec_text  = "○ NO HAND"
        cv2.putText(frame, rec_text, (fw // 2 - 10, 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, rec_color, 2)

        # Buffer bar (scales with frame width)
        bar_w = int((buf_len / MAX_FRAMES) * bar_max)
        cv2.rectangle(frame, (10, 30), (10 + bar_max, 42), (40, 40, 60), -1)
        bar_color = (0, 255, 80) if self._pointing else (200, 130, 0) if buf_len >= MIN_POINTS else (80, 80, 100)
        if bar_w > 0:
            cv2.rectangle(frame, (10, 30), (10 + bar_w, 42), bar_color, -1)
        cv2.putText(frame, f"Buffer {buf_len}/{MAX_FRAMES}", (10, 62),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.45, (180, 180, 200), 1)

        # Templates count (right half, below recording indicator)
        cv2.putText(frame, f"Templates: {len(self.templates)}", (fw // 2 - 10, 62),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.45, (180, 180, 200), 1)

        # Last result (bottom of frame)
        if self.last_name and (time.time() - self.last_time) < 2.0:
            label = self.last_name.replace("_", " ").upper()
            score_color = (0, 230, 118) if self.last_score >= SCORE_THRESHOLD else (255, 176, 0)
            cv2.putText(frame, label, (10, fh - 12),
                        cv2.FONT_HERSHEY_SIMPLEX, 1.0, score_color, 2)
            cv2.putText(frame, f"{self.last_score:.3f}", (10, fh - 42),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, score_color, 2)

    # ── Recognition ──────────────────────────────────────────────────────────

    def _reco_worker(self, buf_snap):
        """Run dollarpy in a background thread, post results back to UI thread."""
        try:
            pts = _extract_points(buf_snap)
            if pts is None:
                return
            name, score = _recognize_points(self.templates, pts)
            if name is not None:
                self.root.after(0, lambda n=name, s=score: self._on_reco_result(n, s))
        finally:
            self._reco_running = False

    def _on_reco_result(self, name, score):
        """Called on the UI thread with dollarpy result."""
        self.last_name  = name
        self.last_score = score

        now = time.time()
        in_cooldown = (now - self.last_time) < self.cooldown

        # Update score bar
        self._update_score_bar(score)
        self.score_label.config(text=f"Score: {score:.3f}")

        # Always clear buffer once we're confident enough — fresh start for next gesture
        if score >= CLEAR_THRESHOLD:
            self.buf.clear()
            self._new_frames = 0
            self._raw_tip_buf.clear()

        if score >= SCORE_THRESHOLD and not in_cooldown:
            display = name.replace("_", " ").title()
            self.gesture_label.config(text=display, fg=GREEN)
            self.last_time = now
            self._log(f"✓ {display}  [{score:.3f}]", GREEN)
        elif score >= SCORE_THRESHOLD * 0.6:
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
        """Rebuild templates — prefer gesture_videos/ (subfolder layout), fall back to moves/."""
        if os.path.isdir(VIDEOS_DIR):
            source_dir   = VIDEOS_DIR
            build_fn     = _build_templates_from_videos
            source_label = "gesture_videos/"
        elif os.path.isdir(MOVES_DIR):
            source_dir   = MOVES_DIR
            build_fn     = _build_templates_from_moves
            source_label = "moves/"
        else:
            messagebox.showerror(
                "Error",
                f"Neither gesture_videos/ nor moves/ folder was found.\n"
                f"Expected at:\n  {VIDEOS_DIR}\n  {MOVES_DIR}")
            return

        self.rebuild_btn.config(state=tk.DISABLED, text="Building…")
        self._set_status(f"Building templates from {source_label}…", ORANGE)
        self._log(f"Starting template rebuild from {source_label}…", ORANGE)

        def _worker():
            def _cb(msg):
                self.root.after(0, lambda m=msg: self._log(m, SUBTEXT))

            templates = build_fn(source_dir, progress_cb=_cb)

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
