# Face Recognition System - User Guide

## Overview

This is a modern face recognition system using deep learning (face_recognition library) with 99%+ accuracy.

## Features

✅ **Automatic face loading** - All faces loaded at startup from `people/` folder
✅ **Dynamic recognition** - Recognizes ANY known person automatically
✅ **Easy to add people** - Take photo OR upload existing photo
✅ **High accuracy** - 99.4% recognition rate (vs 70-85% with old LBPH)
✅ **Simple workflow** - Only 1 clear photo needed per person

## How to Use

#
## Starting the App

```bash
.venv\Scripts\python.exe app-gui.py
```

The app will:
1. Load all faces from `people/` folder
2. Show how many people were loaded
3. Open the GUI

## Main Menu

### ➕ Add New Person
Add a new person to the recognition system

**Steps:**
1. Click "Add New Person"
2. Enter the person's name
3. Choose one option:
   - **📷 Take Photo**: Use webcam to capture
     - Position face in frame
     - Press SPACE when face is detected
     - Press Q to cancel
   - **📁 Upload Photo**: Select existing photo
     - Choose a clear photo with visible face
     - System will verify face is detected

### 🎥 Start Recognition
Start recognizing faces from webcam

**Steps:**
1. Click "Start Recognition"
2. See list of people who will be recognized
3. Click "Start Recognition" button
4. Camera opens and recognizes ALL known faces
5. Press Q to quit early (or wait 30 seconds)

**What you'll see:**
- Green box + name = Person recognized ✅
- Red box + "Unknown" = Face not in system ❌

### 🔄 Reload Faces
Reload all faces from `people/` folder

**When to use:**
- After manually adding/removing photos in `people/` folder
- After external changes to the folder

## People Folder

Location: `c:\Projects\Museum\FaceRecognition-GUI-APP\people\`

**Structure:**
```
people/
├── Ahmed.jpeg          → Person name: "Ahmed"
├── Ayman Ezzat.jpg     → Person name: "Ayman Ezzat"
├── Mazen.jpg           → Person name: "Mazen"
└── Mohnad.jpeg         → Person name: "Mohnad"
```

**Rules:**
- Filename (without extension) = Person's name
- Supported formats: .jpg, .jpeg, .png
- One photo per person
- Photo must have clear, visible face

## Tips for Best Results

### Photo Quality
✅ **Good:**
- Clear, well-lit face
- Looking at camera
- No obstructions
- High resolution

❌ **Bad:**
- Blurry or dark
- Face turned away
- Sunglasses/mask covering face
- Very low resolution

### Recognition Tips
- Ensure good lighting when using webcam
- Face the camera directly
- Remove obstructions (hands, hair covering face)
- Stay within 1-2 meters of camera

## Troubleshooting

### "No faces loaded"
**Problem:** No people in the system
**Solution:** Add people using "Add New Person"

### "No face detected in image"
**Problem:** Photo doesn't have clear face
**Solution:** 
- Use a clearer photo
- Ensure face is visible and not obstructed
- Try different photo

### "Could not open camera"
**Problem:** Camera not accessible
**Solution:**
- Check camera is connected
- Close other apps using camera
- Check camera permissions
- Try unplugging/replugging camera

### Recognition not working
**Problem:** Person not being recognized
**Solution:**
- Ensure person is in `people/` folder
- Click "Reload Faces" button
- Check photo quality
- Try re-adding the person with better photo

### Slow performance
**Problem:** Recognition is slow
**Solution:**
- Normal on older CPUs
- Already optimized (processes 1/4 frames)
- Close other heavy applications

## Technical Details

### Algorithm
- **Face Detection**: HOG (Histogram of Oriented Gradients)
- **Face Recognition**: dlib ResNet-based deep learning
- **Accuracy**: 99.4% on standard benchmarks
- **Speed**: ~15-30 FPS on modern CPU

### Performance Optimizations
- Faces loaded once at startup (not every recognition)
- Processes every other frame (2x speed)
- Downscales to 1/4 resolution for detection (4x speed)
- Displays at full resolution

### Data Storage
- Photos: `people/` folder
- No database needed
- No training files needed
- Just image files

## Comparison with Old System

| Feature | Old (LBPH) | New (face_recognition) |
|---------|------------|------------------------|
| Accuracy | 70-85% | **99.4%** |
| Photos needed | 300 per person | **1 per person** |
| Training | Required (30-60s) | **None** |
| Setup time | ~2 minutes | **Instant** |
| Lighting tolerance | Poor | **Excellent** |
| Angle tolerance | Poor | **Good** |
| Works with glasses | Poor | **Good** |

## Keyboard Shortcuts

- **SPACE**: Capture photo (when taking photo)
- **Q**: Quit/Cancel
- **ESC**: Cancel (when taking photo)

## Support

For issues or questions:
1. Check this guide
2. Check console output for error messages
3. Verify `people/` folder structure
4. Try "Reload Faces" button

## Credits

- face_recognition library by Adam Geitgey
- dlib library by Davis King
- OpenCV for camera and image processing
