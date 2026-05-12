# Smart Museum - Implementation Status

**Last Updated**: May 12, 2026  
**Status**: ✅ Core Features Implemented - Frame Management Issues Addressed

---

## Overview

Smart Museum is an interactive museum experience system combining:
- Face ID authentication
- Hand gesture recognition
- Eye gaze and emotion detection
- YOLO object tracking (watches)
- Circular menu navigation with object swipe gestures
- TUIO marker support

### Architecture
**Multi-service TCP-based system** with shared camera frame distribution:
- Single camera capture → CameraHub distributes frames to all services
- 6 independent TCP services on ports 5000-5005
- C# WinForms client (TuioDemo) connects to all services

---

## Service Architecture

### Camera Frame Flow (CRITICAL)
```
Camera (index 0)
    ↓
CameraHub (main.py)
    ├→ Face Auth (port 5000) - Only active during login
    ├→ Gesture Recognition (port 5001) - Only active after login
    ├→ Gaze + Emotion (port 5002) - Active when C# streams
    ├→ YOLO Context (port 5003) - Active when C# streams
    ├→ Hand Pose (port 5004) - Active when C# streams
    └→ Object Tracking (port 5005) - Only active after login (START_TRACKING)
```

### Services (Port 5000-5005)

| Port | Service | Status | Notes |
|------|---------|--------|-------|
| 5000 | Face Authentication | ✅ Working | Exclusive frame access during login |
| 5001 | Gesture Recognition | ⚠️ Needs Deferral | Currently conflicts with object tracking |
| 5002 | Gaze + Emotion | ✅ Working | Streams on demand from C# |
| 5003 | YOLO Context | ✅ Working | Streams on demand from C# |
| 5004 | Hand Pose | ✅ Working | Streams on demand from C# |
| 5005 | Object Tracking | ✅ Working | Waits for START_TRACKING from C# |

---

## Features Implemented

### ✅ Face Authentication (Working)
- **File**: `python/server/auth_service.py`
- **Protocol**: TCP on port 5000
- **Features**:
  - Face lobby (real-time face detection)
  - New face registration with demographics analysis
  - Face matching against stored encodings
  - Uses `face_recognition` library (dlib-based)
  - Exclusive camera access during authentication phase
  
**Status**: Fully functional - face detection works correctly

### ✅ Object Tracking (Working)
- **File**: `python/server/object_tracking_service.py`
- **Port**: 5005
- **Model**: YOLO11s (class 74 = watches)
- **Features**:
  - Real-time object detection
  - Swipe gesture recognition (left, right, up, down)
  - Movement tracking with 60-frame history
  - TCP server for C# communication
  - **Deferred startup**: Only processes frames after C# sends `START_TRACKING`

**Key Implementation**:
```python
_tracking_active = False  # Only process frames after C# command
# Main loop checks: if not _tracking_active: sleep(0.05); continue
```

**Status**: Fully functional - watches detected, swipes recognized

### ✅ Object Tracking Client (C# - Working)
- **File**: `C#/ObjectTrackingClient.cs`
- **Features**:
  - TCP connection to port 5005
  - Async/await pattern
  - Events: `ObjectGestureRecognized`, `ObjectVisibilityChanged`, `StatusChanged`
  - Methods: `ConnectAsync()`, `StartTrackingAsync()`, `StopTrackingAsync()`, `RecognizeGestureAsync()`

**Status**: Fully implemented and working

### ✅ Circular Menu Integration (Working)
- **File**: `C#/CircularMenuController.cs`
- **Features**:
  - Object swipe handlers: `ObjectSwipeRight()`, `ObjectSwipeLeft()`, `ObjectSwipeUp()`, `ObjectSwipeDown()`
  - Routes to menu navigation methods
  - Auto-opens when object detected
  - Auto-closes 5 seconds after object disappears

**Status**: Fully functional

