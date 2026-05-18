"""
laser_server.py — TCP server for red laser point tracking and rating.

Shares CameraHub with other services. Protocol: newline-delimited JSON.

Commands:
  PING           → {"status":"ok","service":"laser"}
  STATUS         → {"status":"ok","tracking":bool,"rating":int,"hold_progress":float}
  START          → Begin tracking, stream updates
  STOP           → Stop tracking

Streaming updates (after START):
  {"type":"update","rating":3,"hold_progress":0.45}
  {"type":"confirmed","rating":4}
  {"type":"stopped"}
"""

import json
import math
import os
import socket
import threading
import time

import cv2
import numpy as np


HOLD_SECONDS = 5.0
HOST = "127.0.0.1"
PORT = 5006


class LaserTracker:
    def __init__(self, hub):
        self._hub = hub
        self._lock = threading.Lock()
        self._tracking = False
        self._rating = 0
        self._hold_progress = 0.0
        self._hold_start = 0.0
        self._confirmed = False
        self._confirmed_rating = 0

    def start(self):
        with self._lock:
            self._tracking = True
            self._rating = 0
            self._hold_progress = 0.0
            self._hold_start = 0.0
            self._confirmed = False
            self._confirmed_rating = 0

    def stop(self):
        with self._lock:
            self._tracking = False
            self._rating = 0
            self._hold_progress = 0.0

    @property
    def is_tracking(self):
        return self._tracking

    def get_state(self):
        with self._lock:
            return {
                "tracking": self._tracking,
                "rating": self._rating,
                "hold_progress": self._hold_progress,
                "confirmed": self._confirmed,
                "confirmed_rating": self._confirmed_rating,
            }

    def process_frame(self):
        frame = self._hub.get_frame()
        if frame is None:
            return None

        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        blurred = cv2.GaussianBlur(frame, (5, 5), 0)
        hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)

        lower_red1 = np.array([0, 120, 200])
        upper_red1 = np.array([10, 255, 255])
        lower_red2 = np.array([170, 120, 200])
        upper_red2 = np.array([180, 255, 255])

        mask1 = cv2.inRange(hsv, lower_red1, upper_red1)
        mask2 = cv2.inRange(hsv, lower_red2, upper_red2)
        mask = cv2.bitwise_or(mask1, mask2)

        kernel = np.ones((5, 5), np.uint8)
        mask = cv2.erode(mask, kernel, iterations=1)
        mask = cv2.dilate(mask, kernel, iterations=2)

        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        best = None
        best_area = 0

        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area > best_area and area >= 100:
                best_area = area
                x, y, bw, bh = cv2.boundingRect(cnt)
                best = (x + bw // 2, y + bh // 2)

        now = time.time()
        with self._lock:
            if best is not None and not self._confirmed:
                cx, cy = best
                new_rating = max(1, min(5, int((cx / w) * 5) + 1))

                if new_rating == self._rating:
                    elapsed = now - self._hold_start
                    self._hold_progress = min(1.0, elapsed / HOLD_SECONDS)
                else:
                    self._rating = new_rating
                    self._hold_start = now
                    self._hold_progress = 0.0

                if self._hold_progress >= 1.0:
                    self._confirmed = True
                    self._confirmed_rating = self._rating
            else:
                self._rating = 0
                self._hold_progress = 0.0

            result = {
                "rating": self._rating,
                "hold_progress": self._hold_progress,
                "confirmed": self._confirmed,
                "confirmed_rating": self._confirmed_rating,
            }

        return result


def handle_client(conn, tracker):
    buf = ""
    streaming = False

    try:
        while True:
            conn.settimeout(0.01 if streaming else 0.5)

            try:
                data = conn.recv(4096)
                if not data:
                    break
                buf += data.decode("utf-8")
            except socket.timeout:
                pass

            while "\n" in buf:
                line, buf = buf.split("\n", 1)
                line = line.strip()
                if not line:
                    continue

                cmd = line.upper().strip()

                if cmd == "PING":
                    send_json(conn, {"status": "ok", "service": "laser"})

                elif cmd == "STATUS":
                    state = tracker.get_state()
                    send_json(conn, {
                        "status": "ok",
                        "tracking": state["tracking"],
                        "rating": state["rating"],
                        "hold_progress": state["hold_progress"],
                        "confirmed": state["confirmed"],
                        "confirmed_rating": state["confirmed_rating"],
                    })

                elif cmd == "START":
                    tracker.start()
                    streaming = True
                    send_json(conn, {"status": "ok", "message": "tracking started"})

                elif cmd == "STOP":
                    streaming = False
                    tracker.stop()
                    send_json(conn, {"type": "stopped"})

                else:
                    send_json(conn, {"status": "error", "message": f"unknown command: {cmd}"})

            if streaming:
                result = tracker.process_frame()
                if result:
                    if result["confirmed"]:
                        send_json(conn, {
                            "type": "confirmed",
                            "rating": result["confirmed_rating"],
                        })
                        streaming = False
                        tracker.stop()
                    else:
                        send_json(conn, {
                            "type": "update",
                            "rating": result["rating"],
                            "hold_progress": round(result["hold_progress"], 3),
                        })

    except (ConnectionResetError, BrokenPipeError, OSError):
        pass
    finally:
        tracker.stop()
        try:
            conn.close()
        except Exception:
            pass


def send_json(conn, data):
    try:
        conn.sendall((json.dumps(data) + "\n").encode("utf-8"))
    except Exception:
        pass


def start(hub):
    tracker = LaserTracker(hub)
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((HOST, PORT))
    server.listen(5)
    server.settimeout(1.0)
    print(f"[Laser] Server listening on {HOST}:{PORT}")

    while True:
        try:
            conn, addr = server.accept()
            print(f"[Laser] Client connected from {addr}")
            t = threading.Thread(target=handle_client, args=(conn, tracker), daemon=True)
            t.start()
        except socket.timeout:
            continue
        except Exception as e:
            print(f"[Laser] Accept error: {e}")
            continue
