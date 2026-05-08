"""
Development/test_gaze.py
========================
Live gaze tracking tester — shows webcam feed with a dot where you're looking.
Press Q to quit.

Usage:
    python Development/test_gaze.py
    python Development/test_gaze.py --camera 0
"""

import argparse
import os
import sys
import urllib.request

import cv2
import numpy as np

# ── MediaPipe setup ───────────────────────────────────────────────────────────
try:
    import mediapipe as mp
    from mediapipe.tasks import python as _mpt
    from mediapipe.tasks.python import vision as _mpv
    _MP_OK = True
except Exception as e:
    print(f"ERROR: mediapipe not available: {e}")
    sys.exit(1)

_MODEL_PATH = os.path.join(os.path.dirname(__file__), "..", "python", "server", "face_landmarker.task")
_MODEL_URL  = ("https://storage.googleapis.com/mediapipe-models/face_landmarker/"
               "face_landmarker/float16/1/face_landmarker.task")

def _ensure_model():
    if os.path.isfile(_MODEL_PATH) and os.path.getsize(_MODEL_PATH) > 1000:
        return _MODEL_PATH
    print("Downloading face_landmarker.task ...")
    urllib.request.urlretrieve(_MODEL_URL, _MODEL_PATH)
    return _MODEL_PATH

# Landmark indices
_LE_OUT, _LE_IN  = 33,  133
_RE_IN,  _RE_OUT = 362, 263
_NOSE, _FORE, _CHIN = 1, 10, 152
_L_IRIS, _R_IRIS    = 468, 473

def _pt(lms, i, w, h):
    p = lms[i]
    return np.array([p.x * w, p.y * h], dtype=np.float64)

def _gaze(lms, w, h):
    le_o, le_i = _pt(lms, _LE_OUT, w, h), _pt(lms, _LE_IN, w, h)
    re_i, re_o = _pt(lms, _RE_IN,  w, h), _pt(lms, _RE_OUT, w, h)
    li,   ri   = _pt(lms, _L_IRIS, w, h), _pt(lms, _R_IRIS, w, h)

    lw = max(1e-6, np.linalg.norm(le_o - le_i))
    rw = max(1e-6, np.linalg.norm(re_o - re_i))

    # Iris offset relative to eye centre
    off_x = ((li[0] - (le_o + le_i)[0] * .5) / lw +
             (ri[0] - (re_o + re_i)[0] * .5) / rw) * .5
    off_y = ((li[1] - (le_o + le_i)[1] * .5) / lw +
             (ri[1] - (re_o + re_i)[1] * .5) / rw) * .5

    # Head pose (yaw / pitch)
    nose  = _pt(lms, _NOSE, w, h)
    fore  = _pt(lms, _FORE, w, h)
    chin  = _pt(lms, _CHIN, w, h)
    fh    = max(1e-6, np.linalg.norm(fore - chin))
    pitch = (nose[1] - (fore[1] + chin[1]) * .5) / fh
    yaw   = (nose[0] - (le_o[0] + re_o[0]) * .5) / max(1e-6, abs(re_o[0] - le_o[0]))

    gx = float(np.clip(0.5 - 5.0 * off_x - 2.0 * yaw,  0, 1))
    pitch_scale = 20.0 if pitch > 0 else 25.0
    gy = float(np.clip(0.5 + 15.0 * off_y + pitch_scale * pitch, 0, 1))
    return gx, gy, off_x, off_y, yaw, pitch


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--camera", type=int,
                        default=int(os.environ.get("MUSEUM_CAMERA", "0")))
    args = parser.parse_args()

    # Build detector
    base = _mpt.BaseOptions(model_asset_path=_ensure_model())
    opts = _mpv.FaceLandmarkerOptions(
        base_options=base,
        running_mode=_mpv.RunningMode.IMAGE,
        num_faces=1,
        min_face_detection_confidence=0.3,
        min_face_presence_confidence=0.3,
        min_tracking_confidence=0.3,
    )
    det = _mpv.FaceLandmarker.create_from_options(opts)
    print(f"FaceLandmarker ready. Camera {args.camera} — press Q to quit")

    cap = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"ERROR: Cannot open camera {args.camera}")
        sys.exit(1)

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

    while True:
        ok, frame = cap.read()
        if not ok:
            continue

        h, w = frame.shape[:2]
        display = frame.copy()

        rgb    = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_img = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        res    = det.detect(mp_img)

        if not res.face_landmarks:
            cv2.putText(display, "No face detected", (20, 40),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 0, 200), 2)
        else:
            lms = res.face_landmarks[0]
            gx, gy, off_x, off_y, yaw, pitch = _gaze(lms, w, h)

            # Draw gaze dot
            px = int(gx * w)
            py = int(gy * h)
            cv2.circle(display, (px, py), 18, (0, 255, 0), 3)
            cv2.circle(display, (px, py), 5,  (0, 0, 255), -1)
            cv2.line(display, (px - 25, py), (px + 25, py), (255, 255, 255), 1)
            cv2.line(display, (px, py - 25), (px, py + 25), (255, 255, 255), 1)

            # Draw iris landmarks
            for idx in [_L_IRIS, _R_IRIS]:
                p = lms[idx]
                ix, iy = int(p.x * w), int(p.y * h)
                cv2.circle(display, (ix, iy), 4, (0, 255, 255), -1)

            # Debug info
            lines = [
                f"gx={gx:.3f}  gy={gy:.3f}",
                f"iris_off_x={off_x:.3f}  iris_off_y={off_y:.3f}",
                f"yaw={yaw:.3f}  pitch={pitch:.3f}",
            ]
            for i, line in enumerate(lines):
                cv2.putText(display, line, (10, 30 + i * 28),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.65, (200, 255, 200), 1)

        cv2.imshow("Gaze Test — press Q to quit", display)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    cap.release()
    det.close()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