### ✅ Input Prioritization (Working)
- **File**: `C#/InputPrioritizer.cs`
- **Priority Order**:
  1. Objects (highest) - Blocks all gestures
  2. TUIO markers - Blocks hand gestures
  3. Hand gestures (lowest)
- **Features**:
  - 5-second cooldown after object/TUIO clears
  - Thread-safe state management
  - Gesture blocking logic

**Status**: Fully implemented

### ✅ Object Swipe Display (Working)
- **File**: `C#/TuioDemo.cs` - `DrawObjectSwipeOverlay()`
- **Features**:
  - Top-right orange box shows "OBJECT SWIPE RIGHT/LEFT/UP/DOWN DETECTED"
  - 2.5-second fade-out animation
  - Displays on all rendering paths (game, login, analytics)

**Status**: Fully functional

### ✅ Sticky Menu (Working)
- **File**: `C#/TuioDemo.cs` - `HandleObjectVisibilityWithDebounce()`
- **Features**:
  - Menu opens immediately when object detected
  - Stays open for 5 seconds after object disappears
  - Prevents flickering when detection briefly drops
  - Re-detections reset the timer

**Status**: Fully functional

### ⚠️ Hand Gesture Recognition (Frame Conflict Issue)
- **File**: `python/server/gesture_service.py`
- **Issue**: Service runs from startup and competes with object tracking for frames
- **Status**: Needs deferral like object tracking

---

## Known Issues & Solutions Needed

### 1. Hand Gesture Frame Conflict (CURRENT)
**Problem**: Gesture service processes frames continuously, preventing object tracking from getting clean frames after login.

**Current Flow**:
- Login completes
- C# calls `InitializeGestureAndObjectTrackingAfterLogin()`
- Both gesture AND object tracking start
- **Frame contention** → object tracking gets partial/delayed frames

**Solution Needed**:
- Make gesture service defer frame processing like object tracking
- Similar pattern: only start frame loop after explicit command from C#
- Allow gesture client to `START_TRACKING` / `STOP_TRACKING`

### 2. Demographics Warning (Minor)
**Warning**: `[Demographics] Warmup failed (will retry on first use): No module named 'deepface'`

**Impact**: Only affects age/gender/race analysis on new user registration - not critical

**Status**: Installed deepface (may need reload)

---

## Frame Access Timeline (Desired)

```
Startup (Server)
  ├─ Camera Hub: starts capturing
  ├─ Face Auth: ready, listening (NOT processing frames)
  ├─ Gesture: ready, listening (NOT processing frames)
  └─ Object Tracking: ready, listening (NOT processing frames)

User Opens App (C# Client)
  ├─ Face Auth Phase: EXCLUSIVE frame access
  │  └─ Face detection/matching → SUCCESS
  │
  ├─ Login Complete
  │  └─ C# calls InitializeGestureAndObjectTrackingAfterLogin()
  │
  ├─ Gesture Service Phase: processes hand gestures
  │  ├─ C# sends START_TRACKING (gesture)
  │  └─ Hand detection → gesture recognition
  │
  └─ Object Tracking Phase: processes objects
     ├─ C# sends START_TRACKING (object)
     └─ Watch detection → swipe recognition
```

---

## Code Architecture

### C# Main Class: TuioDemo.cs

**Key Members**:
- `objectTrackingClient` - TCP client for object tracking (port 5005)
- `gestureClient` - TCP client for gesture recognition (port 5001)
- `objectStickyCloseTimer` - Keeps menu open 5s after object disappears
- `lastDetectedObjectSwipe` - Tracks last swipe for display
- `objectVisibilityDebounceTimer` - Debounces object visibility changes

**Key Methods**:
- `InitializeGestureAndObjectTrackingAfterLogin()` - Called after face auth succeeds
- `HandleObjectVisibilityWithDebounce()` - Manages menu open/close with debouncing
- `HandleObjectGesture()` - Routes object swipes to menu
- `DrawObjectSwipeOverlay()` - Renders swipe detection feedback

