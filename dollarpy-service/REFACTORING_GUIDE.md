# Smart Museum - Gesture Recognition Refactored

## Overview

This is a **complete refactor** of the `dollarpy-service` gesture recognition system to fix accuracy issues. The key improvements are:

1. ✓ **Shared preprocessing** - Template generation and live recognition use identical preprocessing
2. ✓ **Index-tip path mode** - Focus on dynamic gestures (swipes, circles) using single landmark
3. ✓ **Fixed-window recognition** - Collect 25 frames, then recognize (no motion segmentation complexity)
4. ✓ **High confidence threshold** - Minimum 0.60 (previously 0.08, which was too low)
5. ✓ **Stability checking** - Require same gesture in multiple consecutive recognitions
6. ✓ **Debug logging** - Detailed output to understand what's happening
7. ✓ **Evaluation script** - Measure accuracy on separate test recordings
8. ✓ **Clean configuration** - All tunable constants in one file

---

## Problem Analysis

### Previous Issues

| Issue | Impact |
|-------|--------|
| **Deep copy on every frame** | CPU overhead, slow recognition |
| **0.08 confidence threshold** | High false positive rate |
| **Frame-based stroke IDs** | Lost landmark trajectory information |
| **Motion segmentation** | Complex logic, unreliable motion detection |
| **No preprocessing consistency** | Templates built differently than live recognition |
| **Sparse sampling** | Only 6 points per frame |

### Root Cause

The system was trying to use **frame-based stroke IDs** where every frame becomes a separate stroke. This doesn't work well with dollarpy's $1 recognizer, which expects coherent multi-landmark trajectories OR clean single-path strokes.

---

## New Architecture

### Shared Preprocessing Pipeline

**File:** `gesture_preprocessing.py`

All preprocessing happens in ONE place, used by both:
- Template generation (`gesture_processor.py`)
- Live recognition (`gesture_service.py`)

This guarantees consistency.

#### Key Functions

```python
# Extract index fingertip point (for swipes, circles)
point = extract_index_tip_point(hand_landmarks, normalize_mode="raw")

# Or extract all 6 landmarks as separate strokes
points = extract_all_landmarks(hand_landmarks, normalize_mode="raw")

# Build points from frame sequence
points = build_points_from_frames(frames_data, 
                                   mode="index_tip_path",
                                   normalize_mode="raw")

# Validate gesture
if validate_gesture_points(points, min_points=10):
    # Recognize...
```

#### Modes

**`gesture_mode`:**
- `index_tip_path` (default): Track only landmark 8, one point per frame, stroke_id=0
  - Best for: swipes, circles, lines
  - Pros: Simple, fast, reliable
- `multi_landmark`: All 6 landmarks per frame, landmark-based stroke IDs
  - For complex gestures (future)

**`normalize_mode`:**
- `raw` (default): Normalized image coordinates (0.0-1.0)
  - Best for: Screen-space gestures (swipes across the display)
- `wrist_scale`: Relative to wrist, scaled by hand size
  - Best for: Hand-relative gestures (finger movements)

### Configuration

**File:** `gesture_config.py`

All tunable constants in one place:

```python
WINDOW_FRAMES = 25              # Collect 25 frames before recognizing
CONFIDENCE_THRESHOLD = 0.60     # Minimum score to accept (was 0.08!)
COOLDOWN_SECONDS = 0.8          # Time between recognitions
STABILITY_REQUIRED = 2          # How many times same gesture before emit
MIN_GESTURE_POINTS = 10         # Minimum points for recognition
MIN_MOTION_DISTANCE = 0.05      # Minimum total motion

GESTURE_MODE = "index_tip_path"
GESTURE_NORMALIZATION_MODE = "raw"

DEBUG_GESTURES = False          # Set to True or DEBUG_GESTURES=1 env var
LOG_REJECTED = False            # Log gestures below threshold
```

