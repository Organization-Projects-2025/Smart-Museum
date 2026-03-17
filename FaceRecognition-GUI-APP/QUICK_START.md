# Quick Start Guide

## 🚀 Choose Your Version

### Option 1: Basic Face Recognition (Fast)
**File:** `main.py`

**Features:**
- Face recognition only
- Auto sign-in/sign-up
- Fast processing (~instant)
- No demographics

**Run:**
```bash
python main.py
```

---

### Option 2: Face Recognition + Demographics (Comprehensive)
**File:** `main_with_demographics.py`

**Features:**
- Face recognition
- Auto sign-in/sign-up
- Age detection
- Gender detection
- Emotion detection
- Race/ethnicity detection
- Visit tracking
- User database (JSON)

**Run:**
```bash
python main_with_demographics.py
```

**⚠️ First Run Note:**
- DeepFace will download AI models (~100-200 MB)
- Takes 2-3 minutes on first run
- Subsequent runs are fast

---

## 📋 System Requirements

- Python 3.11+
- Webcam
- Windows/Linux/Mac
- 4GB RAM minimum
- Internet (first run only for demographics version)

---

## 🎯 Recommended Usage

### For Quick Testing:
```bash
python main.py
```

### For Production (Museum):
```bash
python main_with_demographics.py
```

---

## 🔧 Troubleshooting

### "No module named 'deepface'"
```bash
.venv\Scripts\python.exe -m pip install deepface
```

### Camera not opening
- Check camera is connected
- Close other apps using camera
- Check permissions

### Slow first run (demographics version)
- Normal! DeepFace downloads models
- Wait 2-3 minutes
- Next runs will be fast

---

## 📊 What Gets Collected

### main.py:
- Face image only
- User name (auto: user0, user1, etc.)

### main_with_demographics.py:
- Face image
- User name
- Age (estimated)
- Gender
- Emotion at registration
- Race/ethnicity
- Visit count
- Visit timestamps

**Data stored in:** `user_database.json`

---

## 🗂️ File Locations

```
people/              # Face images
user_database.json   # User demographics (auto-created)
```

---

## ⚡ Quick Commands

```bash
# Run basic version
python main.py

# Run demographics version
python main_with_demographics.py

# View user database
type user_database.json

# Check installed packages
.venv\Scripts\python.exe -m pip list
```

---

## 🎨 Customization

### Change countdown time:
Edit in file:
```python
COUNTDOWN_SECONDS = 3  # Change to 5, 10, etc.
```

### Change recognition timeout:
```python
RECOGNITION_TIMEOUT = 10  # Change to 15, 20, etc.
```

### Change face distance threshold (sensitivity):
```python
FACE_DISTANCE_THRESHOLD = 0.6  # Lower = stricter (0.4-0.7)
```

---

## 📈 Next Steps

1. **Test** with `main.py` first
2. **Upgrade** to `main_with_demographics.py` for analytics
3. **Review** `user_database.json` for insights
4. **Customize** welcome screen colors/text
5. **Deploy** at museum entrance

---

## 🆘 Need Help?

Check documentation:
- `README.md` - Project overview
- `DEMOGRAPHICS_INFO.md` - Demographics details
- `PROJECT_STRUCTURE.md` - File organization
- `ORGANIZATION_SUMMARY.md` - What changed

---

**Ready to start!** 🎉
