# Gesture Recognition Preprocessing Consistency

## Overview
This document ensures that preprocessing steps are IDENTICAL between template building and real-time recognition to guarantee accurate gesture matching.

## Critical Consistency Requirements

### 1. MediaPipe Configuration
All components use the SAME MediaPipe Hands configuration:

```python
self.hands = self.mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=1,  # Single hand tracking
    min_detection_confidence=0.6,
    min_tracking_confidence=0.6
)
```

**Applied to:**
- `gesture_processor.py` (template building)
- `gesture_service.py` (C# real-time recognition)
- `gesture_gui.py` (GUI testing)

### 2. Frame Resolution
All components use the SAME frame resolution:

```python
frame = cv2.resize(frame, (640, 480))
```

**Why this matters:**
- Landmark coordinates are in pixels
- Different resolutions = different pixel coordinates
- Templates built at 480x320 won't match recognition at 640x480

### 3. Landmark Selection
All components track the SAME 6 landmarks:

```python
key_landmarks = [0, 4, 8, 12, 16, 20]
# 0  = Wrist
# 4  = Thumb tip
# 8  = Index finger tip
# 12 = Middle finger tip
# 16 = Ring finger tip
# 20 = Pinky tip
```

### 4. Stroke ID Assignment
**CRITICAL**: All components use the SAME stroke ID calculation:

```python
stroke_id = len(points) // 6 + 1

for landmark_id in key_landmarks:
    landmark = hand_landmarks.landmark[landmark_id]
    x = int(landmark.x * image_width)
    y = int(landmark.y * image_height)
    points.append(Point(x, y, stroke_id))
```

**Why this matters:**
- Stroke IDs group points from the same frame
- All 6 landmarks in one frame get the SAME stroke ID
- This tells dollarpy that these points are part of the same "stroke"
- Inconsistent stroke IDs = poor recognition

**Previous Issue (FIXED):**
- Template building used `frame_count` as stroke ID
- Real-time used `len(points) // 6 + 1` as stroke ID
- This caused mismatches even for identical gestures

### 5. Point Collection Order
Points are collected in the SAME order:

1. For each frame with hand detected
2. Calculate stroke_id = len(points) // 6 + 1
3. For each landmark in [0, 4, 8, 12, 16, 20]:
   - Get x, y coordinates
   - Append Point(x, y, stroke_id)

## Preprocessing Pipeline Comparison

### Template Building (gesture_processor.py)
```python
1. Open video file
2. Resize frame to (640, 480)
3. Convert BGR to RGB
4. Process with MediaPipe Hands
5. For each hand detected:
   - Calculate stroke_id = len(points) // 6 + 1
   - Extract landmarks [0, 4, 8, 12, 16, 20]
   - Convert to pixel coordinates (x, y)
   - Append Point(x, y, stroke_id)
6. Create Template(gesture_name, points)
```

### Real-Time Recognition (gesture_service.py)
```python
1. Capture camera frame
2. Resize frame to (640, 480)
3. Convert BGR to RGB
4. Process with MediaPipe Hands
5. For each hand detected:
   - Calculate stroke_id = len(points) // 6 + 1
   - Extract landmarks [0, 4, 8, 12, 16, 20]
   - Convert to pixel coordinates (x, y)
   - Append Point(x, y, stroke_id)
6. When ready, recognize(points)
```

### GUI Testing (gesture_gui.py)
```python
1. Capture camera frame
2. Resize frame to (640, 480)
3. Convert BGR to RGB
4. Process with MediaPipe Hands
5. For each hand detected:
   - Calculate stroke_id = len(points) // 6 + 1
   - Extract landmarks [0, 4, 8, 12, 16, 20]
   - Convert to pixel coordinates (x, y)
   - Append Point(x, y, stroke_id)
6. When ready, recognize(points)
```

## Verification Checklist

Before building new templates or deploying changes, verify:

- [ ] All files use `max_num_hands=1`
- [ ] All files use `min_detection_confidence=0.6`
- [ ] All files use `min_tracking_confidence=0.6`
- [ ] All files resize to `(640, 480)`
- [ ] All files track landmarks `[0, 4, 8, 12, 16, 20]`
- [ ] All files use `stroke_id = len(points) // 6 + 1`
- [ ] All files append `Point(x, y, stroke_id)` in same order

## Impact of Inconsistency

### Example: Different Stroke IDs
**Template (using frame_count):**
```
Frame 1: Points 0-5 have stroke_id=1
Frame 2: Points 6-11 have stroke_id=2
Frame 3: Points 12-17 have stroke_id=3
```

**Real-time (using len(points)//6+1):**
```
Frame 1: Points 0-5 have stroke_id=1
Frame 2: Points 6-11 have stroke_id=2
Frame 3: Points 12-17 have stroke_id=3
```

✓ These match! (After fix)

**Previous (WRONG):**
```
Template: stroke_id = frame_count (1, 2, 3, ...)
Real-time: stroke_id = len(points)//6+1 (1, 2, 3, ...)
```

Even though the numbers look the same, the CALCULATION must be identical to ensure the same grouping logic.

### Example: Different Resolutions
**Template at 480x320:**
```
Point(240, 160) = center of frame
```

**Real-time at 640x480:**
```
Point(320, 240) = center of frame
```

✗ These don't match! Same gesture, different coordinates.

## Testing Consistency

### Step 1: Rebuild Templates
```bash
cd dollarpy-service
..\.venv\Scripts\python.exe build_templates.py "C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves"
```

### Step 2: Test in GUI
```bash
..\.venv\Scripts\python.exe run_gesture_gui.py
```
- Start camera
- Start real-time detection
- Perform gestures from training videos
- Should get HIGH confidence scores (>0.7)

### Step 3: Test in C# App
- Start gesture service
- Run C# app
- Perform same gestures
- Should get consistent recognition

## Expected Results After Fix

### Before Fix (Inconsistent)
```
Template: rotatefingerright (480x320, frame_count stroke_id)
Real-time: rotatefingerright (640x480, len//6+1 stroke_id)
Score: 0.15-0.30 (LOW - poor match)
```

### After Fix (Consistent)
```
Template: rotatefingerright (640x480, len//6+1 stroke_id)
Real-time: rotatefingerright (640x480, len//6+1 stroke_id)
Score: 0.60-0.90 (HIGH - good match)
```

## Files Modified

1. **gesture_processor.py**
   - Changed resolution from (480, 320) to (640, 480)
   - Changed stroke_id from frame_count to len(points)//6+1
   - Changed MediaPipe config to match real-time

2. **gesture_gui.py**
   - Changed max_num_hands from 2 to 1
   - Changed confidence thresholds from 0.5 to 0.6
   - Already using correct stroke_id calculation

3. **gesture_service.py**
   - Already using correct configuration
   - No changes needed

## Maintenance

When adding new preprocessing steps:

1. Add to ALL three files (processor, service, GUI)
2. Use IDENTICAL parameters
3. Test with known gestures
4. Rebuild templates
5. Verify high confidence scores

## Summary

✅ MediaPipe configuration: CONSISTENT
✅ Frame resolution: CONSISTENT (640x480)
✅ Landmark selection: CONSISTENT ([0,4,8,12,16,20])
✅ Stroke ID calculation: CONSISTENT (len//6+1)
✅ Point collection order: CONSISTENT

**Result:** Templates and real-time recognition now use IDENTICAL preprocessing, ensuring accurate gesture matching.