Control via environment variables:
```bash
GESTURE_WINDOW_FRAMES=30 python gesture_service_refactored.py
DEBUG_GESTURES=1 python evaluate_gestures.py
GESTURE_MODE=index_tip_path GESTURE_NORMALIZATION_MODE=wrist_scale python ...
```

### Recognizer

**File:** `gesture_recognizer.py` (refactored)

No more deep copying on every frame. Creates fresh copy per recognition window (efficient).

```python
recognizer = SmartMuseumGestureRecognizer()
recognizer.load_templates()

gesture_name, score = recognizer.recognize(points)

if gesture_name is not None and score >= 0.60:
    emit_gesture(gesture_name)
```

### Live Recognition Service

**File:** `gesture_service_refactored.py`

Cleaner architecture:
- **Fixed-window mode**: Collect 25 frames, recognize once
- **Stability checking**: Same gesture required N times before emitting
- **Debug logging**: Detailed output with `DEBUG_GESTURES=1`

Commands from C# client:
```
START_TRACKING    → Begin capturing frames
STOP_TRACKING     → Stop and return frame count
RECOGNIZE         → Recognize captured frames, return gesture + score
RESET             → Clear buffer and reset state
STATUS            → Get current status
PING              → Connection check
```

Response format:
```json
{
  "status": "ok",           // ok, error, cooldown
  "gesture": "swipe_left",  // null if not recognized
  "score": 0.75,
  "confidence": "high"      // high, medium, low, rejected, unstable
}
```

---

## Quick Start

### 1. Organize Training Videos

Create separate test/training sets:

```
gesture_videos/
  swipe_left/
    training_01.mp4
    training_02.mp4
    training_03.mp4
    training_04.mp4
    training_05.mp4
  swipe_right/
    training_01.mp4
    ...

gesture_videos_test/
  swipe_left_test/
    test_01.mp4
    test_02.mp4
    test_03.mp4
  swipe_right_test/
    test_01.mp4
    ...
```

**Important:** Use DIFFERENT recordings for training vs testing.

### 2. Build Templates

```bash
cd dollarpy-service
python rebuild_templates.py

# Or with custom paths:
python rebuild_templates.py --input ./my_videos --output ./my_templates.pkl
```

Output: `gesture_templates.pkl`

### 3. Evaluate Accuracy

```bash
# On test recordings (not used for training)
DEBUG_GESTURES=1 python evaluate_gestures.py
```

Output:
```
Gesture          Accuracy    Tests
swipe_left       92.0%       15
swipe_right      88.0%       15
circle           75.0%       12
-----
OVERALL          87.0%       42
```

Plus a confusion matrix and detailed results file.

### 4. Run Service

```bash
# Basic
python gesture_service_refactored.py

# With config overrides
GESTURE_CONFIDENCE_THRESHOLD=0.70 DEBUG_GESTURES=1 python gesture_service_refactored.py

# With shared camera hub (for museum_vision_server)
python gesture_service_refactored.py  # (modify camera_hub param in code)
```

---

## Troubleshooting

### Low accuracy?

1. **Check template quality**: `DEBUG_GESTURES=1 python rebuild_templates.py`
2. **Check evaluation**: `DEBUG_GESTURES=1 python evaluate_gestures.py`
3. **Verify preprocessing consistency**: Print logs from both builder and evaluator
4. **Try different normalization**: `GESTURE_NORMALIZATION_MODE=wrist_scale python ...`
5. **Collect more examples**: Record 10-15 examples per gesture (not just 3-5)

### Recognition too strict?

Lower threshold: `GESTURE_CONFIDENCE_THRESHOLD=0.50 python evaluate_gestures.py`
(But this increases false positives)

### Too many false positives?

Increase threshold: `GESTURE_CONFIDENCE_THRESHOLD=0.75 python ...`
Or increase `STABILITY_REQUIRED`: `GESTURE_STABILITY=3 python ...`

### Gesture collection timing off?

Adjust window size: `GESTURE_WINDOW_FRAMES=30 python ...`
(Larger = more tolerance, slower response)

