"""
gaze_emotion_service.py — TCP server on port 5002.
Streams gaze + emotion JSON to C#.

Wire protocol:
  PING   → {"status":"ok"}
  STREAM → {"status":"ok"} then continuous JSON lines
  PAUSE  → {"status":"ok"}
  QUIT   → {"status":"bye"}

Frame: {"ok":true,"t_ms":int,"gx":float,"gy":float,
        "dominant":str,"emotions":{"angry":float,...}}
"""

import json, os, select, socket, sys, threading, time, urllib.request
import cv2
import numpy as np

# ── MediaPipe (gaze) ──────────────────────────────────────────────────────────
_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_MODEL_PATH = os.path.join(_SCRIPT_DIR, "face_landmarker.task")
_MODEL_URL  = ("https://storage.googleapis.com/mediapipe-models/face_landmarker/"
               "face_landmarker/float16/1/face_landmarker.task")

try:
    import mediapipe as mp
    from mediapipe.tasks import python as _mpt
    from mediapipe.tasks.python import vision as _mpv
    _MP_OK = True
except Exception as e:
    _MP_OK = False
    print(f"[GazeEmotion] mediapipe unavailable: {e}")

# ── HSEmotion (emotion) ───────────────────────────────────────────────────────
try:
    from hsemotion_onnx.facial_emotions import HSEmotionRecognizer as _HSE
    _emotion_rec = _HSE(model_name="enet_b0_8_best_afew")
    _HSE_OK = True
    print("[GazeEmotion] HSEmotion ready")
except Exception as e:
    _HSE_OK = False
    _emotion_rec = None
    print(f"[GazeEmotion] HSEmotion unavailable: {e}")

# HSEmotion outputs 8 classes — map to our 7 standard emotions
_HSE_LABELS = ["Anger","Contempt","Disgust","Fear","Happiness","Neutral","Sadness","Surprise"]
_HSE_TO_STD = {
    "Anger":     "angry",
    "Contempt":  "disgust",  # merge into disgust
    "Disgust":   "disgust",
    "Fear":      "fear",
    "Happiness": "happy",
    "Neutral":   "neutral",
    "Sadness":   "sad",
    "Surprise":  "surprise",
}
_EMOTIONS = ("angry","disgust","fear","happy","sad","surprise","neutral")

# Temporal smoothing — rolling average over last N results
_SMOOTH_WINDOW   = 6
_emotion_history: list = []

# ── Landmark indices ──────────────────────────────────────────────────────────
_LE_OUT,_LE_IN   = 33, 133
_RE_IN, _RE_OUT  = 362, 263
_NOSE,_FORE,_CHIN= 1,  10, 152
_L_IRIS,_R_IRIS  = 468, 473

# ── Hub ───────────────────────────────────────────────────────────────────────
_hub = None
def set_hub(hub):
    global _hub
    _hub = hub

# ── Helpers ───────────────────────────────────────────────────────────────────
def _ensure_model():
    if os.path.isfile(_MODEL_PATH) and os.path.getsize(_MODEL_PATH) > 1000:
        return _MODEL_PATH
    print("[GazeEmotion] Downloading face_landmarker.task ...")
    urllib.request.urlretrieve(_MODEL_URL, _MODEL_PATH)
    return _MODEL_PATH

class _LM:
    __slots__ = ("landmark",)
    def __init__(self, lms): self.landmark = lms

def _pt(lms, i, w, h):
    p = lms.landmark[i]
    return np.array([p.x*w, p.y*h], dtype=np.float64)

def _gaze(lms, w, h):
    le_o,le_i = _pt(lms,_LE_OUT,w,h), _pt(lms,_LE_IN,w,h)
    re_i,re_o = _pt(lms,_RE_IN, w,h), _pt(lms,_RE_OUT,w,h)
    li,  ri   = _pt(lms,_L_IRIS,w,h), _pt(lms,_R_IRIS,w,h)
    lw = max(1e-6, np.linalg.norm(le_o-le_i))
    rw = max(1e-6, np.linalg.norm(re_o-re_i))
    ox = ((li[0]-(le_o+le_i)[0]*.5)/lw + (ri[0]-(re_o+re_i)[0]*.5)/rw)*.5
    oy = ((li[1]-(le_o+le_i)[1]*.5)/lw + (ri[1]-(re_o+re_i)[1]*.5)/rw)*.5
    nose,fore,chin = _pt(lms,_NOSE,w,h),_pt(lms,_FORE,w,h),_pt(lms,_CHIN,w,h)
    fh    = max(1e-6, np.linalg.norm(fore-chin))
    pitch = (nose[1]-(fore[1]+chin[1])*.5)/fh
    yaw   = (nose[0]-(le_o[0]+re_o[0])*.5)/max(1e-6,abs(re_o[0]-le_o[0]))
    gx = float(np.clip(0.5+0.55*ox+0.35*yaw,  0,1))
    gy = float(np.clip(0.5+0.55*oy+0.25*pitch, 0,1))
    return gx, gy

