# Smart Museum - Gesture Recognition Service

Dynamic hand gesture recognition using $1 recognizer (dollarpy) and MediaPipe.

## Quick Start

```bash
# 1. Install dependencies
cd Smart-Museum/dollarpy-service
pip install -r requirements.txt

# 2. Run the GUI
python run_gesture_gui.py
```

Templates are automatically loaded on startup.

## Supported Gestures

Automatically detects all gesture classes from video folder names in `../Public/Data/Videos/Moves/`

Current gestures are determined by the folders present.

## Files

- `gesture_gui.py` - GUI application for testing
- `gesture_recognizer.py` - Recognition engine
- `gesture_processor.py` - Video processing for template building
- `build_templates.py` - Rebuild templates from videos
- `run_gesture_gui.py` - Quick launcher
- `gesture_templates.pkl` - Pre-built gesture templates
- `requirements.txt` - Python dependencies

## Rebuild Templates

If you add new gesture videos to `../Public/Data/Videos/Moves/`:

```bash
python build_templates.py "C:\Projects\Museum\Smart-Museum\Public\Data\Videos\Moves"
```

## How It Works

1. **Multi-point tracking**: Tracks 6 hand landmarks (wrist, thumb, index, middle, ring, pinky tips)
2. **Template matching**: Uses $1 recognizer algorithm to match gestures
3. **Real-time recognition**: Processes camera feed at 30+ FPS

## Usage in Code

```python
from gesture_recognizer import SmartMuseumGestureRecognizer
from dollarpy import Point

# Initialize
recognizer = SmartMuseumGestureRecognizer()
recognizer.load_templates()

# Collect points during gesture (6 points per frame)
points = []  # List of Point(x, y, stroke_id)

# Recognize
gesture_name, score = recognizer.recognize(points)
if score > 0.6:
    print(f"Detected: {gesture_name}")
```

## Requirements

- Python 3.8+
- Webcam
- Good lighting for hand detection

## Troubleshooting

**Low recognition scores:**
- Ensure good lighting
- Perform clear, distinct gestures
- Keep hand fully visible

**Camera not opening:**
- Check camera permissions
- Try different camera: change `cv2.VideoCapture(0)` to `cv2.VideoCapture(1)`

---

Part of the Smart Museum project.
