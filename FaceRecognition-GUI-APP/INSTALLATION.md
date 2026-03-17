# Installation Guide

## ✅ System is Already Installed!

All dependencies are installed in your `.venv` virtual environment.

---

## 🚀 Quick Start

### Run Basic Version (Fast):
```bash
python main.py
```

### Run Demographics Version (With Age, Gender, Emotion):
```bash
python main_with_demographics.py
```

---

## ⚠️ First Run Note (Demographics Version)

**On first run, DeepFace will download AI models:**
- Download size: ~100-200 MB
- Time: 2-3 minutes (one-time only)
- Requires internet connection
- Subsequent runs are fast

**What's happening:**
```
Downloading models...
- Age detection model
- Gender detection model  
- Emotion detection model
- Face detection model
```

**Be patient!** This only happens once.

---

## 📦 Installed Packages

All packages are already installed:

```
✅ face_recognition - Face recognition (99%+ accuracy)
✅ deepface - Age, gender, emotion detection
✅ tensorflow - Deep learning backend
✅ tf-keras - Keras for TensorFlow 2.20
✅ opencv-python - Computer vision
✅ dlib-bin - Face detection
✅ numpy - Numerical operations
✅ pillow - Image processing
✅ And more...
```

---

## 🔧 If You Need to Reinstall

### Full Reinstall:
```bash
# Activate virtual environment
.venv\Scripts\activate

# Install all dependencies
pip install -r requirements.txt
```

### Install Specific Package:
```bash
# Face recognition
pip install face_recognition dlib-bin

# Demographics
pip install deepface tf-keras

# OpenCV
pip install opencv-python opencv-contrib-python
```

---

## 🐛 Troubleshooting

### "No module named 'deepface'"
```bash
.venv\Scripts\python.exe -m pip install deepface
```

### "No module named 'tf_keras'"
```bash
.venv\Scripts\python.exe -m pip install tf-keras
```

### "No module named 'face_recognition'"
```bash
.venv\Scripts\python.exe -m pip install face_recognition dlib-bin cmake
```

### Camera not opening
- Check camera is connected
- Close other apps using camera
- Check Windows camera permissions

### Slow first run (demographics)
- **Normal!** DeepFace downloads models
- Wait 2-3 minutes
- Check internet connection
- Next runs will be fast

---

## 📊 Verify Installation

```bash
# Check Python version
python --version
# Should be: Python 3.11.x

# Check installed packages
.venv\Scripts\python.exe -m pip list

# Test imports
.venv\Scripts\python.exe -c "import face_recognition; import cv2; import deepface; print('All imports OK!')"
```

---

## 💾 Disk Space Requirements

- Virtual environment (.venv): ~500 MB
- DeepFace models (downloaded on first run): ~200 MB
- Total: ~700 MB

---

## 🌐 Internet Requirements

**Required:**
- First run of demographics version (model download)

**Not Required:**
- Basic version (main.py)
- Subsequent runs of demographics version
- Face recognition (works offline)

---

## 🔄 Update Dependencies

```bash
# Update all packages
.venv\Scripts\python.exe -m pip install --upgrade -r requirements.txt

# Update specific package
.venv\Scripts\python.exe -m pip install --upgrade deepface
```

---

## 📝 Dependencies List

See `requirements.txt` for complete list:

```
opencv-python>=4.5.5
opencv-contrib-python>=4.5.5
face_recognition>=1.3.0
deepface>=0.0.99
tensorflow>=2.12.0,<2.21
tf-keras>=2.20.0
dlib-bin>=20.0.0
... and more
```

---

## ✅ System Status

**Current Status:** ✅ Fully Installed

**What's Working:**
- ✅ Face recognition
- ✅ Age detection
- ✅ Gender detection
- ✅ Emotion detection
- ✅ Camera access
- ✅ GUI welcome screen
- ✅ Database storage

**Ready to use!** 🎉

---

## 🆘 Need Help?

1. Check this guide
2. Review `QUICK_START.md`
3. Check `README.md`
4. Review error messages in console

---

**Everything is installed and ready!**  
Just run: `python main_with_demographics.py`
