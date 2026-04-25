"""
Face sign-in lobby (OpenCV). Run from face_auth_lobby_worker.py in a subprocess
so imshow runs on the process main thread (required on Windows).
"""
import csv
import os
import re
import sys
import time

import cv2
import face_recognition
import numpy as np

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PYTHON_ROOT_DIR = os.path.dirname(SCRIPT_DIR)
WORKSPACE_ROOT_DIR = os.path.dirname(PYTHON_ROOT_DIR)
USERS_CSV_PATH = os.path.join(WORKSPACE_ROOT_DIR, "C#", "content", "auth", "users.csv")
FACES_DIR = os.path.join(WORKSPACE_ROOT_DIR, "python", "data", "faces")
TOLERANCE = 0.60

_LOBBY_COUNTDOWN_S = 3
_LOBBY_TIMEOUT_S = 10
_LOBBY_BUFFER_S = 5
_LOBBY_COLOR_GOLD = (55, 175, 212)
_LOBBY_COLOR_GOLD_DIM = (70, 130, 140)
_LOBBY_COLOR_PAPYRUS = (200, 220, 240)
_LOBBY_COLOR_RED = (60, 60, 255)
_LOBBY_COLOR_CYAN = (255, 255, 0)
_LOBBY_COLOR_GREEN = (80, 210, 100)

_lobby_face_names = []
_lobby_face_encs = []


def _ensure_faces_dir():
    try:
        os.makedirs(FACES_DIR, exist_ok=True)
    except Exception:
        pass


def load_lobby_faces():
    global _lobby_face_names, _lobby_face_encs
    _lobby_face_names = []
    _lobby_face_encs = []
    if not os.path.isfile(USERS_CSV_PATH):
        return
    try:
        with open(USERS_CSV_PATH, "r", encoding="utf-8") as f:
            reader = csv.DictReader(f)
            for row in reader:
                user_id = (row.get("face_user_id") or "").strip()
                image_path = (row.get("face_image_path") or "").strip()
                if not user_id or not image_path:
                    continue
                abs_path = image_path
                if not os.path.isabs(abs_path):
                    abs_path = os.path.join(WORKSPACE_ROOT_DIR, image_path)
                if not os.path.isfile(abs_path):
                    continue
                try:
                    image = face_recognition.load_image_file(abs_path)
                    encodings = face_recognition.face_encodings(image)
                    if len(encodings) > 0:
                        _lobby_face_encs.append(encodings[0])
                        _lobby_face_names.append(user_id)
                except Exception as e:
                    print("Lobby: error loading face", user_id, e)
    except Exception as e:
        print("Lobby: CSV error", e)


def next_face_user_id():
    max_n = -1
    if os.path.isfile(USERS_CSV_PATH):
        try:
            with open(USERS_CSV_PATH, "r", encoding="utf-8") as f:
                reader = csv.DictReader(f)
                for row in reader:
                    uid = (row.get("face_user_id") or "").strip()
                    m = re.match(r"^user(\d+)$", uid, re.IGNORECASE)
                    if m:
                        max_n = max(max_n, int(m.group(1)))
        except Exception:
            pass
    return "user" + str(max_n + 1)


def _lobby_open_camera():
    index = int(os.environ.get("FACE_CAM_INDEX", "0"))
    if sys.platform == "win32":
        cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
    else:
        cap = cv2.VideoCapture(index)
    return cap


def _lobby_draw_face_guide(frame_bgr):
    height, width = frame_bgr.shape[:2]
    center_x, center_y = width // 2, height // 2
    oval_w, oval_h = 200, 250
    cv2.ellipse(
        frame_bgr,
        (center_x, center_y),
        (oval_w, oval_h),
        0,
        0,
        360,
        _LOBBY_COLOR_GOLD,
        2,
    )
    cv2.line(
        frame_bgr,
        (center_x - 20, center_y),
        (center_x + 20, center_y),
        _LOBBY_COLOR_GOLD_DIM,
        1,
    )
    cv2.line(
        frame_bgr,
        (center_x, center_y - 20),
        (center_x, center_y + 20),
        _LOBBY_COLOR_GOLD_DIM,
        1,
    )
    return frame_bgr


