"""
Gesture recognition configuration.

All tunable parameters in one place.
IMPORTANT: Change these only if you rebuild templates afterwards.
"""

import os


# ============================================================================
# Recognition Parameters
# ============================================================================

# Window size for fixed-window recognition
WINDOW_FRAMES = int(os.environ.get("GESTURE_WINDOW_FRAMES", "25"))
"""
Number of frames to collect before attempting recognition.
Typical: 20-30 frames at 60 FPS = 0.33-0.5 seconds.
"""

# Confidence threshold for accepting a gesture
CONFIDENCE_THRESHOLD = float(os.environ.get("GESTURE_CONFIDENCE_THRESHOLD", "0.25"))
"""
Minimum dollarpy score to accept a recognition.
Lowered to 0.25 for sliding window continuous detection.
Range: 0.0-1.0, where 1.0 is perfect match.
"""

# Cooldown between gesture recognitions
COOLDOWN_SECONDS = float(os.environ.get("GESTURE_COOLDOWN", "0.8"))
"""
Minimum time between two consecutive gesture recognitions.
Prevents spam from the same gesture.
"""

# Number of consecutive high-confidence recognitions to confirm a gesture
STABILITY_REQUIRED = int(os.environ.get("GESTURE_STABILITY", "2"))
"""
How many times the same gesture must be recognized consecutively (above threshold)
before emitting it. Reduces false positives from occasional noise.
"""

# Minimum motion distance for a valid gesture
MIN_MOTION_DISTANCE = float(os.environ.get("GESTURE_MIN_MOTION", "0.05"))
"""
Minimum total Euclidean distance for a gesture to be considered valid.
Prevents recognition from very small, jittery movements.
"""

# Minimum points for recognition
MIN_GESTURE_POINTS = int(os.environ.get("GESTURE_MIN_POINTS", "10"))
"""
Minimum number of points required in a gesture before recognition is attempted.
"""


# ============================================================================
# Preprocessing Parameters
# ============================================================================

# Gesture representation mode
GESTURE_MODE = os.environ.get("GESTURE_MODE", "index_tip_path")
"""
How to extract gesture points:
- "index_tip_path": Track only landmark 8 (index fingertip), one Point per frame.
                    Best for swipes, circles, paths. Single stroke (stroke_id=0).
- "multi_landmark": Track all 6 landmarks, 6 points per frame.
                    Each landmark type gets its own stroke_id (0-5).
                    Best for complex hand shape changes.
"""

# Normalization strategy
GESTURE_NORMALIZATION_MODE = os.environ.get("GESTURE_NORMALIZATION_MODE", "raw")
"""
How to normalize coordinates:
- "raw": Use normalized image coordinates directly (0.0-1.0).
         Good for gestures where absolute screen position matters (swipes).
- "wrist_scale": Normalize relative to wrist, scale by hand size.
                 Good for hand-relative gestures (finger movements).
"""

# Motion detection threshold (for optional motion segmentation)
GESTURE_MOTION_START_THRESHOLD = float(os.environ.get("GESTURE_MOTION_START_THRESHOLD", "0.035"))
"""
Distance (in normalized coords) hand must move to start capturing.
Used only if motion segmentation is enabled.
Typical: 0.035 ≈ 3-4 cm at arm's length on 640-wide frame.
"""

# Frames without hand before resetting motion reference
GESTURE_NO_HAND_RESET_FRAMES = int(os.environ.get("GESTURE_NO_HAND_RESET_FRAMES", "45"))
"""
How many frames without hand detection before resetting the "rest" position.
At 60 FPS: 45 frames ≈ 0.75 seconds.
"""


# ============================================================================
# MediaPipe Configuration
# ============================================================================

# MediaPipe hand detection confidence
MEDIAPIPE_DETECTION_CONFIDENCE = float(os.environ.get("MEDIAPIPE_DETECTION_CONFIDENCE", "0.6"))
"""
Minimum confidence for MediaPipe to detect a hand.
Range: 0.0-1.0. Higher = stricter.
"""

MEDIAPIPE_TRACKING_CONFIDENCE = float(os.environ.get("MEDIAPIPE_TRACKING_CONFIDENCE", "0.6"))
"""
Minimum confidence for MediaPipe to track a hand across frames.
Range: 0.0-1.0. Higher = stricter.
"""


# ============================================================================
# Gesture Categories
# ============================================================================

# Dynamic gestures (recognized via dollarpy path matching)
DYNAMIC_GESTURES = [
    "swipe_left",
    "swipe_right",
    "swipe_up",
    "swipe_down",
    "circle",
    "x_mark",
    "line",
]

# Static hand poses (recognized via MediaPipe rules or separate classifier)
# NOT handled by dollarpy (for now)
STATIC_GESTURES = [
    "open_palm",
    "fist",
    "peace_sign",
    "thumbs_up",
    "thumbs_down",
    "pinch",
    "ok_sign",
    "pointing",
]


# ============================================================================
# Debugging and Logging
# ============================================================================

# Enable detailed logging
DEBUG_GESTURES = os.environ.get("DEBUG_GESTURES", "").lower() in ("1", "true", "yes")
"""
If True, print detailed recognition logs including:
- Number of points
- Preprocessing mode
- Score before/after threshold
- Rejected matches
"""

# Log rejected matches
LOG_REJECTED = os.environ.get("LOG_REJECTED_GESTURES", "").lower() in ("1", "true", "yes")
"""
If True, print info about gestures that scored below threshold.
Useful for tuning CONFIDENCE_THRESHOLD.
"""


# ============================================================================
# Utility Functions
# ============================================================================

def get_config_summary() -> str:
    """Return a summary of the current configuration."""
    return f"""
Gesture Recognition Configuration:
  Mode: {GESTURE_MODE}
  Normalization: {GESTURE_NORMALIZATION_MODE}
  Window: {WINDOW_FRAMES} frames
  Threshold: {CONFIDENCE_THRESHOLD:.2f}
  Cooldown: {COOLDOWN_SECONDS:.2f}s
  Stability: {STABILITY_REQUIRED} recognitions
  Min motion: {MIN_MOTION_DISTANCE:.3f}
  MediaPipe confidence: {MEDIAPIPE_DETECTION_CONFIDENCE:.2f}
  Debug: {DEBUG_GESTURES}
""".strip()
