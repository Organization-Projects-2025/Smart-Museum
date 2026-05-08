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
import demographics_service

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
        rel_path = face_store.save_face_crop(frame, loc_full, new_id)
        face_store.reload()
        return _build_new_response(new_id, rel_path)
    return "NOT_FOUND"

# ── Face lobby (in-process, uses hub frames — no subprocess needed) ───────────

def _build_new_response(user_id: str, face_rel_path: str) -> str:
    """Analyse the saved face and return NEW:<uid>:<age>:<gender>:<race>."""
    abs_path = os.path.join(face_store._WORKSPACE, face_rel_path.replace("/", os.sep))
    try:
        age, gender, race = demographics_service.analyze(abs_path)
    except Exception as e:
        print(f"[Auth] Demographics fallback: {e}")
        age, gender, race = 25, "male", "white"
    return f"NEW:{user_id}:{age}:{gender}:{race}"

# ── Face lobby (in-process, uses hub frames — no subprocess needed) ───────────

def _face_lobby(progress_cb=None) -> str:
    """
    Run face sign-in using frames from the shared hub.
    No subprocess, no camera conflict.
    Returns: FOUND:<uid> | NEW:<uid>:<age>:<gender>:<race> | NOT_FOUND | CANCELLED | ERROR:<msg>
    """
    import face_store as _fs
    _fs.load()
    print(f"[Auth] face_lobby started. Hub={_hub is not None}, known_faces={len(_fs._encodings)}")
    if progress_cb: progress_cb("NO_FACE|Scanning for your face...")

    deadline        = time.time() + 18.0
    face_stable     = None
    countdown_start = None
    last_face_time  = None          # grace period: tolerate missed frames
    last_loc        = None          # keep last good location during grace
    frame_count     = 0
    face_count      = 0
    GRACE_SEC       = 1.5           # allow 1.5s of missed frames

    while time.time() < deadline:
        frame = _hub.get_frame() if _hub else None
        if frame is None:
            time.sleep(0.05)
            continue

        frame_count += 1

        small = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
        rgb   = cv2.cvtColor(small, cv2.COLOR_BGR2RGB)
        locs  = face_recognition.face_locations(rgb)

        if frame_count <= 3:
            print(f"[Auth] frame #{frame_count}: shape={small.shape}, "
                  f"faces_found={len(locs)}, locs={locs}")

        if not locs:
            # Grace period — only reset if no face for GRACE_SEC
            if last_face_time and (time.time() - last_face_time) > GRACE_SEC:
                face_stable = countdown_start = None
                last_face_time = None
                last_loc = None
                if progress_cb: progress_cb("NO_FACE|Scanning for your face...")
            time.sleep(0.05)
            continue

        face_count += 1
        last_face_time = time.time()
        # Scale location back to full frame
        loc = tuple(x * 4 for x in locs[0])  # (top, right, bottom, left)
        last_loc = loc
        top, right, bottom, left = loc
        h, w = frame.shape[:2]
        cx, cy = (left + right) // 2, (top + bottom) // 2
        face_w_ratio = (right - left) / float(w)
        ok_pos = (abs(cx - w//2) < w * 0.35 and abs(cy - h//2) < h * 0.35
                  and 0.08 < face_w_ratio < 0.6)

        if face_count <= 3 or face_count % 20 == 0:
            print(f"[Auth] face #{face_count}: loc={loc}, "
                  f"fw={face_w_ratio:.2f}, ok={ok_pos}")

        if not ok_pos:
            face_stable = countdown_start = None
            if progress_cb: progress_cb("UNSTABLE|Face detected, please hold still and center yourself.")
            time.sleep(0.05)
            continue

        # Face is well-positioned
        if face_stable is None:
            face_stable = time.time()
            print(f"[Auth] Face locked — stabilising 1s")
            if progress_cb: progress_cb("LOCKED|Face locked! Please hold still...")

        if time.time() - face_stable < 1.0:
            time.sleep(0.05)
            continue

        # Try to match against known faces
        if countdown_start is None:
            if progress_cb: progress_cb("LOCKED|Analyzing face...")
            rgb_full = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            encs = face_recognition.face_encodings(rgb_full, [loc])
            print(f"[Auth] Encoding computed: {len(encs)} encodings")
            if encs:
                uid = _fs.match(encs[0])
                print(f"[Auth] Match result: {uid}")
                if uid:
                    return f"FOUND:{uid}"
            countdown_start = time.time()
            print(f"[Auth] No match — new face countdown started (3s)")
            if progress_cb: progress_cb("COUNTDOWN:3.0|Creating new profile in 3.0s...")

        # New face — wait 3 seconds then save + analyse demographics
        if time.time() - countdown_start >= 3.0:
            if progress_cb: progress_cb("DEMOGRAPHICS|Running demographic analysis...")
            new_id = _fs.next_user_id()
            rel_path = _fs.save_face_crop(frame, loc, new_id)
            print(f"[Auth] Saved new face: {new_id} -> {rel_path}")
            _fs.reload()
            return _build_new_response(new_id, rel_path)
        else:
            time_left = 3.0 - (time.time() - countdown_start)
            if progress_cb: progress_cb(f"COUNTDOWN:{time_left:.1f}|Creating new profile in {time_left:.1f}s...")

        time.sleep(0.05)

    print(f"[Auth] face_lobby timed out. frames={frame_count}, faces_detected={face_count}")
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
                    def progress_cb(msg):
                        try:
                            conn.send(f"PROGRESS:{msg}\n".encode("utf-8"))
                        except:
                            pass
                    result = _face_lobby(progress_cb)
                    conn.send(result.encode("utf-8") + b"\n")
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
