# C# Integration Guide

## Overview

This guide shows how to integrate gesture recognition with your C# circular menu application.

## Architecture

```
┌─────────────────┐         Socket (TCP)        ┌──────────────────┐
│   C# App        │ ◄─────────────────────────► │  Python Service  │
│ (Circular Menu) │      localhost:5001         │  (gesture_service)│
└─────────────────┘                             └──────────────────┘
                                                         │
                                                         ▼
                                                  ┌──────────────┐
                                                  │   Camera     │
                                                  │ MediaPipe    │
                                                  │ dollarpy     │
                                                  └──────────────┘
```

## Setup

### 0. Install dependencies (once)

From the **repository root**:

```powershell
powershell -ExecutionPolicy Bypass -File .\install_python_deps.ps1
.\.venv\Scripts\Activate.ps1
```

### 1. Start Python Service

```powershell
python dollarpy-service\gesture_service.py
```

The service will:
- Load gesture templates
- Start camera
- Listen on `localhost:5001`
- Wait for C# client connection

### 2. Add C# Files to Your Project

Copy these files to your C# project:
- `C#/GestureClient.cs` - Client for communicating with Python service
- `C#/CircularMenuGestureExample.cs` - Integration example

### 3. Install NuGet Package

```
Install-Package Newtonsoft.Json
```

## Usage

### Option 1: Continuous Detection (Recommended)

Best for always-on gesture detection:

```csharp
using SmartMuseum;

public class MyCircularMenu
{
    private GestureClient gestureClient;
    
    public async void Initialize()
    {
        // Connect to service
        gestureClient = new GestureClient("localhost", 5001);
        await gestureClient.ConnectAsync();
        
        // Subscribe to gesture events
        gestureClient.GestureRecognized += OnGestureDetected;
        
        // Start tracking
        await gestureClient.StartTrackingAsync();
    }
    
    private void OnGestureDetected(object sender, GestureRecognizedEventArgs e)
    {
        string gesture = e.Result.Gesture;
        double score = e.Result.Score;
        
        switch (gesture)
        {
            case "swipel":
                menu.RotateLeft();
                break;
            case "swiper":
                menu.RotateRight();
                break;
            case "open":
                menu.SelectItem();
                break;
            case "close":
                menu.Close();
                break;
        }
    }
}
```

### Option 2: Manual Detection

Best for user-triggered gestures:

```csharp
// When user starts gesture (e.g., hand enters zone)
await gestureClient.StartTrackingAsync();

// Wait for gesture to complete (2-3 seconds)
await Task.Delay(2000);

// Recognize gesture
var result = await gestureClient.StopAndRecognizeAsync();

if (result.IsValid)
{
    HandleGesture(result.Gesture);
}
```

## Protocol

### Commands (C# → Python)

| Command | Description | Response |
|---------|-------------|----------|
| `START_TRACKING` | Start collecting hand points | `{"status": "ok"}` |
| `STOP_TRACKING` | Stop collecting points | `{"status": "ok", "points": 120}` |
| `RECOGNIZE` | Recognize the gesture | `{"status": "ok", "gesture": "swipel", "score": 0.85}` |
| `RESET` | Clear points and reset | `{"status": "ok"}` |
| `STATUS` | Get service status | `{"status": "ok", "tracking": true, "points": 45}` |
| `PING` | Check if service is alive | `{"status": "ok", "message": "pong"}` |

### Response Format

```json
{
    "status": "ok",
    "gesture": "swipel",
    "score": 0.8523,
    "confidence": "high"
}
```

- `status`: "ok", "error", or "cooldown"
- `gesture`: Detected gesture name (null if none)
- `score`: Confidence score (0.0 - 1.0)
- `confidence`: "high" (>0.7), "medium" (0.5-0.7), "low" (<0.5)

## Gesture Mapping for Circular Menu

| Gesture | Action | Use Case |
|---------|--------|----------|
| Swipe Left | Rotate counter-clockwise | Navigate to previous item |
| Swipe Right | Rotate clockwise | Navigate to next item |
| Open | Select/Expand | Choose current item or open submenu |
| Close | Close/Back | Close menu or go back |

## Tips for Consistent Detection

### 1. Gesture Cooldown
```csharp
// Built-in 1-second cooldown prevents duplicate detections
// Adjust in gesture_service.py if needed:
// self.gesture_cooldown = 1.0  # seconds
```

### 2. Confidence Threshold
```csharp
if (result.Score > 0.7)  // High confidence only
{
    HandleGesture(result.Gesture);
}
```

### 3. Point Collection
```csharp
// Check if enough points collected before recognizing
var status = await gestureClient.GetStatusAsync();
if (status.PointsCollected > 30)  // Minimum 30 points
{
    var result = await gestureClient.StopAndRecognizeAsync();
}
```

### 4. Visual Feedback
```csharp
// Show user when tracking is active
gestureClient.StatusChanged += (s, status) => 
{
    if (status.Contains("Tracking started"))
    {
        ShowTrackingIndicator();  // Visual cue
    }
};
```

### 5. Error Handling
```csharp
try
{
    var result = await gestureClient.StopAndRecognizeAsync();
    if (result.IsValid)
    {
        HandleGesture(result.Gesture);
    }
}
catch (Exception ex)
{
    // Service disconnected - try reconnecting
    await gestureClient.ConnectAsync();
}
```

## Troubleshooting

### Service Not Connecting
```bash
# Check if Python service is running
netstat -an | findstr 5001

# Restart service
python gesture_service.py
```

### Low Recognition Accuracy
- Ensure good lighting
- Perform clear, distinct gestures
- Check camera is working: `python gesture_gui.py`
- Rebuild templates if needed

### Gestures Not Detected
- Check `PointsCollected` in status (need >30 points)
- Increase tracking time (2-3 seconds)
- Verify hand is visible to camera

## Performance

- **Latency**: ~100-200ms from gesture end to recognition
- **FPS**: 30 FPS camera processing
- **CPU**: ~10-15% on modern CPU
- **Memory**: ~200MB for Python service

## Example: Full Integration

See `CircularMenuGestureExample.cs` for complete working example with:
- Automatic connection handling
- Continuous gesture detection
- Circular menu navigation
- Error recovery

---

**Ready to integrate?** Start the Python service and connect your C# app!
