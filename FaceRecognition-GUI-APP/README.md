# Grand Egyptian Museum - Face Recognition System

A modern face recognition system with automatic sign-in/sign-up functionality for the Grand Egyptian Museum.

## 🎯 Features

- **Automatic Face Recognition**: Recognizes registered users instantly
- **Auto Sign-Up**: New users are automatically registered with a 3-second countdown
- **Real-time Guidance**: Provides live feedback for optimal face positioning
- **High Accuracy**: 99%+ recognition rate using deep learning (face_recognition library)
- **User-Friendly**: Simple, intuitive interface with visual guides
- **Museum Welcome Screen**: Beautiful welcome screen for Grand Egyptian Museum

## 📁 Project Structure

```
FaceRecognition-GUI-APP/
├── main.py                      # Main automatic face recognition app (USE THIS)
├── requirements.txt             # Python dependencies
├── README.md                    # This file
│
├── people/                      # User face images (auto-managed)
│   ├── user0.jpg
│   ├── user1.jpg
│   ├── Ahmed.jpeg
│   └── ...
│
├── new_system/                  # New face_recognition system
│   ├── app-gui.py              # Manual GUI app (alternative)
│   ├── Detector_face_recognition.py
│   └── create_dataset_face_recognition.py
│
├── old_system/                  # Old LBPH system (deprecated)
│   ├── Detector.py
│   ├── create_classifier.py
│   └── ...
│
├── assets/                      # Images and icons
│   ├── homepagepic.png
│   ├── icon.ico
│   └── ...
│
├── docs/                        # Documentation
│   ├── HOW_TO_USE.md
│   ├── UPGRADE_NOTES.md
│   └── MAIN_APP_README.md
│
├── tests/                       # Test files
│   ├── test_face_recognition.py
│   └── face.ipynb
│
└── data/                        # Old training data (not used)
    ├── classifiers/
    └── ...
```

## 🚀 Quick Start

### Prerequisites

**Python Version Required: 3.11.4 (or any Python 3.11.x)**

This application requires Python 3.11 for compatibility with the face_recognition library and its dependencies.

### 1. Setup Virtual Environment

```bash
# Create virtual environment with Python 3.11
python -m venv .venv

# Activate virtual environment (Windows)
.venv\Scripts\activate

# Activate virtual environment (Linux/Mac)
source .venv/bin/activate
```

### 2. Install Dependencies

```bash
# Install requirements
pip install -r requirements.txt
```

### 2. Run the Main Application

```bash
python main.py
```

### 3. Use the System

**For Registered Users:**
1. Position your face in the center oval
2. System recognizes you automatically
3. Welcome screen appears with your name

**For New Users:**
1. Position your face in the center oval
2. System shows "New User - Capturing in 3..."
3. Stay still during countdown
4. Automatically registered as "userN"
5. Welcome screen appears

## 📖 How It Works

### Face Recognition Flow

```
Start Camera
    ↓
Detect Face
    ↓
Check Position (centered, good size, good lighting)
    ↓
Face Good? → NO → Show guidance (move closer, center, etc.)
    ↓ YES
Face Stable for 1 second?
    ↓ YES
Try to Recognize
    ↓
Known User? → YES → Welcome Back!
    ↓ NO
Start 3-second countdown
    ↓
Capture & Save as userN
    ↓
Welcome New User!
```

### User Naming Convention

- **Registered users**: Keep their original names (Ahmed, Youssef, etc.)
- **New users**: Automatically named as `user0`, `user1`, `user2`, etc.
- **Incremental**: Numbers increment automatically

## 🎨 Applications

### 1. Main App (main.py) - RECOMMENDED
**Automatic face recognition with sign-in/sign-up**

Features:
- Automatic user detection
- Real-time positioning guidance
- 3-second countdown for new users
- Grand Egyptian Museum welcome screen
- No manual input required

Use case: Museum entrance, kiosk, automated check-in

### 2. Manual GUI App (new_system/app-gui.py)
**Manual face recognition with admin controls**

Features:
- Add new person (take photo or upload)
- Start recognition manually
- Reload faces
- Admin-friendly interface

Use case: Admin panel, manual registration

## 📊 Technical Details

### Algorithm
- **Face Detection**: HOG (Histogram of Oriented Gradients)
- **Face Recognition**: dlib ResNet-based deep learning
- **Accuracy**: 99.4% on LFW benchmark
- **Speed**: Real-time (15-30 FPS)

### Performance Optimizations
- Processes every other frame (2x speed)
- Downscales to 1/4 resolution for detection (4x speed)
- Pre-loads all faces at startup
- Efficient face encoding comparison

### Requirements
- Python 3.11.4 (or any Python 3.11.x version)
- Webcam
- Windows/Linux/Mac
- 4GB RAM minimum
- CPU (no GPU required)

## 🎯 Best Practices

### For Best Recognition Results

**Lighting:**
- ✅ Good, even lighting
- ✅ Face well-lit
- ❌ Avoid backlighting
- ❌ Avoid harsh shadows

**Position:**
- ✅ Face centered in oval
- ✅ Looking straight at camera
- ✅ 50-100cm from camera
- ❌ Too close or too far
- ❌ Tilted or angled

**Environment:**
- ✅ Clean background
- ✅ One person at a time
- ❌ Multiple people in frame
- ❌ Cluttered background

## 🔧 Configuration

Edit `main.py` to customize:

```python
# Configuration
PEOPLE_DIR = "people"              # User images directory
COUNTDOWN_SECONDS = 3              # Countdown for new users
RECOGNITION_TIMEOUT = 10           # Max time to detect face
FACE_DISTANCE_THRESHOLD = 0.6      # Recognition sensitivity (lower = stricter)
```

## 📝 Keyboard Shortcuts

- **Q**: Quit/Cancel
- **ESC**: Close welcome screen

## 🐛 Troubleshooting

### Camera not opening
- Check camera is connected
- Close other apps using camera
- Check camera permissions

### Face not detected
- Ensure good lighting
- Move closer to camera
- Remove obstructions (hands, hair)

### Not recognizing user
- Check user image exists in `people/` folder
- Ensure image has clear face
- Try re-registering user

### Slow performance
- Normal on older CPUs
- Already optimized
- Close other heavy applications

## 📚 Documentation

- **Main App Guide**: `docs/MAIN_APP_README.md`
- **Manual GUI Guide**: `docs/HOW_TO_USE.md`
- **Upgrade Notes**: `docs/UPGRADE_NOTES.md`

## 🔄 Migration from Old System

The old LBPH system has been moved to `old_system/` folder. To migrate:

1. Old users need to re-register (only 1 photo needed vs 300)
2. Delete old training data in `data/` folder (optional)
3. Use new system for better accuracy (70% → 99%)

## 📦 Dependencies

Main dependencies:
- `face_recognition` - Face recognition library
- `opencv-python` - Computer vision
- `numpy` - Numerical operations
- `Pillow` - Image processing
- `dlib-bin` - Face detection backend

See `requirements.txt` for complete list.

## 🎓 Credits

- face_recognition library by Adam Geitgey
- dlib library by Davis King
- OpenCV for camera and image processing

## 📄 License

This project is for the Grand Egyptian Museum.

## 🤝 Support

For issues or questions:
1. Check documentation in `docs/` folder
2. Review troubleshooting section
3. Check console output for errors

---

**Grand Egyptian Museum - Face Recognition System**
*Powered by Deep Learning • 99%+ Accuracy • Real-time Recognition*
