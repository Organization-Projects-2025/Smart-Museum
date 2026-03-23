# Quick Start Guide - Gesture Recognition System

## 🚀 Simplified Workflow

All components now automatically use the correct paths and templates!

### 1. Build Templates (One Command!)

**Windows:**
```bash
cd dollarpy-service
rebuild_templates.bat
```

**Linux/Mac:**
```bash
cd dollarpy-service
./rebuild_templates.sh
```

**Or manually:**
```bash
cd dollarpy-service
python build_templates.py
```

✅ No arguments needed - automatically uses: `C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves`
✅ Saves to: `dollarpy-service/gesture_templates.pkl`

### 2. Test with GUI

```bash
cd dollarpy-service
python run_gesture_gui.py
```

✅ Auto-loads templates on startup
✅ Uses correct template file automatically

### 3. Run Gesture Service (for C# integration)

```bash
cd dollarpy-service
python gesture_service.py
```

✅ Uses the same templates as GUI
✅ Ready for C# client connections

## 📁 File Locations

All gesture recognition files are now in `dollarpy-service/`:

- `build_templates.py` - Build templates from videos
- `gesture_templates.pkl` - The actual templates (auto-generated)
- `gesture_gui.py` - GUI for testing
- `gesture_service.py` - Socket service for C# integration
- `gesture_recognizer.py` - Core recognition logic
- `gesture_processor.py` - Video processing

## 🎯 Default Paths

Everything is configured automatically:

- **Video source**: `C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves`
- **Templates file**: `dollarpy-service/gesture_templates.pkl`
- **Service port**: `5000` (configurable in gesture_service.py)

## ✨ What's New?

1. **No more path confusion** - All components use absolute paths
2. **Auto-load templates** - GUI loads templates on startup
3. **Default video folder** - No need to specify path every time
4. **Consistent preprocessing** - Templates and real-time use same settings
5. **Easy rebuild** - One-click batch/shell scripts

## 🔧 Troubleshooting

### "Templates not found"
Run `rebuild_templates.bat` or `python build_templates.py` from `dollarpy-service` folder

### "Old templates being used"
Delete `gesture_templates.pkl` from root folder (if it exists)
The correct file is in `dollarpy-service/gesture_templates.pkl`

### "Low recognition scores"
Rebuild templates to ensure consistency:
```bash
cd dollarpy-service
python build_templates.py
```

## 📊 Expected Results

After rebuilding templates with the fixed preprocessing:

- **Good match**: Score 0.6-0.9 (green in GUI)
- **Okay match**: Score 0.4-0.6 (orange in GUI)
- **Poor match**: Score <0.4 (red in GUI)

Old templates gave scores of 0.15-0.30 even for correct gestures!
