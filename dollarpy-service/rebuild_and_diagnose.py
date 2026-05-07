"""
Rebuild gesture templates with proper augmentation.
Processes all video folders, augments each video into multiple templates
by sampling the video at different time windows, then saves gesture_templates.pkl.
"""
import os, sys, pickle, math, cv2
from dollarpy import Template, Point, Recognizer
from copy import deepcopy

# ── path setup ────────────────────────────────────────────────────────────────
SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
VIDEOS_DIR  = os.path.join(SCRIPT_DIR, "gesture_videos")
OUTPUT_FILE = os.path.join(SCRIPT_DIR, "gesture_templates.pkl")

sys.path.insert(0, SCRIPT_DIR)
import mediapipe_compat as mp

# ── mediapipe hands ────────────────────────────────────────────────────────────
mp_hands = mp.solutions.hands
hands    = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=1,
    min_detection_confidence=0.5,
    min_tracking_confidence=0.5,
)

# ── helpers ────────────────────────────────────────────────────────────────────
INDEX_TIP  = 8
WRIST      = 0
MIDDLE_MCP = 9

def extract_points_from_frames(frames_data):
    """
    Index-tip path, raw normalized coords (matches gesture_config defaults).
    Returns list of Point(x, y, stroke_id=0) or None if too few / too little motion.
    """
    points = []
    for fd in frames_data:
        lm = fd.get("hand_landmarks")
        if lm is None:
            continue
        tip = lm.landmark[INDEX_TIP]
        points.append(Point(tip.x, tip.y, stroke_id=0))

    if len(points) < 10:
        return None

    # motion check
    total = 0.0
    for i in range(1, len(points)):
        dx = points[i].x - points[i-1].x
        dy = points[i].y - points[i-1].y
        total += math.sqrt(dx*dx + dy*dy)
    if total < 0.05:
        return None

    return points


def process_video(video_path):
    """
    Read a video, detect hand landmarks with MediaPipe, return list of frame dicts.
    """
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"  ✗ Cannot open: {video_path}")
        return []

    video_fps = cap.get(cv2.CAP_PROP_FPS) or 60.0
    step = max(1, round(video_fps / 60.0))   # sample at ~60 FPS

    frames_data = []
    frame_idx   = 0

    while cap.isOpened():
        ret, frame = cap.read()
        if not ret:
            break
        frame_idx += 1
        if (frame_idx - 1) % step != 0:
            continue

        frame = cv2.resize(frame, (640, 480))
        rgb   = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        res   = hands.process(rgb)

        if res.multi_hand_landmarks:
            frames_data.append({
                "hand_landmarks": res.multi_hand_landmarks[0],
                "frame": frame,
            })

    cap.release()
    return frames_data


def augment_frames(frames_data, n_augments=5):
    """
    Create n_augments overlapping windows from the full frame sequence.
    This simulates 'multiple templates from one video'.
    """
    total = len(frames_data)
    if total < 15:
        return [frames_data]

    windows = []
    # always include the full sequence
    windows.append(frames_data)

    # sliding windows of different sizes and start offsets
    for frac in [0.6, 0.75, 0.85]:
        win_size = max(15, int(total * frac))
        for start_frac in [0.0, 0.15, 0.30]:
            start = int(total * start_frac)
            end   = start + win_size
            if end > total:
                end   = total
                start = max(0, total - win_size)
            windows.append(frames_data[start:end])

    # deduplicate by start/end
    seen = set()
    unique = []
    for w in windows:
        key = (id(w[0]) if w else None, len(w))
        if key not in seen:
            seen.add(key)
            unique.append(w)

    return unique[:n_augments]


# ── main rebuild ───────────────────────────────────────────────────────────────
def rebuild_templates(videos_dir, output_file):
    all_templates = []

    gesture_folders = []
    for item in os.listdir(videos_dir):
        if item == "archive":               # skip archive
            continue
        item_path = os.path.join(videos_dir, item)
        if os.path.isdir(item_path):
            gesture_name = item.lower().replace(" ", "_").replace("-", "_")
            gesture_folders.append((item, item_path, gesture_name))

    if not gesture_folders:
        print(f"No gesture folders found in {videos_dir}")
        return []

    print(f"\nFound {len(gesture_folders)} gesture classes:")
    for _, _, name in gesture_folders:
        print(f"  - {name}")
    print()

    for folder_name, folder_path, gesture_name in gesture_folders:
        video_exts = ('.mp4', '.avi', '.mov', '.mkv', '.flv')
        videos = [f for f in os.listdir(folder_path)
                  if f.lower().endswith(video_exts)]

        if not videos:
            print(f"[{gesture_name}] No videos found — skipping")
            continue

        print(f"[{gesture_name}] Processing {len(videos)} video(s)...")
        gesture_templates = []

        for vfile in videos:
            vpath = os.path.join(folder_path, vfile)
            print(f"  → {vfile}", end=" ")

            frames_data = process_video(vpath)
            if not frames_data:
                print("✗ no hand detected")
                continue

            print(f"({len(frames_data)} frames)", end=" ")

            # augment
            windows = augment_frames(frames_data, n_augments=6)
            created = 0
            for win in windows:
                pts = extract_points_from_frames(win)
                if pts:
                    t = Template(gesture_name, pts)
                    gesture_templates.append(t)
                    created += 1

            print(f"→ {created} templates")

        print(f"  Total for '{gesture_name}': {len(gesture_templates)} templates\n")
        all_templates.extend(gesture_templates)

    print(f"{'='*55}")
    print(f"Grand total: {len(all_templates)} templates")
    print(f"{'='*55}\n")

    with open(output_file, "wb") as f:
        pickle.dump(all_templates, f)
    print(f"✓ Saved to: {output_file}")

    return all_templates


# ── quick sanity check ─────────────────────────────────────────────────────────
def diagnose_templates(templates):
    """Cross-validate each template against all others."""
    if not templates:
        print("No templates to diagnose.")
        return

    print("\n── Template summary ──────────────────────────────────────")
    counts = {}
    for t in templates:
        counts[t.name] = counts.get(t.name, 0) + 1
    for name, count in sorted(counts.items()):
        lengths = [len(t) for t in templates if t.name == name]
        avg_l = sum(lengths) / len(lengths) if lengths else 0
        print(f"  {name:<20} {count:>2} templates  avg_pts={avg_l:.0f}")

    print("\n── Cross-validation (each template vs all others) ────────")
    correct = total = 0
    for i, tmpl in enumerate(templates):
        others = [t for j, t in enumerate(templates) if j != i]
        if not others:
            continue
        rec = Recognizer(deepcopy(others))
        result = rec.recognize(tmpl)
        if result:
            pred, score = result
            ok = (pred == tmpl.name)
            correct += ok
            total   += 1
            status = "✓" if ok else "✗"
            if not ok or score < 0.5:
                print(f"  {status} {tmpl.name:<20} → {pred:<20} score={score:.3f}")
    if total:
        print(f"\n  Accuracy: {correct}/{total} = {correct/total*100:.1f}%")
    print()


if __name__ == "__main__":
    print("=" * 55)
    print("Smart Museum — Template Rebuild & Diagnose")
    print("=" * 55)

    templates = rebuild_templates(VIDEOS_DIR, OUTPUT_FILE)
    if templates:
        diagnose_templates(templates)
    else:
        print("ERROR: No templates created.")
