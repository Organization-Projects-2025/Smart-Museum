"""
Shared gesture preprocessing module.

This module ensures that template generation and live recognition
use EXACTLY the same preprocessing logic.

All point extraction, normalization, and stroke ID assignment
happens here to guarantee consistency.

IMPORTANT: Any change to preprocessing requires rebuilding templates.
"""

import math
from typing import List, Tuple, Optional
from dollarpy import Point


# ============================================================================
# Configuration
# ============================================================================

KEY_LANDMARKS = [0, 4, 8, 12, 16, 20]
"""6 hand landmarks: wrist, thumb tip, index tip, middle tip, ring tip, pinky tip."""

INDEX_TIP = 8
"""Index finger tip (most reliable for swipe gestures)."""

WRIST = 0
"""Wrist landmark (for wrist-relative normalization)."""

MIDDLE_MCP = 9
"""Middle finger MCP joint (for hand scale)."""


# ============================================================================
# Point Extraction Modes
# ============================================================================

def extract_index_tip_point(hand_landmarks, normalize_mode: str = "raw") -> Optional[Point]:
    """
    Extract a single point from the index fingertip (landmark 8).
    
    Args:
        hand_landmarks: MediaPipe hand landmarks
        normalize_mode: "raw" (image coordinates) or "wrist_scale" (wrist-relative, normalized by hand size)
    
    Returns:
        Point object with stroke_id=0, or None if extraction fails
    """
    try:
        index_tip = hand_landmarks.landmark[INDEX_TIP]
        
        if normalize_mode == "wrist_scale":
            # Wrist-relative, scale-normalized
            wrist = hand_landmarks.landmark[WRIST]
            middle_mcp = hand_landmarks.landmark[MIDDLE_MCP]
            
            # Calculate scale from wrist to middle MCP
            dx = middle_mcp.x - wrist.x
            dy = middle_mcp.y - wrist.y
            scale = math.sqrt(dx * dx + dy * dy)
            
            if scale < 0.01:  # Avoid division by near-zero
                return None
            
            # Normalize relative to wrist, scale by hand size
            x = (index_tip.x - wrist.x) / scale
            y = (index_tip.y - wrist.y) / scale
        else:  # "raw"
            # Raw normalized image coordinates (0.0 - 1.0)
            x = index_tip.x
            y = index_tip.y
        
        return Point(x, y, stroke_id=0)
    
    except (AttributeError, IndexError):
        return None


def extract_all_landmarks(hand_landmarks, normalize_mode: str = "raw") -> List[Point]:
    """
    Extract all 6 key landmarks as separate strokes (landmark-based stroke IDs).
    
    Use this mode for complex gestures that need multi-hand-part tracking.
    
    Args:
        hand_landmarks: MediaPipe hand landmarks
        normalize_mode: "raw" or "wrist_scale"
    
    Returns:
        List of Points, one per landmark, with stroke_id = landmark position in KEY_LANDMARKS
    """
    points = []
    
    try:
        for stroke_id, landmark_id in enumerate(KEY_LANDMARKS):
            landmark = hand_landmarks.landmark[landmark_id]
            
            if normalize_mode == "wrist_scale":
                wrist = hand_landmarks.landmark[WRIST]
                middle_mcp = hand_landmarks.landmark[MIDDLE_MCP]
                
                dx = middle_mcp.x - wrist.x
                dy = middle_mcp.y - wrist.y
                scale = math.sqrt(dx * dx + dy * dy)
                
                if scale < 0.01:
                    continue
                
                x = (landmark.x - wrist.x) / scale
                y = (landmark.y - wrist.y) / scale
            else:  # "raw"
                x = landmark.x
                y = landmark.y
            
            points.append(Point(x, y, stroke_id=stroke_id))
    
    except (AttributeError, IndexError):
        pass
    
    return points


def normalize_pixels_to_coords(x_pixel: int, y_pixel: int, 
                               image_width: int, image_height: int) -> Tuple[float, float]:
    """
    Convert pixel coordinates to normalized image coordinates (0.0 - 1.0).
    
    Args:
        x_pixel, y_pixel: Pixel coordinates
        image_width, image_height: Image dimensions
    
    Returns:
        (x_norm, y_norm) normalized to 0.0 - 1.0
    """
    if image_width <= 0 or image_height <= 0:
        return 0.0, 0.0
    
    x_norm = x_pixel / image_width
    y_norm = y_pixel / image_height
    
    # Clamp to [0.0, 1.0]
    x_norm = max(0.0, min(1.0, x_norm))
    y_norm = max(0.0, min(1.0, y_norm))
    
    return x_norm, y_norm


# ============================================================================
# Frame Collection for Gestures
# ============================================================================