**Events Handled**:
- Login completion → Initialize tracking clients
- Object detected → Open menu, update prioritizer
- Object swipe → Navigate menu, display feedback
- Object lost → Start 5s timer to close menu

### Python Services Structure

**main.py**: Orchestrator
- Creates CameraHub
- Starts all 6 services in threads
- Passes CameraHub to each service

**camera_hub.py**: Frame Distribution
- Single capture thread reading from camera
- Thread-safe frame distribution via `get_frame()`
- Lock-based access to prevent race conditions

**auth_service.py**: Face Authentication
- Face lobby for real-time face detection
- Face registration with encoding
- Demographics analysis (age/gender/race)
- **Modification**: Removed pause/resume calls (doesn't use object tracking)

**object_tracking_service.py**: Object Detection
- **Deferred main loop**: Only processes frames when `_tracking_active == True`
- Waits for `START_TRACKING` command from C#
- YOLO11s model (class 74 = watches)
- Swipe gesture detection via movement tracking
- TCP server responding to C# commands

**gesture_service.py**: Hand Gestures
- MediaPipe hand detection
- Dollarpy gesture recognition
- Per-client state machine
- **Issue**: Runs main loop from startup (should defer like object tracking)

---

## Testing Checklist

- [x] Face authentication working
- [x] Face detection works (faces_found > 0)
- [x] Face encoding and matching works
- [x] New user registration works
- [x] Object tracking detects watches
- [x] Object swipe gestures recognized
- [x] Circular menu opens on object detection
- [x] Menu navigates with object swipes
- [x] Object swipe display shows feedback (top-right orange box)
- [x] Menu doesn't flicker (sticky 5s timer)
- [x] Input prioritizer blocks hand gestures when object present
- [ ] Hand gestures work without conflicting with object tracking
- [ ] Gesture service defers frame processing like object tracking
- [ ] Complete login → hand gesture → object tracking workflow

---

## Environment Variables

```bash
MUSEUM_CAMERA=0           # Camera index (default: 0)
GESTURE_CAMERA=0          # Gesture service camera override
YOLO_CONTEXT_MOCK=0       # Use mock YOLO data (default: real)
DISABLE_GESTURE=0         # Skip gesture service (default: enabled)
```

---

## Deployment Notes

1. **Python Environment**: `.venv/Scripts/python.exe`
2. **Required Packages**: 
   - opencv-python
   - face-recognition (dlib-based)
   - mediapipe
   - ultralytics (YOLO)
   - hsemotion-onnx
   - deepface (for demographics)
   - dollarpy

3. **Models Required**:
   - `yolo11s.pt` - Object tracking
   - `yolov8n.pt` - YOLO context
   - `hand_landmarker.task` - Hand pose
   - `face_landmarker.task` - Gaze/emotion

4. **C# Build**: `dotnet build C#/TUIO_DEMO.csproj -c Debug`

---

## Next Steps

1. **Fix Gesture Frame Conflict** (HIGH PRIORITY)
   - Defer gesture service frame processing
   - Implement `START_TRACKING`/`STOP_TRACKING` pattern
   - Test complete workflow: login → gesture → object tracking

2. **Install/Fix Demographics** (MEDIUM)
   - Verify deepface installation
   - Test new user registration with demographics

3. **Comprehensive Testing** (HIGH)
   - Full login workflow
   - Hand gesture detection after login
   - Object tracking after gesture initialized
   - No frame conflicts

4. **Performance Optimization** (LOW)
   - Profile frame processing
   - Optimize gesture/object detection inference

---

## Contact & Documentation

- Face Recognition: `face_recognition` library (dlib)
- YOLO: Ultralytics YOLOv8/v11
- Gestures: Dollarpy gesture recognizer
- Hand: MediaPipe hand tracking
- Gaze: MediaPipe face landmarks + manual geometry
- Emotion: HSEmotion (ONNX)

**Last Working State**: Face auth + Object tracking working; Hand gestures need frame deferral