def _emotion(frame_bgr, face_bbox):
    """Crop face from frame and run HSEmotion. Returns smoothed emotion dict or None."""
    global _emotion_history
    if not _HSE_OK or _emotion_rec is None:
        return None
    try:
        # Crop face with padding
        top, right, bottom, left = face_bbox
        fh, fw = frame_bgr.shape[:2]
        pad = 20
        crop = frame_bgr[max(0,top-pad):min(fh,bottom+pad),
                         max(0,left-pad):min(fw,right+pad)]
        if crop.size == 0:
            crop = frame_bgr

        _, scores = _emotion_rec.predict_emotions(crop, logits=False)
        total = sum(scores) or 1.0

        # Accumulate scores into 7 standard emotions (Contempt → disgust)
        raw: dict = {}
        for label, score in zip(_HSE_LABELS, scores):
            key = _HSE_TO_STD[label]
            raw[key] = raw.get(key, 0.0) + float(score) / total  # float() converts numpy float32
        emo = {k: float(raw.get(k, 0.0)) for k in _EMOTIONS}  # ensure all plain Python floats

        # Normalise
        t = sum(emo.values()) or 1.0
        emo = {k: v/t for k, v in emo.items()}

        # Temporal smoothing
        _emotion_history.append(emo)
        if len(_emotion_history) > _SMOOTH_WINDOW:
            _emotion_history.pop(0)
        smoothed = {k: float(sum(e[k] for e in _emotion_history) / len(_emotion_history))
                    for k in _EMOTIONS}
        t2 = sum(smoothed.values()) or 1.0
        smoothed = {k: float(v/t2) for k, v in smoothed.items()}

        return {"dominant": max(smoothed, key=smoothed.get), "emotions": smoothed}
    except Exception as e:
        print(f"[GazeEmotion] HSEmotion error: {e}")
        return None

# ── Background loop ───────────────────────────────────────────────────────────
class _Loop:
    def __init__(self):
        self.lock      = threading.Lock()
        self.latest    = None
        self._active   = False
        self._thread   = None
        self._det      = None
        self._fi       = 0
        self._t0       = None
        self._last_emo = None

    def start(self):
        with self.lock:
            if self._active and self._thread and self._thread.is_alive():
                return
            self._active   = True
            self._t0       = time.time()*1000.0
            self._fi       = 0
            self._last_emo = None
            self._thread   = threading.Thread(target=self._body, daemon=True)
            self._thread.start()

    def stop(self):
        with self.lock:
            self._active = False

    def _body(self):
        if not _MP_OK:
            with self.lock:
                self.latest = {"ok":False,"t_ms":0,"reason":"mediapipe_missing"}
            return
        try:
            base = _mpt.BaseOptions(model_asset_path=_ensure_model())
            opts = _mpv.FaceLandmarkerOptions(
                base_options=base, running_mode=_mpv.RunningMode.IMAGE,
                num_faces=1, min_face_detection_confidence=0.3,
                min_face_presence_confidence=0.3, min_tracking_confidence=0.3,
            )
            self._det = _mpv.FaceLandmarker.create_from_options(opts)
            print("[GazeEmotion] FaceLandmarker ready")
        except Exception as e:
            print(f"[GazeEmotion] Init failed: {e}")
            with self.lock:
                self.latest = {"ok":False,"t_ms":0,"reason":str(e)}
            return

        while True:
            with self.lock:
                if not self._active: break
            frame = _hub.get_frame() if _hub else None
            if frame is None:
                time.sleep(0.012)
                continue

            self._fi += 1
            t_ms = int(time.time()*1000.0 - self._t0)
            h, w = frame.shape[:2]

            # Gaze via MediaPipe
            rgb    = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            mp_img = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            res    = self._det.detect(mp_img)
            if not res.face_landmarks:
                if self._fi % 150 == 1:
                    print(f"[GazeEmotion] No face (frame {self._fi})")
                with self.lock:
                    self.latest = {"ok":False,"t_ms":t_ms,"reason":"no_face"}
                continue

            lms = _LM(list(res.face_landmarks[0]))
            gx, gy = _gaze(lms, w, h)

            # Face bbox from landmarks
            xs = [lm.x*w for lm in lms.landmark]
            ys = [lm.y*h for lm in lms.landmark]
            bbox = (int(min(ys)), int(max(xs)), int(max(ys)), int(min(xs)))

            # Emotion via HSEmotion — run every 4th frame, but always on frame 1
            if self._fi == 1 or self._fi % 4 == 0:
                emo = _emotion(frame, bbox)
                if emo:
                    self._last_emo = emo

            # If emotion not ready yet, still send gaze with neutral placeholder
            if self._last_emo is None:
                neutral = {k: (1.0/7.0) for k in _EMOTIONS}
                self._last_emo = {"dominant": "neutral", "emotions": neutral}

            if self._fi <= 4 or self._fi % 300 == 0:
                print(f"[GazeEmotion] frame={self._fi} dominant={self._last_emo['dominant']} gx={gx:.2f} gy={gy:.2f}")

            with self.lock:
                self.latest = {
                    "ok": True, "t_ms": t_ms,
                    "gx": float(gx), "gy": float(gy),
                    "dominant": self._last_emo["dominant"],
                    "emotions": self._last_emo["emotions"],
                }

        if self._det:
            try: self._det.close()
            except: pass

