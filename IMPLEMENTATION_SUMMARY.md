# Unified Museum Server - Implementation Summary

## 🎉 What Was Created

A unified server implementation that consolidates all Python museum services into a single fault-isolated process.

## 📁 New Files Created

### Core Server
- **`python/server/unified_museum_server.py`** - Main unified server with fault isolation
- **`python/server/test_unified_server.py`** - Service connectivity test script

### Startup Scripts
- **`run_unified_server.bat`** - Windows startup script
- **`run_unified_server.sh`** - Linux/Mac startup script (executable)

### Documentation
- **`UNIFIED_SERVER_README.md`** - Complete documentation
- **`QUICK_START.md`** - Quick start guide
- **`unified_server.env.example`** - Configuration example file

### Updated Files
- **`run_all_servers.ps1`** - Updated to support unified server mode
- **`run_all_servers.sh`** - Updated to support unified server mode

## 🏗️ Architecture

### Single Process, Fault-Isolated Services

```
┌─────────────────────────────────────────────────────────┐
│           Unified Museum Server (Main Process)          │
├─────────────────────────────────────────────────────────┤
│  ServiceWrapper (Fault Isolation)                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ FACE_AUTH    │  │ GESTURE      │  │ GAZE_EMOTION │  │
│  │ Port 5000    │  │ Port 5001    │  │ Port 5002    │  │
│  │ Thread       │  │ Thread       │  │ Thread       │  │
│  │ Try-Catch    │  │ Try-Catch    │  │ Try-Catch    │  │
│  └──────────────┘  └──────────────┘  └──────────────┘  │
│  ┌──────────────┐  ┌──────────────┐                     │
│  │ YOLO_CONTEXT │  │ HAND_TRACK   │                     │
│  │ Port 5003    │  │ Port 5004    │                     │
│  │ Thread       │  │ Thread       │                     │
│  │ Try-Catch    │  │ Try-Catch    │                     │
│  └──────────────┘  └──────────────┘                     │
├─────────────────────────────────────────────────────────┤
│  Health Monitoring (10-second intervals)                 │
│  ServiceLogger (Color-coded, timestamped)                │
│  SharedCameraHub (Optional, single camera)              │
└─────────────────────────────────────────────────────────┘
```

### Key Features

1. **Fault Isolation**: Each service runs in its own thread with try-catch
2. **Health Monitoring**: Automatic monitoring every 10 seconds
3. **Color-coded Logging**: Easy to identify service-specific messages
4. **Shared Camera Hub**: Vision services share one camera (optional)
5. **Flexible Configuration**: Enable/disable services via environment variables
6. **Graceful Shutdown**: Clean shutdown of all services

## 🚀 How to Use

### Quick Start

```bash
# Windows
run_unified_server.bat

# Linux/Mac
./run_unified_server.sh

# Manual
python python/server/unified_museum_server.py
```

### Test Services

```bash
python python/server/test_unified_server.py
```

### Configuration

```bash
# Change camera
export MUSEUM_CAMERA=1

# Disable specific service
export DISABLE_HAND_TRACK=1

# Use real YOLO
export YOLO_CONTEXT_MOCK=0
```

## 🔌 Service Ports

| Service | Port | Protocol | C# Client |
|---------|------|----------|-----------|
| FACE_AUTH | 5000 | TCP | `AuthIntegration.cs` |
| GESTURE | 5001 | TCP/JSON | `GestureClient.cs` |
| GAZE_EMOTION | 5002 | TCP/JSON | `GazeEmotionClient.cs` |
| YOLO_CONTEXT | 5003 | TCP/JSON | `YoloContextClient.cs` |
| HAND_TRACK | 5004 | TCP/JSON | `HandTrackClient.cs` |

## 🛡️ Fault Isolation Examples

### Example 1: Hand Tracking Crashes

```
[14:35:10.123] HAND_TRACK ✗ Service crashed: Camera unavailable
[14:35:20.456] MAIN ⚠ HAND_TRACK is down (errors: 1, last error: Camera unavailable)

[14:35:15.234] FACE_AUTH ✓ User authenticated: user42
[14:35:18.567] GAZE_EMOTION ✓ Gaze detected: (0.5, 0.6)
```

**Result**: Other services continue running normally.

### Example 2: YOLO Service Import Error

```
[14:32:15.123] YOLO_CONTEXT ✗ Failed to import yolo_context_service: No module named 'ultralytics'
[14:32:25.456] MAIN ⚠ YOLO_CONTEXT is down (errors: 1, last error: Failed to import...)

[14:32:16.234] FACE_AUTH ✓ Starting service on port 5000
[14:32:16.345] GESTURE ✓ Starting service on port 5001
```

**Result**: Other services start successfully, YOLO service is disabled.

## 📊 Logging System

### Log Format

```
[HH:MM:SS.mmm] SERVICE_NAME SYMBOL Message
```

### Log Levels

- **✓ INFO** - Normal operation
- **⚠ WARN** - Warning messages
- **✗ ERROR** - Service failures
- **○ DEBUG** - Debug information