def _lobby_check_face_position(face_location, frame_shape):
    height, width = frame_shape[:2]
    top, right, bottom, left = face_location
    face_center_x = (left + right) // 2
    face_center_y = (top + bottom) // 2
    face_width = right - left
    frame_center_x = width // 2
    frame_center_y = height // 2
    x_offset = abs(face_center_x - frame_center_x)
    y_offset = abs(face_center_y - frame_center_y)
    is_centered = x_offset < width * 0.2 and y_offset < height * 0.2
    is_good_size = 0.15 < (face_width / float(width)) < 0.4
    feedback = []
    if not is_centered:
        feedback.append("Move to center")
    if not is_good_size:
        if face_width / float(width) < 0.15:
            feedback.append("Move closer")
        else:
            feedback.append("Move back")
    return is_centered and is_good_size, feedback


def _lobby_put_label(img, text, org, scale=0.85, color=None, thickness=2):
    if color is None:
        color = _LOBBY_COLOR_PAPYRUS
    cv2.putText(
        img,
        text,
        org,
        cv2.FONT_HERSHEY_SIMPLEX,
        scale,
        color,
        thickness,
        cv2.LINE_AA,
    )


def _lobby_save_new_face_crop(frame_bgr, face_location):
    _ensure_faces_dir()
    top, right, bottom, left = face_location
    h, w = frame_bgr.shape[:2]
    pad = 40
    top = max(0, int(top) - pad)
    left = max(0, int(left) - pad)
    bottom = min(h, int(bottom) + pad)
    right = min(w, int(right) + pad)
    crop = frame_bgr[top:bottom, left:right]
    if crop.size == 0:
        crop = frame_bgr
    new_id = next_face_user_id()
    rel_path = "python/data/faces/" + new_id + ".jpg"
    abs_path = os.path.join(WORKSPACE_ROOT_DIR, rel_path.replace("/", os.sep))
    if not cv2.imwrite(abs_path, crop):
        return None
    return new_id


