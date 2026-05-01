# Unified Museum Server

Single-process Python server that runs all Smart Museum services with fault isolation.

## Features

- **Single Process**: All services run in one Python process
- **Fault Isolation**: If one service crashes, others continue running
- **Shared Camera Hub**: Vision services share one camera to avoid conflicts
- **Health Monitoring**: Automatic monitoring and logging of service status
- **Flexible Configuration**: Enable/disable individual services via environment variables
- **Color-coded Logging**: Easy to read logs with service identification

## Services

| Service | Port | Description | C# Client |
|---------|------|-------------|-----------|
| FACE_AUTH | 5000 | Face ID + Bluetooth authentication | `AuthIntegration.cs` |
| GESTURE | 5001 | DollarPy gesture recognition | `GestureClient.cs` |
| GAZE_EMOTION | 5002 | Gaze tracking + emotion detection | `GazeEmotionClient.cs` |
| YOLO_CONTEXT | 5003 | YOLO object detection/tracking | `YoloContextClient.cs` |
| HAND_TRACK | 5004 | MediaPipe hand tracking | `HandTrackClient.cs` |

## Quick Start

### Windows
```batch
run_unified_server.bat
```

### Linux/Mac
```bash
chmod +x run_unified_server.sh
./run_unified_server.sh
```

### Manual
```bash
python python/server/unified_museum_server.py
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `MUSEUM_CAMERA` | Camera index to use | `0` |
| `GAZE_EMOTION_MIRROR` | Mirror camera frames (1=enabled) | `0` |
| `PYTHON_SERVER_PORT` | Face auth service port | `5000` |
| `HAND_TRACK_PORT` | Hand tracking service port | `5004` |
| `DISABLE_<SERVICE>` | Disable specific service | `none` |

### Disable Specific Services

To disable a service, set the corresponding environment variable:

```bash
# Disable hand tracking
export DISABLE_HAND_TRACK=1

# Disable gesture recognition
export DISABLE_GESTURE=1

# Disable multiple services
export DISABLE_HAND_TRACK=1
export DISABLE_GESTURE=1
```

## Logging

The server provides color-coded, timestamped logs for each service:

- **✓ INFO** - Normal operation messages
- **⚠ WARN** - Warning messages
- **✗ ERROR** - Error messages (service failures)
- **○ DEBUG** - Debug information

### Log Format
```
[HH:MM:SS.mmm] SERVICE_NAME SYMBOL Message
```

Example:
```
[14:32:15.123] FACE_AUTH ✓ Starting service on port 5000
[14:32:15.456] GAZE_EMOTION ✓ Service started successfully
[14:32:20.789] HAND_TRACK ✗ Service crashed: Camera unavailable
```

## Fault Isolation

The unified server is designed to handle service failures gracefully:

1. **Independent Threads**: Each service runs in its own thread
2. **Error Catching**: Exceptions are caught and logged without crashing other services
3. **Health Monitoring**: Service status is monitored every 10 seconds
4. **Error Tracking**: Error counts and last error messages are tracked

### Example Failure Scenario

If the hand tracking service crashes due to camera issues:

```
[14:35:10.123] HAND_TRACK ✗ Service crashed: Camera unavailable
[14:35:20.456] MAIN ⚠ HAND_TRACK is down (errors: 1, last error: Camera unavailable)
```

Other services continue running normally:
```
[14:35:15.234] FACE_AUTH ✓ User authenticated: user42
[14:35:18.567] GAZE_EMOTION ✓ Gaze detected: (0.5, 0.6)
```

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│           Unified Museum Server (Main Process)          │
├─────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ FACE_AUTH    │  │ GESTURE      │  │ GAZE_EMOTION │  │
│  │ Port 5000    │  │ Port 5001    │  │ Port 5002    │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐                     │
│  │ YOLO_CONTEXT │  │ HAND_TRACK   │                     │
│  │ Port 5003    │  │ Port 5004    │                     │
│  └──────────────┘  └──────────────┘                     │
├─────────────────────────────────────────────────────────┤
│              Shared Camera Hub (Optional)               │
│         (Single camera for all vision services)         │
└─────────────────────────────────────────────────────────┘
```

## Troubleshooting

### Port Already in Use
If you see "Port already in use" errors:

1. Check what's using the port:
   ```bash
   # Windows
   netstat -ano | findstr :5000

   # Linux/Mac
   lsof -i :5000
   ```

2. Kill the process or change the port via environment variable

### Camera Not Available
If camera services fail to start:

1. Check camera index:
   ```bash
   export MUSEUM_CAMERA=1  # Try camera index 1
   ```

2. Disable camera services temporarily:
   ```bash
   export DISABLE_HAND_TRACK=1
   export DISABLE_GAZE_EMOTION=1
   export DISABLE_YOLO_CONTEXT=1
   ```

### Service Import Errors
If you see import errors:

1. Ensure all dependencies are installed:
   ```bash
   pip install -r requirements.txt
   ```

2. Check that dollarpy-service is in the correct location

## Development

### Adding a New Service

1. Create the service module in `python/server/`
2. Add a start function to the service
3. Register the service in `UnifiedMuseumServer.initialize_services()`
4. Update this README with the new service information

### Testing Individual Services

To test a service without running the full server:

```bash
# Test face auth
python python/server/python_server.py

# Test gaze emotion
python python/server/gaze_emotion_service.py

# Test hand tracking
python python/server/hand_tracker_service.py
```

## Migration from Multiple Servers

If you're currently running multiple separate servers:

1. **Stop all existing servers**
2. **Run the unified server**: `run_unified_server.bat`
3. **No C# changes needed**: Clients connect to the same ports
4. **Monitor logs**: Check that all services start successfully

## Performance Considerations

- **CPU Usage**: All services share the same process CPU
- **Memory Usage**: Shared memory for common libraries
- **Camera Usage**: Single camera for vision services (if hub enabled)
- **Network**: Separate sockets for each service

## Security Notes

- All services bind to `127.0.0.1` (localhost only)
- No external network access required
- Camera access is limited to the configured camera index

## License

Part of the Smart Museum project.