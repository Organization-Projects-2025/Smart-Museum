"""
Real-time hand tracker using MediaPipe Hands.
Streams normalized hand pose as newline-delimited JSON over TCP.

Default port: 5004 (configurable with HAND_TRACK_PORT).

Wire protocol (one side per line):
  C# → service  : "START\n"  — begin streaming pose frames
  C# → service  : "STOP\n"   — pause streaming (connection stays open)
  C# → service  : "QUIT\n"   — close session
  service → C#  : JSON frame + "\n"  at ~30 fps

Pose frame fields:
  valid  (bool)  — false when no hand detected
  wx     (float) — wrist X, 0=left 1=right (raw camera, may be mirrored)
  wy     (float) — wrist Y, 0=bottom 1=top  (flipped from MediaPipe's image-y)
  wz     (float) — estimated depth 0=very close (large hand) 1=far (small)
  fist   (bool)  — true when all fingers appear curled (grab gesture)
"""

import os
import sys
import socket
import threading
import time
import json
import math

# Resolve sibling dollarpy-service path so mediapipe_compat is importable.
_SERVER_DIR = os.path.dirname(os.path.abspath(__file__))
_DOLLARPY_DIR = os.path.normpath(
    os.path.join(os.path.dirname(os.path.dirname(_SERVER_DIR)), "dollarpy-service")
)
if os.path.isdir(_DOLLARPY_DIR) and _DOLLARPY_DIR not in sys.path:
    sys.path.insert(0, _DOLLARPY_DIR)

try:
    import mediapipe_compat as mp
    _mp_hands_module = mp.solutions.hands
except Exception as _e:
    print(f"[HandTracker] WARNING: mediapipe_compat import failed ({_e}). "
          "Install mediapipe and check dollarpy-service/mediapipe_compat.py.")
    _mp_hands_module = None

import cv2

HOST = "127.0.0.1"
PORT = int(os.environ.get("HAND_TRACK_PORT", "5004"))
TARGET_FPS = 30
FRAME_INTERVAL = 1.0 / TARGET_FPS

# Palm size (wrist→middle-MCP distance in normalised coords) at "neutral" depth.
# Adjust if your webcam / mounting distance differs.
REF_PALM_SIZE = 0.32


# ---------------------------------------------------------------------------
# MediaPipe helpers
# ---------------------------------------------------------------------------

def _palm_size(lm):
    """Distance wrist (0) → middle-MCP (9) in normalized image space."""
    dx = lm[9].x - lm[0].x
    dy = lm[9].y - lm[0].y
    return math.sqrt(dx * dx + dy * dy)


def _is_fist(lm):
    """True when ≥3 of 4 fingers have their tip below their PIP joint (y↓)."""
    pairs = [(8, 6), (12, 10), (16, 14), (20, 18)]
    return sum(1 for tip, pip in pairs if lm[tip].y > lm[pip].y) >= 3


def _build_pose(frame_bgr, hands_model):
    rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
    results = hands_model.process(rgb)
    if not results.multi_hand_landmarks:
        return {"valid": False, "wx": 0.5, "wy": 0.5, "wz": 0.5, "fist": False}

    lm = results.multi_hand_landmarks[0].landmark
    ps = _palm_size(lm)
    # Smaller palm → farther away → higher wz
    wz = max(0.0, min(1.0, 1.0 - (ps / REF_PALM_SIZE) * 0.85))
    return {
        "valid": True,
        "wx": round(float(lm[0].x), 4),
        "wy": round(float(1.0 - lm[0].y), 4),   # flip so 0=bottom, 1=top
        "wz": round(wz, 4),
        "fist": _is_fist(lm),
    }


# ---------------------------------------------------------------------------
# Per-client session
# ---------------------------------------------------------------------------

def _handle_client(conn, addr):
    print(f"[HandTracker] Client {addr} connected")
    cap = None
    streaming = False

    if _mp_hands_module is None:
        try:
            conn.sendall(
                json.dumps({"error": "mediapipe_unavailable"}).encode() + b"\n"
            )
        except Exception:
            pass
        conn.close()
        return

    hands = _mp_hands_module.Hands(
        static_image_mode=False,
        max_num_hands=1,
        min_detection_confidence=0.55,
        min_tracking_confidence=0.55,
    )

    try:
        cap = cv2.VideoCapture(0)
        if not cap.isOpened():
            conn.sendall(
                json.dumps({"error": "camera_unavailable"}).encode() + b"\n"
            )
            return

        buf = ""
        conn.settimeout(0.05)

        while True:
            # --- read any pending commands ---
            try:
                chunk = conn.recv(64).decode("utf-8", errors="ignore")
                if not chunk:
                    break
                buf += chunk
                while "\n" in buf:
                    line, buf = buf.split("\n", 1)
                    cmd = line.strip().upper()
                    if cmd == "START":
                        streaming = True
                    elif cmd == "STOP":
                        streaming = False
                    elif cmd == "QUIT":
                        return
            except socket.timeout:
                pass
            except Exception:
                break

            if streaming:
                t0 = time.time()
                ret, frame = cap.read()
                if ret:
                    pose = _build_pose(frame, hands)
                    msg = json.dumps(pose) + "\n"
                    try:
                        conn.sendall(msg.encode())
                    except Exception:
                        break
                elapsed = time.time() - t0
                sleep_t = FRAME_INTERVAL - elapsed
                if sleep_t > 0:
                    time.sleep(sleep_t)
            else:
                time.sleep(0.02)

    except Exception as exc:
        print(f"[HandTracker] Session error for {addr}: {exc}")
    finally:
        hands.close()
        if cap:
            cap.release()
        try:
            conn.close()
        except Exception:
            pass
        print(f"[HandTracker] Client {addr} disconnected")


# ---------------------------------------------------------------------------
# Server entry point
# ---------------------------------------------------------------------------

def start_server(host=HOST, port=PORT):
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        srv.bind((host, port))
    except OSError as e:
        if "10048" in str(e) or "already in use" in str(e).lower():
            print(
                f"[HandTracker] Port {port} is already in use. "
                "Kill the existing process or change PORT."
            )
        raise
    srv.listen(5)
    print(f"[HandTracker] Listening on {host}:{port}  (30 fps JSON pose stream)")

    try:
        while True:
            conn, addr = srv.accept()
            t = threading.Thread(
                target=_handle_client, args=(conn, addr), daemon=True
            )
            t.start()
    except KeyboardInterrupt:
        print("\n[HandTracker] Stopping.")
    finally:
        srv.close()


if __name__ == "__main__":
    _port = int(os.environ.get("HAND_TRACKER_PORT", str(PORT)))
    start_server(HOST, _port)