def run_face_auth_lobby():
    """Returns FOUND:uid, NEW:uid, NOT_FOUND, CANCELLED, or ERROR:..."""
    try:
        load_lobby_faces()
        cap = _lobby_open_camera()
        if not cap.isOpened():
            return "ERROR:Could not open camera (try FACE_CAM_INDEX=1)"

        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

        win_title = "Smart Grand Egyptian Museum — Face sign-in"
        start_time = time.time()
        recognized_user = None
        countdown_started = False
        countdown_start_time = None
        face_stable_time = None
        capture_frame_bgr = None
        capture_face_location = None

        time_limit = _LOBBY_TIMEOUT_S + _LOBBY_COUNTDOWN_S + _LOBBY_BUFFER_S

        try:
            while True:
                ret, frame = cap.read()
                if not ret:
                    break

                frame = cv2.flip(frame, 1)
                display = frame.copy()
                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                small = cv2.resize(rgb_frame, (0, 0), fx=0.25, fy=0.25)
                face_locations = face_recognition.face_locations(small)
                face_locations = [
                    (t * 4, r * 4, b * 4, l * 4) for (t, r, b, l) in face_locations
                ]

                if len(face_locations) == 0:
                    _lobby_draw_face_guide(display)
                    _lobby_put_label(display, "No face detected", (40, 48), 1.0, _LOBBY_COLOR_RED)
                    _lobby_put_label(
                        display,
                        "Centre your face in the oval — arm's length, good light",
                        (40, 92),
                        0.65,
                        _LOBBY_COLOR_GOLD_DIM,
                        2,
                    )
                    face_stable_time = None
                    countdown_started = False

                elif len(face_locations) > 1:
                    _lobby_put_label(
                        display,
                        "Only one person at the camera, please",
                        (40, 48),
                        0.85,
                        _LOBBY_COLOR_RED,
                    )
                    face_stable_time = None
                    countdown_started = False

                else:
                    loc = face_locations[0]
                    top, right, bottom, left = loc
                    ok_pos, feedback = _lobby_check_face_position(loc, frame.shape)

                    if ok_pos:
                        cv2.rectangle(display, (left, top), (right, bottom), _LOBBY_COLOR_GREEN, 3)
                        if face_stable_time is None:
                            face_stable_time = time.time()
                        time_stable = time.time() - face_stable_time

                        if time_stable < 1.0:
                            _lobby_put_label(display, "Hold still...", (40, 48), 1.0, _LOBBY_COLOR_CYAN)
                        else:
                            if not countdown_started:
                                encs = face_recognition.face_encodings(rgb_frame, [loc])
                                if len(encs) > 0 and len(_lobby_face_encs) > 0:
                                    fe = encs[0]
                                    matches = face_recognition.compare_faces(
                                        _lobby_face_encs, fe, tolerance=TOLERANCE
                                    )
                                    dists = face_recognition.face_distance(_lobby_face_encs, fe)
                                    best = int(np.argmin(dists))
                                    if matches[best]:
                                        recognized_user = _lobby_face_names[best]
                                        break
                                countdown_started = True
                                countdown_start_time = time.time()

                            if countdown_started:
                                elapsed = time.time() - countdown_start_time
                                remaining = _LOBBY_COUNTDOWN_S - int(elapsed)
                                if remaining > 0:
                                    _lobby_put_label(
                                        display,
                                        "New guest — saving in %d..." % remaining,
                                        (40, 48),
                                        0.8,
                                        _LOBBY_COLOR_CYAN,
                                    )
                                    _lobby_put_label(
                                        display, "Stay still!", (40, 92), 0.75, _LOBBY_COLOR_PAPYRUS
                                    )
                                else:
                                    capture_frame_bgr = frame.copy()
                                    capture_face_location = loc
                                    break
                    else:
                        cv2.rectangle(display, (left, top), (right, bottom), _LOBBY_COLOR_CYAN, 3)
                        _lobby_draw_face_guide(display)
                        _lobby_put_label(display, "Adjust position:", (40, 44), 0.9, _LOBBY_COLOR_CYAN)
                        for i, msg in enumerate(feedback):
                            _lobby_put_label(
                                display, "  " + msg, (40, 84 + i * 36), 0.72, _LOBBY_COLOR_PAPYRUS
                            )
                        face_stable_time = None
                        countdown_started = False

                _lobby_put_label(
                    display,
                    "Q = cancel   |   Smart Grand Egyptian Museum",
                    (40, display.shape[0] - 36),
                    0.55,
                    _LOBBY_COLOR_GOLD_DIM,
                    1,
                )
                cv2.imshow(win_title, display)
                key = cv2.waitKey(1) & 0xFF
                if key == ord("q"):
                    return "CANCELLED"

                if time.time() - start_time > time_limit:
                    break
        finally:
            try:
                cap.release()
            except Exception:
                pass
            try:
                cv2.destroyAllWindows()
            except Exception:
                pass

        if recognized_user:
            return "FOUND:" + recognized_user
        if capture_frame_bgr is not None and capture_face_location is not None:
            new_id = _lobby_save_new_face_crop(capture_frame_bgr, capture_face_location)
            if new_id:
                return "NEW:" + new_id
            return "ERROR:Could not save face image"

        return "NOT_FOUND"
    except Exception as e:
        try:
            cv2.destroyAllWindows()
        except Exception:
            pass
        return "ERROR:" + str(e)


def run_face_auth_lobby_via_worker():
    """Run lobby in a child process so OpenCV windows work on Windows."""
    import subprocess

    worker = os.path.join(SCRIPT_DIR, "face_auth_lobby_worker.py")
    if not os.path.isfile(worker):
        return "ERROR:Missing face_auth_lobby_worker.py"
    try:
        proc = subprocess.run(
            [sys.executable, worker],
            cwd=SCRIPT_DIR,
            capture_output=True,
            text=True,
            timeout=180,
        )
    except subprocess.TimeoutExpired:
        return "ERROR:Face sign-in took too long — try again."

    err_txt = (proc.stderr or "").strip()
    if err_txt:
        print("[face_auth_lobby]", err_txt)

    out_lines = [ln.strip() for ln in (proc.stdout or "").splitlines() if ln.strip()]
    if not out_lines:
        msg = err_txt or "The camera window did not return a result."
        return "ERROR:" + msg

    last = out_lines[-1]
    if last.startswith(("ERROR:", "FOUND:", "NEW:")) or last in ("NOT_FOUND", "CANCELLED"):
        return last
    if proc.returncode != 0:
        return "ERROR:" + (err_txt or last or "Camera step failed")
    return last