_loop = _Loop()

# ── TCP ───────────────────────────────────────────────────────────────────────
def _drain(conn, buf):
    try:
        r,_,_ = select.select([conn],[],[],0)
    except (ValueError,OSError): return buf, None
    if not r: return buf, None
    try: chunk = conn.recv(4096)
    except (BlockingIOError,ConnectionError,OSError): return buf, None
    if not chunk: return buf, "__CLOSED__"
    buf += chunk
    last = None
    while b"\n" in buf:
        line, buf = buf.split(b"\n",1)
        s = line.decode("utf-8",errors="ignore").strip().upper()
        if s: last = s
    return buf, last

def _handle(conn, addr):
    print(f"[GazeEmotion] Client: {addr}")
    streaming, buf = False, b""
    try:
        conn.setblocking(True)
        while True:
            if not streaming:
                chunk = conn.recv(4096)
                if not chunk: break
                buf += chunk
                while b"\n" in buf:
                    line, buf = buf.split(b"\n",1)
                    cmd = line.decode("utf-8",errors="ignore").strip().upper()
                    if not cmd: continue
                    if cmd == "PING":
                        conn.sendall((json.dumps({"status":"ok"})+"\n").encode())
                    elif cmd == "STREAM":
                        streaming = True
                        _loop.start()
                        conn.sendall((json.dumps({"status":"ok"})+"\n").encode())
                        conn.setblocking(False)
                    elif cmd in ("PAUSE","QUIT"):
                        conn.sendall((json.dumps({"status":"ok" if cmd=="PAUSE" else "bye"})+"\n").encode())
                        if cmd == "QUIT": return
            else:
                buf, cmd = _drain(conn, buf)
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
                with _loop.lock:
                    snap = dict(_loop.latest) if _loop.latest else {"ok":False,"t_ms":0,"reason":"warmup"}
                try:
                    conn.sendall((json.dumps(snap)+"\n").encode())
                except (BrokenPipeError,ConnectionError,OSError):
                    break
                time.sleep(0.066)
    except Exception as e:
        print(f"[GazeEmotion] Error {addr}: {e}")
    finally:
        try: conn.close()
        except: pass
        print(f"[GazeEmotion] Disconnected: {addr}")

def start(host="127.0.0.1", port=5002):
    srv = socket.socket()
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(8)
    print(f"[GazeEmotion] Listening on {host}:{port}")
    try:
        while True:
            c, a = srv.accept()
            threading.Thread(target=_handle, args=(c,a), daemon=True).start()
    except KeyboardInterrupt:
        pass
    finally:
        srv.close()
        _loop.stop()
