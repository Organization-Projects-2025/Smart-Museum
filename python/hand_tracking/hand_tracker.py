import cv2
import json
import math
import os
import socket
import threading
import time
import mediapipe as mp
from mediapipe.tasks.python import vision, BaseOptions
from mediapipe.tasks.python.vision import (
    HandLandmarker, HandLandmarkerOptions, RunningMode, HandLandmarksConnections
)

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "hand_landmarker.task")

# ── Socket config ─────────────────────────────────────────────────────────────
SOCKET_HOST = "127.0.0.1"
SOCKET_PORT = 5555

FINGER_NAMES = ["thumb", "index", "middle", "ring", "pinky"]

FINGER_LANDMARKS = [
    (4,  3,  2,  1),   # thumb
    (8,  7,  6,  5),   # index
    (12, 11, 10, 9),   # middle
    (16, 15, 14, 13),  # ring
    (20, 19, 18, 17),  # pinky
]

HAND_CONNECTIONS = [(c.start, c.end) for c in HandLandmarksConnections.HAND_CONNECTIONS]


# ── Socket server (non-blocking, broadcasts to all connected clients) ─────────
class SocketServer:
    def __init__(self, host, port):
        self._clients = []
        self._lock = threading.Lock()
        self._server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._server.bind((host, port))
        self._server.listen(5)
        self._server.setblocking(False)
        print(f"[Socket] Listening on {host}:{port}")

    def accept_new(self):
        try:
            conn, addr = self._server.accept()
            conn.setblocking(False)
            with self._lock:
                self._clients.append(conn)
            print(f"[Socket] Client connected: {addr}")
        except BlockingIOError:
            pass

    def send(self, data: str):
        msg = (data + "\n").encode("utf-8")
        dead = []
        with self._lock:
            for c in self._clients:
                try:
                    c.sendall(msg)
                except Exception:
                    dead.append(c)
            for c in dead:
                self._clients.remove(c)

    def close(self):
        with self._lock:
            for c in self._clients:
                try: c.close()
                except: pass
        self._server.close()


# ── Geometry helpers ──────────────────────────────────────────────────────────
def dist(a, b):
    return math.sqrt((a.x-b.x)**2 + (a.y-b.y)**2 + (a.z-b.z)**2)


def angle_deg(a, b, c):
    ba = (a.x-b.x, a.y-b.y, a.z-b.z)
    bc = (c.x-b.x, c.y-b.y, c.z-b.z)
    dot = sum(ba[i]*bc[i] for i in range(3))
    mag = math.sqrt(sum(x**2 for x in ba)) * math.sqrt(sum(x**2 for x in bc))
    if mag == 0: return 0.0
    return math.degrees(math.acos(max(-1, min(1, dot/mag))))


def is_finger_extended(lm, tip_i, dip_i, pip_i, mcp_i, threshold=0.65):
    mcp, pip, dip, tip = lm[mcp_i], lm[pip_i], lm[dip_i], lm[tip_i]
    mcp_to_tip = dist(mcp, tip)
    mcp_to_pip = dist(mcp, pip)
    if mcp_to_pip == 0: return False
    ratio = mcp_to_tip / mcp_to_pip
    pip_angle = angle_deg(mcp, pip, tip)
    return ratio > threshold and pip_angle > 155


def is_thumb_extended(lm, label):
    tip, ip, mcp, cmc = lm[4], lm[3], lm[2], lm[1]
    index_mcp = lm[5]
    if angle_deg(mcp, ip, tip) < 150: return False
    return dist(tip, index_mcp) > dist(cmc, index_mcp) * 0.6


def count_fingers(landmarks, label):
    fingers = [1 if is_thumb_extended(landmarks, label) else 0]
    for tip_i, dip_i, pip_i, mcp_i in FINGER_LANDMARKS[1:]:
        fingers.append(1 if is_finger_extended(landmarks, tip_i, dip_i, pip_i, mcp_i) else 0)
    return fingers


def palm_pos(landmarks, w, h):
    lm = landmarks[0]
    return {"x": round(lm.x * w), "y": round(lm.y * h), "z": round(lm.z, 4)}


def build_json(fingers, palm, label):
    return {
        "hand": label,
        "fingers_up": sum(fingers),
        "fingers": {n: bool(v) for n, v in zip(FINGER_NAMES, fingers)},
        "palm_position": palm,
    }


def draw_hand(frame, landmarks, img_w, img_h):
    pts = [(round(lm.x * img_w), round(lm.y * img_h)) for lm in landmarks]
    for start, end in HAND_CONNECTIONS:
        cv2.line(frame, pts[start], pts[end], (0, 200, 255), 2)
    for x, y in pts:
        cv2.circle(frame, (x, y), 4, (255, 255, 255), -1)
        cv2.circle(frame, (x, y), 4, (0, 150, 255), 1)


# ── Main ──────────────────────────────────────────────────────────────────────
options = HandLandmarkerOptions(
    base_options=BaseOptions(model_asset_path=MODEL_PATH),
    running_mode=RunningMode.IMAGE,
    num_hands=2,
    min_hand_detection_confidence=0.6,
    min_tracking_confidence=0.6,
)

server = SocketServer(SOCKET_HOST, SOCKET_PORT)
cap = cv2.VideoCapture(0)
prev_state = {}

SEND_INTERVAL = 1 / 60   # send at 60 Hz regardless of finger changes
last_send_time = 0.0

with HandLandmarker.create_from_options(options) as detector:
    while cap.isOpened():
        server.accept_new()

        ret, frame = cap.read()
        if not ret:
            break

        frame = cv2.flip(frame, 1)
        img_h, img_w = frame.shape[:2]

        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=frame)
        result   = detector.detect(mp_image)

        output_data = []
        changed = False

        if result.hand_landmarks:
            for i, landmarks in enumerate(result.hand_landmarks):
                label   = result.handedness[i][0].display_name
                fingers = count_fingers(landmarks, label)
                palm    = palm_pos(landmarks, img_w, img_h)
                data    = build_json(fingers, palm, label)
                output_data.append(data)

                if prev_state.get(label) != fingers:
                    prev_state[label] = fingers
                    changed = True

                draw_hand(frame, landmarks, img_w, img_h)
                cv2.putText(frame, f"{label}: {data['fingers_up']} fingers",
                            (palm["x"] - 40, palm["y"] - 20),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
        else:
            if prev_state:
                prev_state.clear()
                changed = True

        now = time.monotonic()

        # Print only when fingers change
        if changed and output_data:
            print(json.dumps(output_data))

        # Send over socket at fixed rate (30 Hz) whenever there's data
        if output_data and (now - last_send_time) >= SEND_INTERVAL:
            server.send(json.dumps(output_data))
            last_send_time = now

        cv2.imshow("Hand Tracker", frame)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

cap.release()
server.close()
cv2.destroyAllWindows()