### Service Colors

- **FACE_AUTH** - Green
- **GESTURE** - Yellow
- **GAZE_EMOTION** - Blue
- **YOLO_CONTEXT** - Magenta
- **HAND_TRACK** - Cyan
- **MAIN** - White

## 🔄 Migration from Legacy

### Before (Multiple Processes)

```bash
# Terminal 1
python python/server/museum_vision_server.py

# Terminal 2
python python/server/hand_tracker_service.py
```

### After (Single Process)

```bash
# Single terminal
python python/server/unified_museum_server.py
```

### Benefits

- **Simpler deployment**: One command to start all services
- **Better fault isolation**: One service crash doesn't affect others
- **Unified logging**: All logs in one place with color coding
- **Health monitoring**: Automatic service status checking
- **Easier debugging**: Centralized error reporting

## 🧪 Testing

### Test All Services

```bash
python python/server/test_unified_server.py
```

### Expected Output

```
============================================================
Unified Museum Server - Service Test
============================================================

Testing Services:
------------------------------------------------------------
Testing FACE_AUTH on 127.0.0.1:5000... ✓ Connected
Testing GESTURE on 127.0.0.1:5001... ✓ Connected (JSON OK)
Testing GAZE_EMOTION on 127.0.0.1:5002... ✓ Connected (JSON OK)
Testing YOLO_CONTEXT on 127.0.0.1:5003... ✓ Connected (JSON OK)
Testing HAND_TRACK on 127.0.0.1:5004... ✓ Connected

============================================================
Test Results:
------------------------------------------------------------
  FACE_AUTH       - ✓ PASS
  GESTURE         - ✓ PASS
  GAZE_EMOTION    - ✓ PASS
  YOLO_CONTEXT    - ✓ PASS
  HAND_TRACK      - ✓ PASS
------------------------------------------------------------
  Total: 5 | Passed: 5 | Failed: 0
============================================================

✓ All services are running correctly!
```

## 🔧 Configuration Options

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `MUSEUM_CAMERA` | Camera index | `0` |
| `GAZE_EMOTION_MIRROR` | Mirror camera frames | `0` |
| `PYTHON_SERVER_PORT` | Face auth port | `5000` |
| `HAND_TRACK_PORT` | Hand tracking port | `5004` |
| `YOLO_CONTEXT_MOCK` | Use mock YOLO data | `1` |
| `DISABLE_<SERVICE>` | Disable specific service | `none` |
| `USE_UNIFIED_SERVER` | Use unified server mode | `0` |

### Service-Specific Disable

```bash
# Disable hand tracking only
export DISABLE_HAND_TRACK=1

# Disable multiple services
export DISABLE_HAND_TRACK=1
export DISABLE_GESTURE=1
export DISABLE_YOLO_CONTEXT=1
```

## 📈 Performance Considerations

### Advantages

- **Single Process**: Reduced overhead vs multiple processes
- **Shared Resources**: Camera, memory, CPU optimization
- **Faster Startup**: All services start together
- **Unified Monitoring**: Single health check system

### Considerations

- **CPU Usage**: All services share same process CPU
- **Memory Usage**: Shared memory for common libraries
- **Fault Domain**: Process-level issues affect all services (mitigated by fault isolation)

## 🐛 Troubleshooting

### Common Issues

1. **Port conflicts**: Change ports or stop conflicting processes
2. **Camera unavailable**: Try different camera index or disable camera services
3. **Import errors**: Install missing dependencies
4. **Service not responding**: Check logs for error messages

### Debug Mode

```bash
# Enable debug logging
export PYTHONUNBUFFERED=1
python python/server/unified_museum_server.py
```

## 📚 Documentation

- **`UNIFIED_SERVER_README.md`** - Complete technical documentation
- **`QUICK_START.md`** - Quick start guide with examples
- **`unified_server.env.example`** - Configuration template

## 🎯 Next Steps

1. **Test the server**: Run `test_unified_server.py`
2. **Configure as needed**: Set environment variables
3. **Integrate with C#**: No changes needed, clients work as before
4. **Monitor performance**: Watch logs for any issues
5. **Customize**: Add new services or modify existing ones

## 🏆 Success Criteria

✅ **Single process** - All services run in one Python process
✅ **Fault isolation** - Service failures don't affect others
✅ **Health monitoring** - Automatic status checking and logging
✅ **Easy deployment** - One command to start all services
✅ **C# compatibility** - No changes needed to existing clients
✅ **Flexible configuration** - Enable/disable services as needed
✅ **Good logging** - Color-coded, timestamped logs
✅ **Testing tools** - Service connectivity test script

## 🎉 Summary

The unified museum server provides a robust, fault-isolated solution for running all Python services in a single process. It maintains full compatibility with existing C# clients while providing better monitoring, logging, and fault tolerance.

**Ready to use?** Run `run_unified_server.bat` (Windows) or `./run_unified_server.sh` (Linux/Mac) to get started!