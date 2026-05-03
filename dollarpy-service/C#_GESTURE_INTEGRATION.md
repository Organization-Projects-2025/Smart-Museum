# C# Gesture Service Integration Guide

## Quick Start

### 1. Start the Service
```bash
cd dollarpy-service
python gesture_service.py
```

### 2. Connect from C#
```csharp
TcpClient client = new TcpClient("127.0.0.1", 5001);
NetworkStream stream = client.GetStream();
```

### 3. Start Tracking
```csharp
SendCommand(stream, "START_TRACKING");
// Response: {"status": "ok", "message": "Tracking started"}
```

### 4. Poll for Gestures
```csharp
while (true)
{
    Thread.Sleep(100); // Poll every 100ms
    
    var status = SendCommand(stream, "STATUS");
    
    if (!status.in_cooldown && status.last_gesture != null)
    {
        // New gesture detected!
        HandleGesture(status.last_gesture);
    }
    
    if (status.in_cooldown)
    {
        Console.WriteLine($"Cooldown: {status.cooldown_remaining}s");
    }
}
```

## How It Works

### Continuous Detection Flow

```
1. START_TRACKING
   ↓
2. Service continuously monitors camera (60 FPS)
   ↓
3. Sliding window maintains last 60 frames
   ↓
4. Recognition runs every 50ms
   ↓
5. When confidence > 0.4:
   - Gesture TRIGGERED
   - Buffer cleared
   - 3-second cooldown starts
   ↓
6. C# polls STATUS/RECOGNIZE to get gesture
   ↓
7. After 3 seconds, cooldown ends
   ↓
8. Service resumes listening
```

## Commands

### START_TRACKING
Start gesture detection.

**Request:**
```
START_TRACKING
```

**Response:**
```json
{
    "status": "ok",
    "message": "Tracking started"
}
```

### STOP_TRACKING
Stop gesture detection and release camera.

**Request:**
```
STOP_TRACKING
```

**Response:**
```json
{
    "status": "ok",
    "message": "Tracking stopped",
    "frames": 45
}
```

### STATUS
Get current tracking status.

**Request:**
```
STATUS
```

**Response (Active):**
```json
{
    "status": "ok",
    "tracking": true,
    "frames": 60,
    "templates": 8,
    "last_gesture": "swipe_right",
    "sliding_window": true,
    "window_size": "60/60",
    "in_cooldown": false,
    "cooldown_remaining": 0.0
}
```

**Response (Cooldown):**
```json
{
    "status": "ok",
    "tracking": true,
    "frames": 35,
    "templates": 8,
    "last_gesture": "swipe_right",
    "sliding_window": false,
    "window_size": "35/60",
    "in_cooldown": true,
    "cooldown_remaining": 2.3
}
```

### RECOGNIZE
Get last detected gesture (if any).

**Request:**
```
RECOGNIZE
```

**Response (Gesture Available):**
```json
{
    "status": "ok",
    "gesture": "swipe_right",
    "score": 1.0,
    "confidence": "high",
    "message": "Last gesture: swipe_right"
}
```

**Response (Cooldown):**
```json
{
    "status": "cooldown",
    "gesture": null,
    "score": 0.0,
    "confidence": "cooldown",
    "cooldown_remaining": 2.3,
    "message": "Cooldown active (2.3s remaining)"
}
```

**Response (No Gesture):**
```json
{
    "status": "ok",
    "gesture": null,
    "score": 0.0,
    "confidence": "none",
    "message": "No gesture detected yet"
}
```

### RESET
Reset all state and stop tracking.

**Request:**
```
RESET
```

**Response:**
```json
{
    "status": "ok",
    "message": "Reset complete"
}
```

### PING
Test connection.

**Request:**
```
PING
```

**Response:**
```json
{
    "status": "ok",
    "message": "pong"
}
```

## C# Implementation Example

