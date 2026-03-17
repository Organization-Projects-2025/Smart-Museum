# Project Structure

## 📁 Organized Folder Structure

```
FaceRecognition-GUI-APP/
│
├── 📄 main.py                          ⭐ MAIN APPLICATION (USE THIS)
├── 📄 requirements.txt                 Python dependencies
├── 📄 README.md                        Project overview
├── 📄 PROJECT_STRUCTURE.md            This file
│
├── 📁 people/                          👥 User Face Images (Auto-managed)
│   ├── user0.jpg                      Auto-registered users
│   ├── user1.jpg
│   ├── Ahmed.jpeg                     Pre-registered users
│   ├── Ayman Ezzat.jpg
│   ├── Mazen.jpg
│   ├── Mohnad.jpeg
│   └── Youssef.jpeg
│
├── 📁 new_system/                      🆕 New Face Recognition System
│   ├── app-gui.py                     Manual GUI application
│   ├── Detector_face_recognition.py   Recognition engine
│   └── create_dataset_face_recognition.py  Dataset creation
│
├── 📁 old_system/                      🗄️ Old LBPH System (Deprecated)
│   ├── Detector.py                    Old detector (70-85% accuracy)
│   ├── create_classifier.py           Old training
│   ├── create_dataset.py              Old dataset creation
│   ├── demo.py                        Old demo
│   ├── predict.py                     Old prediction
│   ├── gender_prediction.py           Gender/age prediction
│   └── nameslist.txt                  Old user list
│
├── 📁 assets/                          🎨 Images & Icons
│   ├── homepagepic.png               Homepage image
│   ├── icon.ico                       Application icon
│   ├── 2.png                          Asset image
│   └── end.png                        End screen image
│
├── 📁 docs/                            📚 Documentation
│   ├── HOW_TO_USE.md                 Manual GUI guide
│   ├── UPGRADE_NOTES.md              System upgrade notes
│   └── MAIN_APP_README.md            Main app documentation
│
├── 📁 tests/                           🧪 Test Files
│   ├── test_face_recognition.py      Test script
│   └── face.ipynb                    Jupyter notebook tests
│
├── 📁 data/                            💾 Old Training Data (Not Used)
│   ├── classifiers/                   Old LBPH classifiers
│   │   ├── ab_classifier.xml
│   │   ├── ngoc_classifier.xml
│   │   ├── tho_classifier.xml
│   │   └── tho1_classifier.xml
│   ├── ab/                            Old training images (300 each)
│   ├── ngoc/
│   ├── tho/
│   ├── tho1/
│   └── youssef/
│
├── 📁 .venv/                           🐍 Virtual Environment
│   └── (Python packages)
│
└── 📁 .git/                            📦 Git Repository
    └── (Version control)
```

## 🎯 File Purpose Guide

### ⭐ Main Files (Use These)

| File | Purpose | When to Use |
|------|---------|-------------|
| `main.py` | Automatic face recognition | Museum entrance, kiosk, auto check-in |
| `new_system/app-gui.py` | Manual GUI app | Admin panel, manual registration |

### 📚 Documentation Files

| File | Content |
|------|---------|
| `README.md` | Project overview and quick start |
| `PROJECT_STRUCTURE.md` | This file - folder organization |
| `docs/MAIN_APP_README.md` | Detailed main app guide |
| `docs/HOW_TO_USE.md` | Manual GUI app guide |
| `docs/UPGRADE_NOTES.md` | LBPH → face_recognition upgrade notes |

### 🗂️ Data Folders

| Folder | Purpose | Status |
|--------|---------|--------|
| `people/` | Active user face images | ✅ Active |
| `data/` | Old LBPH training data | ⚠️ Deprecated |
| `old_system/` | Old LBPH system files | ⚠️ Deprecated |

### 🎨 Asset Folders

| Folder | Content |
|--------|---------|
| `assets/` | Images, icons, UI assets |
| `tests/` | Test scripts and notebooks |

## 🔄 System Comparison

### Old System (old_system/)
- **Algorithm**: LBPH (Local Binary Patterns Histograms)
- **Accuracy**: 70-85%
- **Training**: Required (300 images per person)
- **Status**: Deprecated, kept for reference

### New System (new_system/ + main.py)
- **Algorithm**: dlib ResNet deep learning
- **Accuracy**: 99%+
- **Training**: Not required (1 image per person)
- **Status**: Active, recommended

## 📊 Folder Sizes (Approximate)

```
people/          ~5 MB    (5 users × 1 image each)
data/            ~500 MB  (Old training data - can be deleted)
old_system/      ~1 MB    (Old code - can be deleted)
new_system/      ~100 KB  (Active code)
assets/          ~2 MB    (Images and icons)
.venv/           ~500 MB  (Python packages)
```

## 🧹 Cleanup Recommendations

### Safe to Delete (if not needed)

1. **Old training data**: `data/` folder (~500 MB)
   - Old LBPH training images
   - Old classifier files
   - Not used by new system

2. **Old system code**: `old_system/` folder (~1 MB)
   - Deprecated LBPH code
   - Kept for reference only

3. **Test files**: `tests/` folder (optional)
   - Test scripts
   - Jupyter notebooks

### Keep These

1. **people/**: Active user images
2. **new_system/**: Active code
3. **main.py**: Main application
4. **assets/**: UI assets
5. **docs/**: Documentation
6. **.venv/**: Python environment
7. **requirements.txt**: Dependencies

## 🚀 Quick Navigation

### To run main app:
```bash
python main.py
```

### To run manual GUI:
```bash
python new_system/app-gui.py
```

### To view documentation:
- Main app: `docs/MAIN_APP_README.md`
- Manual GUI: `docs/HOW_TO_USE.md`

### To add new user manually:
1. Add image to `people/` folder
2. Name it: `username.jpg`
3. Restart app or click "Reload Faces"

## 📝 Notes

- All user images are stored in `people/` folder
- New users are auto-named as `user0`, `user1`, etc.
- Old system files kept for reference but not used
- Main app (`main.py`) is the recommended entry point
