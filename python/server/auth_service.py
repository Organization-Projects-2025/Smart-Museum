"""
auth_server.py — TCP server on port 5000.
Handles face ID and Bluetooth authentication for C#.

Wire protocol (line-based text, newline-terminated):
  bluetooth_scan <MAC>      → FOUND:<name>:<mac> | NOT_FOUND | ERROR:<msg>
  bluetooth_register_pick   → FOUND\t<name>\t<mac> | NOT_FOUND | ERROR:<msg>
  face_id_scan              → FOUND:<uid> | NOT_FOUND | ERROR:<msg>
  face_register_scan        → FOUND:<uid> | NEW:<uid> | NOT_FOUND | ERROR:<msg>
  face_auth_lobby           → FOUND:<uid> | NEW:<uid> | NOT_FOUND | CANCELLED | ERROR:<msg>
  exit                      → BYE
"""

import os
import re
import socket
import threading
import time

import cv2
import face_recognition
import numpy as np

import face_store

# ── Bluetooth ─────────────────────────────────────────────────────────────────
try:
    import bluetooth as _bt
    _BT_OK = True
except ImportError:
    _bt    = None
    _BT_OK = False

_MAC_RE = re.compile(r'^([0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}$')

def _bt_scan(mac: str) -> str:
    if not _MAC_RE.match(mac.strip()):
        return f"ERROR:Invalid MAC '{mac}'"
    if not _BT_OK:
        return "ERROR:PyBluez not installed (pip install pybluez2)"
    try:
        for addr, name in _bt.discover_devices(lookup_names=True, duration=8, flush_cache=True):
            if addr.upper() == mac.upper():
                return f"FOUND:{name}:{addr}"
        return "NOT_FOUND"
    except Exception as e:
        return f"ERROR:{e}"

def _bt_pick() -> str:
    if not _BT_OK:
        return "ERROR:PyBluez not installed (pip install pybluez2)"
    try:
        devices = _bt.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        if not devices:
            return "NOT_FOUND"
        for addr, name in devices:
            if name and str(name).strip():
                return f"FOUND\t{name.strip()}\t{addr}"
        addr, name = devices[0]
        return f"FOUND\t{name or 'Unknown'}\t{addr}"
    except Exception as e:
        return f"ERROR:{e}"

# ── Face recognition (hub-based) ──────────────────────────────────────────────
_hub = None

def set_hub(hub):
    global _hub
    _hub = hub

def _face_scan_and_match() -> str:
    if not face_store._encodings:
        face_store.load()
    if not face_store._encodings:
        return "ERROR:No known faces loaded"
    deadline = time.time() + 10.0
    while time.time() < deadline:
        frame = _hub.get_frame() if _hub else None
        if frame is None:
            time.sleep(0.05)
            continue
        small = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
        rgb   = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
        locs  = face_recognition.face_locations(rgb)
        encs  = face_recognition.face_encodings(rgb, locs)
        for enc in encs:
            uid = face_store.match(enc)
            if uid:
                return f"FOUND:{uid}"
    return "NOT_FOUND"

def _face_register_scan() -> str:
    face_store.load()
    face_store.ensure_faces_dir()
    deadline = time.time() + 12.0
    while time.time() < deadline:
        frame = _hub.get_frame() if _hub else None
        if frame is None:
            time.sleep(0.05)
            continue
        small = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
        rgb   = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
        locs  = face_recognition.face_locations(rgb)
        encs  = face_recognition.face_encodings(rgb, locs)
        if not encs:
            continue
        uid = face_store.match(encs[0])
        if uid:
            return f"FOUND:{uid}"
        loc_full = (locs[0][0]*4, locs[0][1]*4, locs[0][2]*4, locs[0][3]*4)
        new_id   = face_store.next_user_id()
        face_store.save_face_crop(frame, loc_full, new_id)
        return f"NEW:{new_id}"
    return "NOT_FOUND"

# ── Face lobby (in-process, uses hub frames — no subprocess needed) ───────────

def _face_lobby() -> str:
    """
    Run face sign-in using frames from the shared hub.
    No subprocess, no camera conflict.
    Returns: FOUND:<uid> | NEW:<uid> | NOT_FOUND | CANCELLED | ERROR:<msg>
    """
    import face_store as _fs
    _fs.load()

    deadline     = time.time() + 18.0
    face_stable  = None
    countdown_start = None

    while time.time() < deadline:
        frame = _hub.get_frame() if _hub else None
        if frame is None:
            time.sleep(0.05)
            continue

        small = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
        rgb   = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
        locs  = face_recognition.face_locations(rgb)

        if not locs:
            face_stable = countdown_start = None
            time.sleep(0.05)
            continue

        # Scale location back to full frame
        loc = tuple(x * 4 for x in locs[0])  # (top, right, bottom, left)
        top, right, bottom, left = loc
        h, w = frame.shape[:2]
        cx, cy = (left + right) // 2, (top + bottom) // 2
        ok_pos = (abs(cx - w//2) < w * 0.35 and abs(cy - h//2) < h * 0.35
                  and 0.08 < (right - left) / float(w) < 0.6)

        if not ok_pos:
            face_stable = countdown_start = None
            time.sleep(0.05)
            continue

        # Face is well-positioned
        if face_stable is None:
            face_stable = time.time()

        if time.time() - face_stable < 1.0:
            time.sleep(0.05)
            continue

        # Try to match against known faces
        if countdown_start is None:
            rgb_full = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            encs = face_recognition.face_encodings(rgb_full, [loc])
            if encs:
                uid = _fs.match(encs[0])
                if uid:
                    return f"FOUND:{uid}"
            countdown_start = time.time()

        # New face — wait 3 seconds then save
        if time.time() - countdown_start >= 3.0:
            new_id = _fs.next_user_id()
            _fs.save_face_crop(frame, loc, new_id)
            return f"NEW:{new_id}"

        time.sleep(0.05)

    return "NOT_FOUND"

# ── TCP server ────────────────────────────────────────────────────────────────
def _handle(conn, addr):
    print(f"[Auth] Client: {addr}")
    buf = ""
    try:
        while True:
            data = conn.recv(1024)
            if not data:
                break
            buf += data.decode("utf-8", errors="ignore")
            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                cmd = line.strip()
                if not cmd:
                    continue
                parts = cmd.split()
                if parts[0] == "bluetooth_scan" and len(parts) >= 2:
                    conn.send(_bt_scan(parts[1]).encode())
                elif parts[0] == "bluetooth_register_pick":
                    conn.send(_bt_pick().encode())
                elif parts[0] == "face_id_scan":
                    conn.send(_face_scan_and_match().encode())
                elif parts[0] == "face_register_scan":
                    conn.send(_face_register_scan().encode())
                elif parts[0] == "face_auth_lobby":
                    conn.send(_face_lobby().encode())
                elif parts[0] == "exit":
                    conn.send(b"BYE")
                    return
    except Exception as e:
        print(f"[Auth] Error: {e}")
    finally:
        conn.close()
        print(f"[Auth] Disconnected: {addr}")

def start(host="127.0.0.1", port=None):
    port = port or int(os.environ.get("PYTHON_SERVER_PORT", "5000"))
    srv = socket.socket()
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(5)
    print(f"[Auth] Listening on {host}:{port}")
    try:
        while True:
            conn, addr = srv.accept()
            threading.Thread(target=_handle, args=(conn, addr), daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