```csharp
using System;
using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;

public class GestureClient
{
    private TcpClient client;
    private NetworkStream stream;
    
    public void Connect()
    {
        client = new TcpClient("127.0.0.1", 5001);
        stream = client.GetStream();
        Console.WriteLine("Connected to gesture service");
    }
    
    public dynamic SendCommand(string command)
    {
        // Send command
        byte[] data = Encoding.UTF8.GetBytes(command + "\n");
        stream.Write(data, 0, data.Length);
        
        // Read response
        byte[] buffer = new byte[4096];
        int bytes = stream.Read(buffer, 0, buffer.Length);
        string response = Encoding.UTF8.GetString(buffer, 0, bytes);
        
        // Parse JSON
        return JsonConvert.DeserializeObject<dynamic>(response);
    }
    
    public void StartTracking()
    {
        var response = SendCommand("START_TRACKING");
        Console.WriteLine($"Tracking: {response.message}");
    }
    
    public void MonitorGestures()
    {
        string lastGesture = null;
        
        while (true)
        {
            Thread.Sleep(100); // Poll every 100ms
            
            var status = SendCommand("STATUS");
            
            // Check if in cooldown
            if (status.in_cooldown == true)
            {
                Console.WriteLine($"⏸ Cooldown: {status.cooldown_remaining}s");
                continue;
            }
            
            // Check for new gesture
            string currentGesture = status.last_gesture;
            if (currentGesture != null && currentGesture != lastGesture)
            {
                Console.WriteLine($"✓ Gesture detected: {currentGesture}");
                HandleGesture(currentGesture);
                lastGesture = currentGesture;
            }
        }
    }
    
    private void HandleGesture(string gesture)
    {
        switch (gesture)
        {
            case "swipe_right":
                Console.WriteLine("→ Swipe Right Action");
                break;
            case "swipe_left":
                Console.WriteLine("← Swipe Left Action");
                break;
            case "swipe_up":
                Console.WriteLine("↑ Swipe Up Action");
                break;
            case "swipe_down":
                Console.WriteLine("↓ Swipe Down Action");
                break;
            case "circle_clockwise":
                Console.WriteLine("↻ Circle Clockwise Action");
                break;
            case "circle_counterclockwise":
                Console.WriteLine("↺ Circle Counter-Clockwise Action");
                break;
            default:
                Console.WriteLine($"? Unknown gesture: {gesture}");
                break;
        }
    }
    
    public void Disconnect()
    {
        SendCommand("STOP_TRACKING");
        stream?.Close();
        client?.Close();
        Console.WriteLine("Disconnected from gesture service");
    }
}

// Usage
class Program
{
    static void Main()
    {
        var gestureClient = new GestureClient();
        
        try
        {
            gestureClient.Connect();
            gestureClient.StartTracking();
            gestureClient.MonitorGestures();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            gestureClient.Disconnect();
        }
    }
}
```

## Gesture Names

The service recognizes these gestures:

- `swipe_right` - Horizontal swipe to the right
- `swipe_left` - Horizontal swipe to the left
- `swipe_up` - Vertical swipe upward
- `swipe_down` - Vertical swipe downward
- `circle_clockwise` - Circular motion clockwise
- `circle_counterclockwise` - Circular motion counter-clockwise
- `wave` - Wave gesture
- `zoom_in` - Pinch/zoom in gesture
- `zoom_out` - Spread/zoom out gesture

## Configuration

### Confidence Threshold
Only gestures with confidence > 0.4 are triggered.

To adjust:
```python
# In gesture_service.py
confidence_threshold = 0.4  # Change to 0.5 for stricter, 0.3 for looser
```

### Cooldown Duration
Default: 3 seconds

To adjust:
```python
# In gesture_service.py
gesture_cooldown = 3.0  # Change to 2.0 for faster, 5.0 for slower
```

### Sliding Window Size
Default: 60 frames

To adjust:
```python
# In gesture_service.py
MAX_WINDOW_FRAMES = 60  # Change to 45 for smaller, 90 for larger
```

## Troubleshooting

### Issue: No gestures detected
1. Check if templates are loaded: `STATUS` → `templates: 8`
2. Perform clearer gestures
3. Check console logs for confidence scores
4. Lower confidence threshold if needed

### Issue: Too many false positives
1. Increase confidence threshold from 0.4 to 0.5
2. Increase cooldown from 3.0 to 5.0
3. Perform more distinct gestures

### Issue: Cooldown too long
1. Decrease cooldown from 3.0 to 2.0
2. Check `cooldown_remaining` in STATUS

### Issue: Connection refused
1. Ensure service is running: `python gesture_service.py`
2. Check port 5001 is not in use
3. Check firewall settings

## Performance Tips

### Polling Frequency
- **100ms (10 Hz)**: Good balance
- **50ms (20 Hz)**: More responsive, higher CPU
- **200ms (5 Hz)**: Lower CPU, less responsive

### Network Optimization
```csharp
// Use persistent connection
TcpClient client = new TcpClient();
client.NoDelay = true; // Disable Nagle's algorithm
client.Connect("127.0.0.1", 5001);
```

### Error Handling
```csharp
try
{
    var status = SendCommand("STATUS");
}
catch (IOException ex)
{
    Console.WriteLine("Connection lost, reconnecting...");
    Reconnect();
}
```

## Best Practices

1. **Always call START_TRACKING** before expecting gestures
2. **Poll STATUS regularly** (every 100ms) for best responsiveness
3. **Check in_cooldown** before processing gestures
4. **Handle connection errors** gracefully
5. **Call STOP_TRACKING** when done to release camera
6. **Use RESET** to clear state between sessions

---

**Updated**: May 3, 2026
**Version**: 2.1
