# Python Dependencies Installation Guide

## 🚀 Quick Installation

### **Option 1: PowerShell Script (Recommended for Windows)**
```powershell
powershell -ExecutionPolicy Bypass -File .\install_python_deps.ps1
```

### **Option 2: Manual Installation**
```bash
# Create virtual environment
python -m venv .venv

# Activate virtual environment
# Windows:
.venv\Scripts\activate
# Linux/Mac:
source .venv/bin/activate

# Upgrade pip
python -m pip install -U pip

# Install dependencies
python -m pip install -r requirements.txt

# Install face_recognition separately (Windows requirement)
python -m pip install "face_recognition>=1.3.0" --no-deps
```

### **Option 3: Using Conda (Alternative)**
```bash
# Create conda environment
conda create -n smart-museum python=3.10

# Activate environment
conda activate smart-museum

# Install dependencies
pip install -r requirements.txt

# Install face_recognition separately
pip install "face_recognition>=1.3.0" --no-deps
```

## 📋 What Gets Installed

### **Core Dependencies**
- `numpy` - Numerical computing
- `opencv-python` - Computer vision
- `Click` - Command-line interface

### **Face Recognition (Port 5000)**
- `face-recognition-models` - Pre-trained models
- `dlib-bin` - Machine learning toolkit (Windows)
- `Pillow` - Image processing
- `pybluez2` - Bluetooth communication

### **Hand Tracking & Gesture (Ports 5001, 5004)**
- `mediapipe` - Hand tracking and pose estimation
- `dollarpy` - Gesture recognition

### **YOLO Object Detection (Port 5003)**
- `ultralytics` - YOLO implementation
- `torch` - Deep learning framework (auto-installed)
- `torchvision` - Computer vision for PyTorch (auto-installed)

## 🔧 Service-Specific Requirements

| Service | Port | Dependencies | Can Disable? |
|---------|------|--------------|--------------|
| Face Auth + Bluetooth | 5000 | face-recognition, dlib, opencv, pybluez2 | Yes |
| Gesture Recognition | 5001 | mediapipe, dollarpy, opencv | Yes |
| Gaze + Emotion | 5002 | mediapipe, opencv | Yes |
| YOLO Context | 5003 | ultralytics, torch, opencv | Yes |
| Hand Tracking | 5004 | mediapipe, opencv | Yes |

## ⚡ Minimal Installation (For Testing)

If you want to test without heavy dependencies:

```bash
# Install only core dependencies
pip install numpy opencv-python mediapipe

# Disable heavy services
export DISABLE_YOLO_CONTEXT=1
export DISABLE_FACE_AUTH=1
```

## 🎯 GPU Acceleration (Optional)

For better YOLO performance:

```bash
# NVIDIA GPU
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu118

# AMD GPU
pip install torch torchvision --index-url https://download.pytorch.org/whl/rocm5.7
```

## 🐛 Troubleshooting

### **Issue: dlib installation failed**
```bash
# Windows: Use dlib-bin (already in requirements.txt)
# Linux: Install system dependencies
sudo apt-get install build-essential cmake

# Or use conda
conda install -c conda-forge dlib
```

### **Issue: pybluez2 not available on Windows**
```bash
# Skip Bluetooth features
# The app will work without Bluetooth authentication
```

### **Issue: ultralytics too slow**
```bash
# Use mock mode (no GPU needed)
export YOLO_CONTEXT_MOCK=1
```

### **Issue: mediapipe not found**
```bash
# Ensure Python 3.8+ and reinstall
pip install --upgrade mediapipe
```

### **Issue: opencv-python import error**
```bash
# Try headless version for server environments
pip uninstall opencv-python
pip install opencv-python-headless
```

## 🧪 Testing Installation

After installation, test that all services work:

```bash
# Start unified server
python python/server/unified_museum_server.py

# In another terminal, test services
python python/server/test_unified_server.py
```

Expected output:
```
✓ All services are running correctly!
```

## 📊 Platform Compatibility

| Platform | Status | Notes |
|----------|--------|-------|
| Windows 10/11 | ✅ Full | Use PowerShell script |
| Linux (Ubuntu) | ✅ Full | May need system dependencies |
| macOS | ✅ Full | May need Xcode tools |
| Python 3.8+ | ✅ Required | 3.10 recommended |

## 🔍 Version Requirements

- **Python**: 3.8 or higher (3.10 recommended)
- **pip**: Latest version
- **OS**: Windows 10+, Ubuntu 20.04+, macOS 10.15+

## 📝 Post-Installation Configuration

After installing dependencies, configure the unified server:

```bash
# Set camera index (if not using default camera 0)
export MUSEUM_CAMERA=1

# Disable specific services (if not needed)
export DISABLE_HAND_TRACK=1
export DISABLE_GESTURE=1

# Use mock YOLO data (faster, no GPU needed)
export YOLO_CONTEXT_MOCK=1
```

## 🎉 Next Steps

1. **Install dependencies** using one of the methods above
2. **Test installation** with the test script
3. **Start the unified server**: `python python/server/unified_museum_server.py`
4. **Run the C# app** from Visual Studio or command line

## 📚 Additional Resources

- [Unified Server Documentation](UNIFIED_SERVER_README.md)
- [Quick Start Guide](QUICK_START.md)
- [Implementation Summary](IMPLEMENTATION_SUMMARY.md)

---

**Need help?** Check the main [README.md](README.md) or open an issue on GitHub.