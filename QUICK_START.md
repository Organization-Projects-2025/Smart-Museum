# Quick Start Guide - Unified Museum Server

## 🚀 Getting Started in 3 Steps

### Step 1: Start the Unified Server

**Windows:**
```batch
run_unified_server.bat
```

**Linux/Mac:**
```bash
chmod +x run_unified_server.sh
./run_unified_server.sh
```

**Manual:**
```bash
python python/server/unified_museum_server.py
```

### Step 2: Verify Services Are Running

Open a new terminal and run the test script:

```bash
python python/server/test_unified_server.py
```

You should see all services marked as ✓ PASS.

### Step 3: Run Your C# Application

Start your Smart Museum C# application. It will automatically connect to all services on their default ports.

## 📋 What You Should See

When the unified server starts, you'll see:

```
========================================
Smart Museum - Unified Server
========================================

[14:32:15.123] MAIN ✓ Starting all services...
[14:32:15.234] MAIN ✓ Camera hub initialized: camera=0, mirror=0
[14:32:15.345] FACE_AUTH ✓ Starting service on port 5000
[14:32:15.456] GESTURE ✓ Starting service on port 5001
[14:32:15.567] GAZE_EMOTION ✓ Starting service on port 5002
[14:32:15.678] YOLO_CONTEXT ✓ Starting service on port 5003
[14:32:15.789] HAND_TRACK ✓ Starting service on port 5004

============================================================
Service Status:
  FACE_AUTH       - Port 5000 - ENABLED
  GESTURE         - Port 5001 - ENABLED
  GAZE_EMOTION    - Port 5002 - ENABLED
  YOLO_CONTEXT    - Port 5003 - ENABLED
  HAND_TRACK      - Port 5004 - ENABLED
============================================================

[14:32:16.123] MAIN ✓ Unified Museum Server running. Press Ctrl+C to stop.
[14:32:16.234] MAIN ✓ C# clients can connect to:
[14:32:16.345] MAIN ✓   - Face Auth:     127.0.0.1:5000
[14:32:16.456] MAIN ✓   - Gesture:       127.0.0.1:5001
[14:32:16.567] MAIN ✓   - Gaze+Emotion:  127.0.0.1:5002
[14:32:16.678] MAIN ✓   - YOLO Context:  127.0.0.1:5003
[14:32:16.789] MAIN ✓   - Hand Track:    127.0.0.1:5004
```

## 🔧 Common Configuration

### Change Camera Index

If your webcam is not camera 0:

**Windows:**
```batch
set MUSEUM_CAMERA=1
run_unified_server.bat
```

**Linux/Mac:**
```bash
export MUSEUM_CAMERA=1
./run_unified_server.sh
```

### Disable Specific Services

If you don't need all services:

**Windows:**
```batch
set DISABLE_HAND_TRACK=1
set DISABLE_GESTURE=1
run_unified_server.bat
```

**Linux/Mac:**
```bash
export DISABLE_HAND_TRACK=1
export DISABLE_GESTURE=1
./run_unified_server.sh
```

### Use Real YOLO Instead of Mock Data

If you have ultralytics installed:

**Windows:**
```batch
set YOLO_CONTEXT_MOCK=0
run_unified_server.bat
```

**Linux/Mac:**
```bash
export YOLO_CONTEXT_MOCK=0
./run_unified_server.sh
```

## 🐛 Troubleshooting

### "Port Already in Use" Error

**Problem:** A service can't start because its port is already in use.

**Solution:**
1. Find what's using the port:
   ```bash
   # Windows
   netstat -ano | findstr :5000

   # Linux/Mac
   lsof -i :5000
   ```

2. Kill the process or change the port:
   ```bash
   # Windows
   set PYTHON_SERVER_PORT=5005

   # Linux/Mac
   export PYTHON_SERVER_PORT=5005
   ```

### "Camera Unavailable" Error

**Problem:** Camera services fail to start.

**Solution:**
1. Try a different camera index:
   ```bash
   export MUSEUM_CAMERA=1
   ```

2. Disable camera services temporarily:
   ```bash
   export DISABLE_HAND_TRACK=1
   export DISABLE_GAZE_EMOTION=1
   export DISABLE_YOLO_CONTEXT=1
   ```

3. Check if another application is using the camera.

### Service Import Errors

**Problem:** Services fail to start due to missing imports.

**Solution:**
1. Install required dependencies:
   ```bash
   pip install opencv-python face-recognition mediapipe ultralytics
   ```

2. Ensure dollarpy-service is in the correct location.

### Services Not Responding

**Problem:** Test script shows services as failed.

**Solution:**
1. Check if the unified server is actually running.
2. Look for error messages in the server output.
3. Try restarting the unified server.
4. Check Windows Firewall or antivirus settings.

## 📊 Monitoring Service Health

The unified server includes built-in health monitoring:

- **Automatic Checks**: Service status is checked every 10 seconds
- **Error Logging**: Failed services are logged with error details
- **Heartbeat Monitoring**: Services that stop sending heartbeats are flagged

Watch for these log messages:

```
[14:35:20.456] MAIN ⚠ HAND_TRACK is down (errors: 1, last error: Camera unavailable)
[14:35:30.789] MAIN ⚠ GAZE_EMOTION hasn't sent heartbeat in 30+ seconds
```

## 🔄 Switching from Legacy Mode

If you're currently using the old multi-process setup:

1. **Stop all existing Python servers**
2. **Start the unified server**: `run_unified_server.bat`
3. **No C# changes needed**: Your C# app will work exactly the same
4. **Monitor logs**: Ensure all services start successfully

To switch back to legacy mode if needed:

```bash
# Windows
set USE_UNIFIED_SERVER=0
.\run_all_servers.ps1

# Linux/Mac
export USE_UNIFIED_SERVER=0
./run_all_servers.sh
```

## 🎯 Next Steps

1. **Read the full documentation**: See `UNIFIED_SERVER_README.md`
2. **Configure your environment**: Copy `unified_server.env.example` to `.env`
3. **Test with your C# app**: Run your Smart Museum application
4. **Monitor performance**: Watch the logs for any issues

## 💡 Tips

- **Development**: Use `DISABLE_<SERVICE>=1` to disable services you're not working on
- **Testing**: Run `test_unified_server.py` to verify connectivity
- **Debugging**: Check the color-coded logs for each service
- **Performance**: Monitor CPU/memory usage if running on limited hardware

## 🆘 Need Help?

If you encounter issues not covered here:

1. Check the full documentation: `UNIFIED_SERVER_README.md`
2. Review the server logs for error messages
3. Test individual services separately
4. Check your Python dependencies are installed

---

**Ready to go?** Start the server and run your C# app! 🎉