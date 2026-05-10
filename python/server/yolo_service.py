"""
yolo_server.py — TCP server on port 5003.
Streams YOLOv8 object tracking JSON to C#.

Wire protocol: PING / STREAM / PAUSE / QUIT
Frame: {"ok":true,"t_ms":int,"tracks":[{"id":int,"cls":str,"cx":float,"cy":float,"w":float,"h":float,"conf":float},...]}
"""

import json
import os
import select
import socket
import threading
import time

_hub  = None
_t0   = None
_mock = os.environ.get("YOLO_CONTEXT_MOCK","").strip() in ("1","true","yes")

def set_hub(hub):
    global _hub
    _hub = hub

# ── YOLO model ────────────────────────────────────────────────────────────────
_tracker = None
if not _mock:
    try:
        from ultralytics import YOLO
        _tracker = YOLO("yolov8n.pt")
        print("[YOLO] Model loaded")
    except Exception as e:
        print(f"[YOLO] ultralytics unavailable ({e}) — using mock mode")
        _mock = True

# ── Mock tracks ───────────────────────────────────────────────────────────────
def _mock_tracks(phase):
    phase = phase % 6
    if phase == 0: return [{"id":1,"cls":"person",    "cx":0.5,"cy":0.45,"w":0.35,"h":0.6, "conf":0.88}]
    if phase == 1: return [{"id":2,"cls":"cell phone","cx":0.7,"cy":0.6, "w":0.08,"h":0.14,"conf":0.77}]
    if phase == 2: return [{"id":1,"cls":"person",    "cx":0.5,"cy":0.5, "w":0.2, "h":0.35,"conf":0.72},
                           {"id":3,"cls":"book",      "cx":0.25,"cy":0.55,"w":0.12,"h":0.1,"conf":0.68}]
    if phase == 3: return [{"id":4,"cls":"laptop",    "cx":0.4,"cy":0.5, "w":0.22,"h":0.12,"conf":0.66}]
    if phase == 4: return []
    return [{"id":5,"cls":"person","cx":0.5,"cy":0.5,"w":0.12,"h":0.22,"conf":0.61}]

def _real_tracks(frame):
    tracks = []
    for result in _tracker.track(source=frame, stream=True, persist=True, verbose=False):
        boxes = getattr(result,"boxes",None)
        if boxes is None or len(boxes) == 0: break
        xywhn,cls_a,conf_a = boxes.xywhn.cpu().numpy(),boxes.cls.cpu().numpy(),boxes.conf.cpu().numpy()
        id_t  = boxes.id
        id_a  = id_t.cpu().numpy() if id_t is not None else None
        names = result.names or {}
        for j in range(len(xywhn)):
            cx,cy,w,h = float(xywhn[j][0]),float(xywhn[j][1]),float(xywhn[j][2]),float(xywhn[j][3])
            tracks.append({"id": int(id_a[j]) if id_a is not None else j,
                           "cls": str(names.get(int(cls_a[j]),int(cls_a[j]))),
                           "cx":cx,"cy":cy,"w":w,"h":h,"conf":float(conf_a[j])})
        break
    return tracks

def _frame_json(tracks):
    global _t0
    if _t0 is None: _t0 = time.time()*1000.0
    return json.dumps({"ok":True,"t_ms":int(time.time()*1000-_t0),"tracks":tracks})

# ── TCP ───────────────────────────────────────────────────────────────────────
def _drain(conn, buf):
    try:
        r,_,_ = select.select([conn],[],[],0)
    except (ValueError,OSError): return buf,None
    if not r: return buf,None
    try: chunk = conn.recv(4096)
    except (BlockingIOError,ConnectionError,OSError): return buf,None
    if not chunk: return buf,"__CLOSED__"
    buf += chunk
    last = None
    while b"\n" in buf:
        line,buf = buf.split(b"\n",1)
        s = line.decode("utf-8",errors="ignore").strip().upper()
        if s: last = s
    return buf,last

def _handle(conn, addr):
    print(f"[YOLO] Client: {addr}")
    streaming,buf,mock_i = False,b"",0
    try:
        conn.setblocking(True)
        while True:
            if not streaming:
                data = conn.recv(4096)
                if not data: break
                buf += data
                while b"\n" in buf:
                    line,buf = buf.split(b"\n",1)
                    cmd = line.decode("utf-8",errors="ignore").strip().upper()
                    if not cmd: continue
                    if cmd == "PING":
                        conn.sendall((json.dumps({"status":"ok"})+"\n").encode())
                    elif cmd == "STREAM":
                        streaming = True
                        conn.sendall((json.dumps({"status":"ok"})+"\n").encode())
                        conn.setblocking(False)
                    elif cmd in ("PAUSE","QUIT"):
                        conn.sendall((json.dumps({"status":"ok" if cmd=="PAUSE" else "bye"})+"\n").encode())
                        if cmd == "QUIT": return
            else:
                buf,cmd = _drain(conn,buf)
                if cmd == "__CLOSED__": break
                if cmd == "PAUSE":
                    streaming = False
                    conn.setblocking(True)
                    conn.sendall((json.dumps({"status":"ok"})+"\n").encode())
                    continue
                if cmd == "QUIT":
                    conn.setblocking(True)
                    conn.sendall((json.dumps({"status":"bye"})+"\n").encode())
                    return
                if _mock or _tracker is None:
                    tracks = _mock_tracks(mock_i); mock_i += 1
                else:
                    frame = _hub.get_frame() if _hub else None
                    if frame is not None:
                        try:
                            tracks = _real_tracks(frame)
                        except Exception as e:
                            print(f"[YOLO] Inference error, switching to mock: {e}")
                            _mock = True
                            tracks = _mock_tracks(mock_i); mock_i += 1
                    else:
                        tracks = []
                try:
                    conn.sendall((_frame_json(tracks)+"\n").encode())
                except (BrokenPipeError,ConnectionError,OSError): break
                time.sleep(0.18)
    except Exception as e:
        print(f"[YOLO] Error {addr}: {e}")
    finally:
        try: conn.close()
        except: pass
        print(f"[YOLO] Disconnected: {addr}")

def start(host="127.0.0.1", port=5003):
    srv = socket.socket()
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host,port))
    srv.listen(8)
    print(f"[YOLO] Listening on {host}:{port} [{'MOCK' if _mock else 'REAL'}]")
    try:
        while True:
            c,a = srv.accept()
            threading.Thread(target=_handle,args=(c,a),daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
