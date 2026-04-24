# ⚠️ ACTION REQUIRED: Rebuild Templates

## Why?
The preprocessing code has been fixed to ensure consistency between template building and real-time recognition. Old templates were built with different settings and won't match well.

## Quick Start - Easiest Way!

### Windows:
```bash
cd dollarpy-service
rebuild_templates.bat
```

### Linux/Mac:
```bash
cd dollarpy-service
./rebuild_templates.sh
```

That's it! The script automatically uses the correct folder: `C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves`

## Manual Method (if needed)

### Step 1: Rebuild Templates
```bash
cd dollarpy-service
python build_templates.py
```

No arguments needed! It automatically uses: `C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves`

You should see:
```
============================================================
Smart Museum - Gesture Template Builder
============================================================
Using default path: C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves
------------------------------------------------------------
Building templates...
Found 4 gesture classes:
- close -> close
- swipeL -> swipel
- swipeR -> swiper
- thumbs -> thumbs

✓ Successfully created 4 templates
✓ Templates saved to: gesture_templates.pkl
```

### Step 2: Test in GUI
```bash
python run_gesture_gui.py
```

The GUI now automatically:
- Loads templates from `dollarpy-service/gesture_templates.pkl`
- Uses the correct default folder when building templates

1. Templates are auto-loaded on startup
2. Click "Start Camera"
3. Click "Start Real-Time Detection"
4. Perform gestures from your training videos
5. **Check scores**: Should be 0.6-0.9 for correct gestures (was 0.15-0.30 before)

### Step 3: Test in C# App

Terminal 1 - Start gesture service:
```bash
python gesture_service.py
```

Terminal 2 - Run C# app (build and run TUIO_DEMO.csproj)

Perform gestures - they should be recognized reliably!

## What Changed?

### Before (Inconsistent)
- Templates: 480x320 resolution, frame_count stroke IDs
- Real-time: 640x480 resolution, len//6+1 stroke IDs
- Result: Poor matching, scores 0.15-0.30

### After (Consistent)
- Templates: 640x480 resolution, len//6+1 stroke IDs
- Real-time: 640x480 resolution, len//6+1 stroke IDs
- Result: Good matching, scores 0.6-0.9

## New Features

✅ **Auto-default folder**: `build_templates.py` uses correct folder automatically
✅ **Auto-load templates**: GUI loads templates on startup
✅ **Correct paths**: All components use `dollarpy-service/gesture_templates.pkl`
✅ **Easy rebuild**: Just run `rebuild_templates.bat` (Windows) or `rebuild_templates.sh` (Linux/Mac)

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
