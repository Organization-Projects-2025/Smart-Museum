# Migration Guide: Old → Refactored Gesture Service

## Summary

Your old gesture service will not work with the new pipeline. **You must rebuild templates** using the new `gesture_preprocessing.py` module.

This is expected and necessary because:
- Old templates used frame-based stroke IDs
- New templates use index-tip-path with stroke_id=0
- Different preprocessing = different template format
- Old templates will not match new recognition code

---

## Migration Checklist

- [ ] **Record NEW training videos** (or use old ones with new builder)
- [ ] **Build templates**: `python rebuild_templates.py`
- [ ] **Evaluate accuracy**: `python evaluate_gestures.py`
- [ ] **Replace service**: Use `gesture_service_refactored.py` instead of `gesture_service.py`
- [ ] **Update config**: Adjust `gesture_config.py` as needed
- [ ] **Tune thresholds**: Based on evaluation results
- [ ] **Test with C#**: Verify integration works

---

## Step-by-Step Migration

### 1. Archive Old System (Optional)

```bash
# Backup old code
cp gesture_service.py gesture_service_old.py
cp gesture_processor.py gesture_processor_old.py
cp gesture_recognizer.py gesture_recognizer_old.py
cp gesture_templates.pkl gesture_templates_old.pkl
```

### 2. Organize Training Videos

Create training/test split:

```bash
# Create directories
mkdir -p gesture_videos/swipe_left
mkdir -p gesture_videos/swipe_right
mkdir -p gesture_videos/circle
mkdir -p gesture_videos_test/swipe_left_test
mkdir -p gesture_videos_test/swipe_right_test
mkdir -p gesture_videos_test/circle_test

# Move or record videos
# Training: 5-10 examples per gesture
# Test: 5-10 separate examples per gesture
```

### 3. Build Templates

```bash
cd dollarpy-service

# Build templates from training videos
python rebuild_templates.py

# Or with specific paths
python rebuild_templates.py --input ./gesture_videos --output ./gesture_templates.pkl
```

This creates: `gesture_templates.pkl`

### 4. Evaluate on Test Set

```bash
# Run evaluation
python evaluate_gestures.py

# Output shows:
# - Per-gesture accuracy
# - Confusion matrix
# - Detailed results
```

Example output:
```
Gesture            Accuracy    Tests
swipe_left         92.0%       15
swipe_right        88.0%       15
circle             85.0%       12
-----
OVERALL            88.3%       42
```

If accuracy < 80%:
- Check template quality (good lighting, clear gestures)
- Collect more training examples
- Try different normalization mode
- Check that test videos are truly separate from training

### 5. Update Your Service

**Old approach:**
```bash
python gesture_service.py
```

**New approach:**
```bash
python gesture_service_refactored.py
```

The new service:
- Uses shared preprocessing (consistent)
- Fixed 25-frame window (reliable)
- High confidence threshold 0.60 (no false positives)
- Stability checking (confirm gesture N times)
- Debug logging available

### 6. Verify C# Integration

Your C# client code should work unchanged:
```csharp
socket.Send("START_TRACKING\n");
socket.Send("RECOGNIZE\n");
// Receives: {"status": "ok", "gesture": "swipe_left", "score": 0.75, ...}
```

Response format is compatible.

---

## Configuration Migration

### Old `gesture_service.py`:
```python
gesture_cooldown = 1.0
motion_start_threshold = 0.035
no_hand_reset_frames = 45

if score > 0.08:  # BAD: too low!
    return gesture
```

### New `gesture_config.py`:
```python
COOLDOWN_SECONDS = 0.8
GESTURE_MOTION_START_THRESHOLD = 0.035  # Not used in fixed-window mode
GESTURE_NO_HAND_RESET_FRAMES = 45       # Not used in fixed-window mode
CONFIDENCE_THRESHOLD = 0.60             # GOOD: meaningful threshold

if score > CONFIDENCE_THRESHOLD:
    return gesture
```

All constants are **environment-variable configurable**:
```bash
GESTURE_WINDOW_FRAMES=30 GESTURE_CONFIDENCE_THRESHOLD=0.70 python gesture_service_refactored.py
```

---

## Troubleshooting Migration

### "Templates not found" error
```
Error: Templates file not found: gesture_templates.pkl
```
**Solution:** Run `python rebuild_templates.py` first

### Recognition very poor
```
All gestures predicted as "unknown" with score < 0.60
```
**Solutions:**
1. Check template quality: `DEBUG_GESTURES=1 python rebuild_templates.py`
2. Check frame capture: Print debug output from recognition
3. Try different normalization: `GESTURE_NORMALIZATION_MODE=wrist_scale python ...`
4. Lower threshold temporarily: `GESTURE_CONFIDENCE_THRESHOLD=0.40 python ...`

### "Preprocessing failed" in service
```
Error: Preprocessing failed (not enough points, insufficient motion)
```
**Solutions:**
1. Ensure hand is visible during gesture
2. Make gesture larger/faster
3. Check MIN_MOTION_DISTANCE not too high: `GESTURE_MIN_MOTION=0.03 python ...`

### Accuracy still low after migration
1. Record more training examples (10-15 per gesture, not 3-5)
2. Ensure good lighting
3. Ensure camera is stable
4. Try different camera distance/angle
5. Use `DEBUG_GESTURES=1` to inspect what points are extracted

---

## Comparing Old vs New

| Aspect | Old | New |
|--------|-----|-----|
| **Stroke ID** | Frame-based (1, 2, 3, ...) | Gesture-based (0) for index_tip_path |
| **Points per frame** | 6 (all landmarks) | 1 (index fingertip) for index_tip_path |
| **Preprocessing** | Duplicated in 2 places | **Unified in gesture_preprocessing.py** |
| **Threshold** | 0.08 ❌ | 0.60 ✓ |
| **Motion detection** | Complex state machine | Simple 25-frame window ✓ |
| **Recognition** | On every frame | On window completion |
| **Deep copy** | Every frame ❌ | Per window ✓ |
| **Stability check** | None | Require N consecutive ✓ |
| **Configuration** | Hardcoded | gesture_config.py ✓ |
| **Evaluation** | None | evaluate_gestures.py ✓ |

---

## Expected Performance Improvement

### Before Refactoring
- Accuracy: ~40-60% (low threshold accepts too much noise)
- Threshold: 0.08 (too permissive)
- Motion detection: Unreliable
- False positives: High

### After Refactoring
- Accuracy: 80-95% (on test set)
- Threshold: 0.60 (meaningful)
- Motion detection: Removed (fixed window simpler)
- False positives: Rare (stability checking)

**Caveat:** Your actual improvement depends on:
- Quality of training/test videos
- Gesture clarity and distinctiveness
- Whether templates match live camera setup

---

## Rollback (If Needed)

If new system doesn't work:

```bash
# Revert to old code
cp gesture_service_old.py gesture_service.py
cp gesture_recognizer_old.py gesture_recognizer.py
cp gesture_templates_old.pkl gesture_templates.pkl

python gesture_service.py
```

But recommend fixing issues instead of reverting.

---

## Questions?

Refer to:
- **REFACTORING_GUIDE.md** - Complete refactoring documentation
- **gesture_config.py** - Configuration options with comments
- **gesture_preprocessing.py** - Preprocessing logic and modes
- **evaluate_gestures.py** - Accuracy evaluation script
- **gesture_service_refactored.py** - New service implementation
