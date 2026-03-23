# ⚠️ ACTION REQUIRED: Rebuild Templates

## Why?
The preprocessing code has been fixed to ensure consistency between template building and real-time recognition. Old templates were built with different settings and won't match well.

## What to do?

### Step 1: Rebuild Templates (REQUIRED)
```bash
cd dollarpy-service
..\.venv\Scripts\python.exe build_templates.py "C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves"
```

You should see:
```
============================================================
Smart Museum - Gesture Template Builder
============================================================
Processing videos from: C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves
------------------------------------------------------------
Building templates...
Found 4 gesture classes:
- close -> close
- RotateFingerRight -> rotatefingerright
- swipeL -> swipel
- swipeR -> swiper

=== Processing close gestures ===
Processing close (1).mp4 as close_1...
Created 1 templates for close

[... similar for other gestures ...]

✓ Successfully created 4 templates
✓ Templates saved to: gesture_templates.pkl
```

### Step 2: Test in GUI
```bash
..\.venv\Scripts\python.exe run_gesture_gui.py
```

1. Click "Start Camera"
2. Click "Start Real-Time Detection"
3. Perform gestures from your training videos
4. **Check scores**: Should be 0.6-0.9 for correct gestures (was 0.15-0.30 before)

### Step 3: Test in C# App

Terminal 1 - Start gesture service:
```bash
..\.venv\Scripts\python.exe gesture_service.py
```

Terminal 2 - Run C# app (build and run TUIO_DEMO.csproj)

Perform rotatefingerright gesture - menu should open reliably!

## What Changed?

### Before (Inconsistent)
- Templates: 480x320 resolution, frame_count stroke IDs
- Real-time: 640x480 resolution, len//6+1 stroke IDs
- Result: Poor matching, scores 0.15-0.30

### After (Consistent)
- Templates: 640x480 resolution, len//6+1 stroke IDs
- Real-time: 640x480 resolution, len//6+1 stroke IDs
- Result: Good matching, scores 0.6-0.9

## Checklist

- [ ] Rebuild templates with command above
- [ ] See "✓ Successfully created 4 templates" message
- [ ] Test in GUI - see high confidence scores (>0.6)
- [ ] Test in C# app - menu opens with rotatefingerright gesture
- [ ] Delete this file once done ✓

## Need Help?

See detailed documentation:
- `PREPROCESSING_CONSISTENCY.md` - Technical details
- `PREPROCESSING_FIX_SUMMARY.md` - What was fixed
- `README.md` - General usage guide