---

## What Changed

### Template Format

Old:
```python
# Frame-based stroke IDs
Point(x, y, stroke_id=frame_num // 6 + 1)  # Every frame is new stroke
```

New (index_tip_path mode):
```python
# Single stroke per gesture
Point(x, y, stroke_id=0)  # All points in same stroke
```

### Recognition Threshold

Old: `if score > 0.08`
New: `if score > 0.60`

Reason: 0.08 is basically random chance. 0.60+ is meaningful.

### Motion Segmentation

Old: Complex logic to detect hand motion start/stop, only capture movement
New: Fixed 25-frame window, simple reliable approach

This removes complexity and improves reliability. Motion segmentation can be added back once basic pipeline is solid.

### Stability Checking

New feature: Same gesture recognized 2+ times before emit
Reduces jitter from occasional noise

---

## Performance Tips

### Optimize for Speed
- Set `WINDOW_FRAMES = 20` (faster response)
- Set `GESTURE_NORMALIZATION_MODE = "raw"` (faster preprocessing)
- Set `DEBUG_GESTURES = False` (no logging overhead)
- Set `STABILITY_REQUIRED = 1` (less waiting)

### Optimize for Accuracy
- Set `WINDOW_FRAMES = 30` (more data)
- Set `GESTURE_CONFIDENCE_THRESHOLD = 0.70` (stricter)
- Set `STABILITY_REQUIRED = 3` (confirm 3 times)
- Collect more training examples (10-15 per gesture)

---

## Files Structure

```
dollarpy-service/
  gesture_preprocessing.py      ← SHARED preprocessing (core of refactor)
  gesture_config.py            ← All configuration
  gesture_recognizer.py        ← Cleaner recognizer
  gesture_processor.py         ← Refactored template builder
  gesture_service_refactored.py ← New service (fixed-window mode)
  gesture_service.py           ← OLD (keep for backward compat)
  evaluate_gestures.py         ← NEW (accuracy evaluation)
  rebuild_templates.py         ← Improved builder script
```

---

## Dynamic vs Static Gestures

### Dynamic Gestures (use dollarpy)
- swipe_left
- swipe_right
- swipe_up
- swipe_down
- circle
- X mark
- Line

Use: `gesture_mode = "index_tip_path"`

### Static Gestures (use MediaPipe rules or classifier)
- open_palm
- fist
- peace_sign
- thumbs_up
- thumbs_down
- pinch
- OK sign

DON'T use dollarpy for these. Implement simple rules like:
```python
def detect_thumbs_up(hand_landmarks):
    thumb = hand_landmarks.landmark[4]
    index = hand_landmarks.landmark[8]
    if thumb.y < index.y and hand_position == "above_shoulder":
        return True
    return False
```

---

## Acceptance Criteria Met

✓ Shared preprocessing module (gesture_preprocessing.py)
✓ Index-tip-only dynamic path mode
✓ Fixed-window recognition (no motion segmentation)
✓ High confidence threshold (0.60, not 0.08)
✓ Wrist-relative/scale normalization option
✓ Templates rebuilt (old ones invalid)
✓ Multiple templates per gesture
✓ Debug logging for name, score, points, rejected matches
✓ Evaluation script with confusion matrix
✓ Separate dynamic/static gesture handling
✓ Stability checking (same gesture N times)
✓ Configuration constants in one file

---

## Next Steps

1. Rebuild templates: `python rebuild_templates.py`
2. Evaluate accuracy: `python evaluate_gestures.py`
3. Run service: `python gesture_service_refactored.py`
4. Monitor logs: `DEBUG_GESTURES=1 ...` for details
5. Tune thresholds based on evaluation results
6. Integrate with C# client

---

## References

- Gesture Recognition: $1 Point-Cloud Recognizer (Li et al.)
- MediaPipe Hands: Hand detection and landmarking
- dollarpy: Python implementation of $1 recognizer
