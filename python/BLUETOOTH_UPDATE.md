# Bluetooth Library Update - Windows Compatibility

> **Current stack (2026):** `python_server.py` uses **PyBluez2** again (`import bluetooth`, `bluetooth.discover_devices`). Root `requirements.txt` lists `pybluez2>=0.40`. The sections below describe an older switch to bleak and are kept for history only.

## Changes Made

### 1. Replaced pybluez2 with bleak

**Problem**: `pybluez2` doesn't work on Windows (missing `bluetooth\windows` directory)

**Solution**: Switched to `bleak` - a modern, cross-platform Bluetooth library that works on Windows, macOS, and Linux.

### 2. Updated Files

#### Root `requirements.txt` (see also `python/requirements.txt` → includes root)
- Removed: `pybluez2>=0.46`
- Added: `bleak>=0.21.0`

#### `python/server/python_server.py`
- Changed import from `import bluetooth` to `from bleak import BleakScanner`
- Added `import asyncio` for async Bluetooth scanning
- Rewrote `scan_bluetooth()` function to use bleak's async API
- Added `_async_scan_bluetooth()` helper function

### 3. Installation Steps

From the **repository root** (folder containing `install_python_deps.ps1`):

```powershell
powershell -ExecutionPolicy Bypass -File .\install_python_deps.ps1
```

That installs `bleak`, `dlib-bin`, `face-recognition-models`, `face_recognition` (with `--no-deps`), and all other Python dependencies into `.venv`.

### 4. How It Works

**Old (pybluez2)**:
```python
devices = bluetooth.discover_devices(lookup_names=True, duration=8)
for addr, name in devices:
    if addr == target_mac:
        return f"FOUND:{name}:{addr}"
```

**New (bleak)**:
```python
devices = await BleakScanner.discover(timeout=8.0)
for device in devices:
    if device.address.upper() == target_mac.upper():
        return f"FOUND:{device.name}:{device.address}"
```

### 5. MAC Address Format

Bleak normalizes MAC addresses to uppercase with colons (e.g., `70:31:7F:15:75:34`).
The code automatically handles both formats:
- Input: `70:31:7F:15:75:34` or `70-31-7F-15-75-34`
- Normalized: `70:31:7F:15:75:34`

### 6. Testing

Start the Python server:
```bash
cd Smart-Museum/python/server
..\..\..venv\Scripts\python.exe python_server.py
```

From C#, send Bluetooth scan command:
```
bluetooth_scan 70:31:7F:15:75:34
```

Expected responses:
- `FOUND:DeviceName:70:31:7F:15:75:34` - Device found
- `NOT_FOUND` - Device not in range
- `ERROR:message` - Bluetooth error

### 7. User Database Update

Updated `C#/content/auth/users.csv`:
- user3 (Lina Adel) MAC address: `70:31:7F:15:75:34`

## Benefits of bleak

1. **Cross-platform**: Works on Windows, macOS, Linux
2. **Modern**: Uses Windows BLE APIs (WinRT)
3. **Maintained**: Active development and support
4. **Async**: Non-blocking Bluetooth operations
5. **No compilation**: Pure Python, no C++ build required

## Compatibility

- Windows 10/11 with Bluetooth support
- Python 3.11+
- Works with both Bluetooth Classic and BLE devices
