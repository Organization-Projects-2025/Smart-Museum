"""
hand_server.py — TCP server on port 5004.
Streams MediaPipe hand pose JSON to C# at ~30 fps.

Wire protocol:
  START → (begins streaming)
  STOP  → (pauses streaming)
  QUIT  → (closes connection)

Frame: {"valid":bool,"wx":float,"wy":float,"wz":float,"fist":bool}
"""

import json
import math
import os
import socket
import sys
import threading
import time

import cv2

# ── MediaPipe ─────────────────────────────────────────────────────────────────
try:
    # Use the inline wrapper from gesture_service
    from gesture_service import mp
    _HANDS = mp.hands
    _MP_OK = True
except Exception as e:
    _HANDS = None
    _MP_OK = False
    print(f"[Hand] mediapipe_compat unavailable: {e}")

REF_PALM = 0.32
_FPS     = 30
_INTERVAL= 1.0 / _FPS

_hub = None
def set_hub(hub):
    global _hub
    _hub = hub

# ── Pose extraction ───────────────────────────────────────────────────────────
def _pose(frame_bgr, hands_model) -> dict:
    rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
    res = hands_model.process(rgb)
    if not res.multi_hand_landmarks:
        return {"valid":False,"wx":0.5,"wy":0.5,"wz":0.5,"fist":False}
    lm = res.multi_hand_landmarks[0].landmark
    dx,dy = lm[9].x-lm[0].x, lm[9].y-lm[0].y
    ps = math.sqrt(dx*dx+dy*dy)
    wz = max(0.0, min(1.0, 1.0-(ps/REF_PALM)*0.85))
    fist = sum(1 for tip,pip in [(8,6),(12,10),(16,14),(20,18)] if lm[tip].y>lm[pip].y) >= 3
    return {"valid":True,"wx":round(float(lm[0].x),4),"wy":round(float(1.0-lm[0].y),4),
            "wz":round(wz,4),"fist":fist}

# ── TCP ───────────────────────────────────────────────────────────────────────
def _handle(conn, addr):
    print(f"[Hand] Client: {addr}")
    if not _MP_OK:
        try: conn.sendall((json.dumps({"error":"mediapipe_unavailable"})+"\n").encode())
        except: pass
        conn.close()
        return

    hands = _HANDS.Hands(static_image_mode=False, max_num_hands=1,
                         min_detection_confidence=0.55, min_tracking_confidence=0.55)
    streaming, buf = False, ""
    try:
        conn.settimeout(0.05)
        while True:
            try:
                chunk = conn.recv(64).decode("utf-8",errors="ignore")
                if not chunk: break
                buf += chunk
                while "\n" in buf:
                    line,buf = buf.split("\n",1)
                    cmd = line.strip().upper()
                    if cmd == "START": streaming = True
                    elif cmd == "STOP":  streaming = False
                    elif cmd == "QUIT":  return
            except socket.timeout:
                pass
            except Exception:
                break

            if streaming:
                t0    = time.time()
                frame = _hub.get_frame() if _hub else None
                if frame is not None:
                    try:
                        conn.sendall((json.dumps(_pose(frame, hands))+"\n").encode())
                    except Exception:
                        break
                wait = _INTERVAL - (time.time()-t0)
                if wait > 0: time.sleep(wait)
            else:
                time.sleep(0.02)
    except Exception as e:
        print(f"[Hand] Error {addr}: {e}")
    finally:
        try: hands.close()
        except: pass
        try: conn.close()
        except: pass
        print(f"[Hand] Disconnected: {addr}")

def start(host="127.0.0.1", port=None):
    port = port or int(os.environ.get("HAND_TRACK_PORT","5004"))
    srv = socket.socket()
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host,port))
    srv.listen(5)
    print(f"[Hand] Listening on {host}:{port}")
    try:
        while True:
            c,a = srv.accept()
            threading.Thread(target=_handle,args=(c,a),daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