def build_points_from_frames(frames_data: List[dict], 
                             mode: str = "index_tip_path",
                             normalize_mode: str = "raw") -> List[Point]:
    """
    Build a list of Points from a sequence of frames.
    
    Args:
        frames_data: List of dicts with keys:
                     - 'hand_landmarks': MediaPipe hand landmarks
                     - 'frame': (optional) raw frame for pixel extraction
        mode: "index_tip_path" (single point per frame, stroke_id=0)
              or "multi_landmark" (6 points per frame, separate stroke IDs)
        normalize_mode: "raw" or "wrist_scale"
    
    Returns:
        List of Point objects ready for dollarpy recognition
    """
    points = []
    
    if mode == "index_tip_path":
        # Single point per frame: index fingertip, stroke_id=0
        for frame_data in frames_data:
            hand_landmarks = frame_data.get("hand_landmarks")
            if hand_landmarks is None:
                continue
            
            point = extract_index_tip_point(hand_landmarks, normalize_mode)
            if point is not None:
                points.append(point)
    
    elif mode == "multi_landmark":
        # Multiple points per frame: all 6 landmarks, stroke_id per landmark type
        for frame_data in frames_data:
            hand_landmarks = frame_data.get("hand_landmarks")
            if hand_landmarks is None:
                continue
            
            frame_points = extract_all_landmarks(hand_landmarks, normalize_mode)
            points.extend(frame_points)
    
    return points


def validate_gesture_points(points: List[Point], min_points: int = 10) -> bool:
    """
    Validate that a gesture has enough points for recognition.
    
    Args:
        points: List of Point objects
        min_points: Minimum required points
    
    Returns:
        True if points list is valid for recognition
    """
    return len(points) >= min_points


def calculate_gesture_bounds(points: List[Point]) -> Tuple[float, float, float, float]:
    """
    Calculate the bounding box of a gesture.
    
    Returns:
        (min_x, max_x, min_y, max_y)
    """
    if not points:
        return 0.0, 0.0, 0.0, 0.0
    
    x_coords = [p.x for p in points]
    y_coords = [p.y for p in points]
    
    return min(x_coords), max(x_coords), min(y_coords), max(y_coords)


def calculate_gesture_motion(points: List[Point]) -> float:
    """
    Calculate total motion distance of a gesture (Euclidean distance between consecutive points).
    
    Returns:
        Total distance traveled by the gesture
    """
    if len(points) < 2:
        return 0.0
    
    total_distance = 0.0
    for i in range(1, len(points)):
        dx = points[i].x - points[i - 1].x
        dy = points[i].y - points[i - 1].y
        total_distance += math.sqrt(dx * dx + dy * dy)
    
    return total_distance


# ============================================================================
# Preprocessing Pipeline
# ============================================================================

def preprocess_gesture_frames(frames_data: List[dict],
                              mode: str = "index_tip_path",
                              normalize_mode: str = "raw",
                              min_motion: float = 0.05) -> Optional[List[Point]]:
    """
    Complete preprocessing pipeline for a gesture.
    
    This is the single source of truth for preprocessing both:
    - Template generation (gesture_processor.py)
    - Live recognition (gesture_service.py)
    
    Args:
        frames_data: List of frame data dicts
        mode: Gesture representation mode
        normalize_mode: Normalization strategy
        min_motion: Minimum total gesture motion to consider valid
    
    Returns:
        List of Points, or None if gesture is invalid
    """
    # Extract points using the chosen mode
    points = build_points_from_frames(frames_data, mode, normalize_mode)
    
    # Validate minimum points
    if not validate_gesture_points(points, min_points=10):
        return None
    
    # Check motion amount
    motion = calculate_gesture_motion(points)
    if motion < min_motion:
        return None
    
    return points


# ============================================================================
# Debugging and Introspection
# ============================================================================

def describe_preprocessing_mode(mode: str, normalize_mode: str) -> str:
    """
    Return a human-readable description of the preprocessing configuration.
    """
    mode_desc = {
        "index_tip_path": "Index fingertip path (single point per frame, stroke_id=0)",
        "multi_landmark": "All 6 landmarks (separate stroke IDs per landmark type)"
    }.get(mode, "Unknown mode")
    
    norm_desc = {
        "raw": "Raw normalized image coordinates (0.0-1.0)",
        "wrist_scale": "Wrist-relative, scaled by hand size"
    }.get(normalize_mode, "Unknown normalization")
    
    return f"{mode_desc} + {norm_desc}"


def log_gesture_stats(points: List[Point], label: str = "Gesture") -> None:
    """
    Print debugging statistics for a gesture.
    """
    if not points:
        print(f"{label}: No points")
        return
    
    min_x, max_x, min_y, max_y = calculate_gesture_bounds(points)
    motion = calculate_gesture_motion(points)
    
    print(f"{label} Stats:")
    print(f"  Points: {len(points)}")
    print(f"  Motion: {motion:.4f}")
    print(f"  Bounds: X=[{min_x:.3f}, {max_x:.3f}] Y=[{min_y:.3f}, {max_y:.3f}]")
    print(f"  Range: X={max_x - min_x:.3f}, Y={max_y - min_y:.3f}")
