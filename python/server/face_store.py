"""
face_store.py — Load, cache, and match face encodings from users.csv.

Single source of truth for face data. Used by both face_auth_service
and the face lobby UI.
"""

import csv
import os
import re
from typing import List, Optional, Tuple

import cv2
import face_recognition
import numpy as np

_SCRIPT_DIR  = os.path.dirname(os.path.abspath(__file__))
_SERVER_DIR  = _SCRIPT_DIR
_PYTHON_ROOT = os.path.dirname(_SERVER_DIR)
_WORKSPACE   = os.path.dirname(_PYTHON_ROOT)

USERS_CSV  = os.path.join(_WORKSPACE, "C#", "content", "auth", "users.csv")
FACES_DIR  = os.path.join(_WORKSPACE, "python", "data", "faces")
TOLERANCE  = 0.60

_names:     List[str]       = []
_encodings: List[np.ndarray] = []


def ensure_faces_dir():
    os.makedirs(FACES_DIR, exist_ok=True)


def load():
    """Load all known face encodings from users.csv into memory."""
    global _names, _encodings
    _names, _encodings = [], []
    if not os.path.isfile(USERS_CSV):
        return
    try:
        with open(USERS_CSV, "r", encoding="utf-8") as f:
            for row in csv.DictReader(f):
                uid  = (row.get("face_user_id") or "").strip()
                path = (row.get("face_image_path") or "").strip()
                if not uid or not path:
                    continue
                abs_path = path if os.path.isabs(path) else os.path.join(_WORKSPACE, path)
                if not os.path.isfile(abs_path):
                    continue
                try:
                    img  = face_recognition.load_image_file(abs_path)
                    encs = face_recognition.face_encodings(img)
                    if encs:
                        _encodings.append(encs[0])
                        _names.append(uid)
                except Exception as e:
                    print(f"[FaceStore] Error loading {uid}: {e}")
    except Exception as e:
        print(f"[FaceStore] CSV error: {e}")
    print(f"[FaceStore] Loaded {len(_names)} faces")


def match(encoding: np.ndarray) -> Optional[str]:
    """Return user_id if encoding matches a known face, else None."""
    if not _encodings:
        return None
    matches   = face_recognition.compare_faces(_encodings, encoding, tolerance=TOLERANCE)
    distances = face_recognition.face_distance(_encodings, encoding)
    best      = int(np.argmin(distances))
    return _names[best] if matches[best] else None


def next_user_id() -> str:
    """Return the next available userN id."""
    max_n = -1
    if os.path.isfile(USERS_CSV):
        try:
            with open(USERS_CSV, "r", encoding="utf-8") as f:
                for row in csv.DictReader(f):
                    uid = (row.get("face_user_id") or "").strip()
                    m = re.match(r"^user(\d+)$", uid, re.IGNORECASE)
                    if m:
                        max_n = max(max_n, int(m.group(1)))
        except Exception:
            pass
    return f"user{max_n + 1}"


def save_face_crop(frame_bgr: np.ndarray, face_location: Tuple, user_id: str) -> str:
    """Crop and save face image. Returns absolute path."""
    ensure_faces_dir()
    top, right, bottom, left = face_location
    h, w = frame_bgr.shape[:2]
    pad = 40
    top    = max(0, int(top)    - pad)
    left   = max(0, int(left)   - pad)
    bottom = min(h, int(bottom) + pad)
    right  = min(w, int(right)  + pad)
    crop = frame_bgr[top:bottom, left:right]
    if crop.size == 0:
        crop = frame_bgr
    rel  = f"python/data/faces/{user_id}.jpg"
    path = os.path.join(_WORKSPACE, rel.replace("/", os.sep))
    cv2.imwrite(path, crop)
    return rel  # relative path stored in CSV
