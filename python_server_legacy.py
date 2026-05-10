# Simple Python Socket Server for Bluetooth and Face ID
# Follows pattern from Code Samples/serverCSharp.ipynb

import socket
import threading
import re
import os
import sys
import cv2
import numpy as np

# Trajectory + gesture imports
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "python"))
try:
    from trajectory_analyzer import analyze_trajectory
    from dollarpy import Recognizer, Template, Point as DollarPoint
    _gesture_ready = True
except ImportError as _e:
    _gesture_ready = False
    print(f"Gesture scan unavailable: {_e}")

try:
    import bluetooth
except ImportError:
    bluetooth = None

try:
    import face_recognition
except ImportError:
    face_recognition = None

# Face ID configuration
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PEOPLE_DIR = os.path.join(SCRIPT_DIR, "FaceRecognition-GUI-APP", "people")
TOLERANCE = 0.60
known_face_names = []
known_face_encodings = []

# ── MAC Address Helper ─────────────────────────────────────────────────────
def normalize_mac(text):
    # Convert any MAC format to uppercase with colons: XX:XX:XX:XX:XX:XX
    cleaned = re.sub(r"[^0-9A-Fa-f]", "", text or "").upper()
    if len(cleaned) != 12:
        return ""
    return ":".join(cleaned[i:i + 2] for i in range(0, 12, 2))


def scan_bluetooth(target_mac):
    # TODO: Bluetooth scanning disabled locally — works on target machine with PyBluez installed.
    # Uncomment the block below when deploying to a machine with bluetooth support.
    return "ERROR:Bluetooth disabled on this machine"

    # if not bluetooth:
    #     return "ERROR:PyBluez not installed"
    # try:
    #     target_normalized = normalize_mac(target_mac)
    #     if not target_normalized:
    #         return "ERROR:Invalid MAC format"
    #     devices = bluetooth.discover_devices(lookup_names=True, duration=8, flush_cache=True)
    #     for addr, name in devices:
    #         if normalize_mac(addr) == target_normalized:
    #             return f"FOUND:{name}:{addr}"
    #     return "NOT_FOUND"
    # except Exception as e:
    #     return f"ERROR:{str(e)}"


# ── Face ID Helper ────────────────────────────────────────────────────────
def load_known_faces():
    # Load all face encodings from people directory
    global known_face_names, known_face_encodings
    known_face_names = []
    known_face_encodings = []

    if not face_recognition or not os.path.isdir(PEOPLE_DIR):
        print(f"Face recognition not available or people dir missing: {PEOPLE_DIR}")
        return

    print(f"Loading faces from {PEOPLE_DIR}")
    for name in os.listdir(PEOPLE_DIR):
        low = name.lower()
        if not (low.endswith(".jpg") or low.endswith(".jpeg") or low.endswith(".png")):
            continue

        path = os.path.join(PEOPLE_DIR, name)
        try:
            image = face_recognition.load_image_file(path)
            encodings = face_recognition.face_encodings(image)
            if len(encodings) > 0:
                known_face_encodings.append(encodings[0])
                known_face_names.append(os.path.splitext(name)[0])
                print(f"Loaded face: {os.path.splitext(name)[0]}")
            else:
                print(f"No face found in {name}")
        except Exception as e:
            print(f"Error loading face {name}: {e}")

    print(f"Total known faces loaded: {len(known_face_names)}")


def scan_face_id():
    # Scan camera for recognized faces
    if not face_recognition:
        return "ERROR:face_recognition not installed"
    
    try:
        # Load known faces if not already loaded
        if len(known_face_encodings) == 0:
            load_known_faces()
        
        if len(known_face_encodings) == 0:
            return "ERROR:No known faces in people directory"
        
        print(f"Starting face scan with {len(known_face_encodings)} known faces")
        
        # Open camera
        video_capture = cv2.VideoCapture(0)
        if not video_capture.isOpened():
            return "ERROR:Could not open camera"
        
        face_found = False
        found_user = None
        max_frames = 100  # Max 100 frames before timeout
        frame_count = 0
        
        try:
            while not face_found and frame_count < max_frames:
                ret, frame = video_capture.read()
                frame_count += 1
                if not ret:
                    continue
                
                # Resize for faster processing
                small_frame = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
                rgb_small_frame = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)
                
                # Detect faces
                face_locations = face_recognition.face_locations(rgb_small_frame)
                print(f"Frame {frame_count}: Found {len(face_locations)} faces")
                face_encodings = face_recognition.face_encodings(rgb_small_frame, face_locations)
                
                # Match against known faces
                for face_encoding in face_encodings:
                    matches = face_recognition.compare_faces(known_face_encodings, face_encoding, tolerance=TOLERANCE)
                    face_distances = face_recognition.face_distance(known_face_encodings, face_encoding)
                    best_match_index = int(np.argmin(face_distances))
                    
                    if matches[best_match_index]:
                        found_user = known_face_names[best_match_index]
                        face_found = True
                        print(f"Match found: {found_user}")
                        break
        
        finally:
            video_capture.release()
            cv2.destroyAllWindows()
        
        if found_user:
            return f"FOUND:{found_user}"
        else:
            return "NOT_FOUND"
    
    except Exception as e:
        return f"ERROR:{str(e)}"


# ── Gesture Scan (reads from hand_tracker.py on port 5555) ───────────────
# Gesture templates are trained once and reused across requests
_gesture_recognizer = None
_gesture_templates = []

HAND_TRACKER_HOST = "127.0.0.1"
HAND_TRACKER_PORT = 5555
COLLECT_SECONDS   = 3  # how long to collect palm points per gesture

def register_gesture_template(label, points):
    global _gesture_recognizer
    _gesture_templates.append(Template(label, points))
    _gesture_recognizer = Recognizer(_gesture_templates)
    print(f"Template registered: {label} ({len(_gesture_templates)} total)")

def _collect_palm_points(duration=COLLECT_SECONDS):
    """
    Connect to hand_tracker.py socket, collect palm positions for `duration` seconds.
    Returns list of DollarPoint from the dominant (first) hand's palm.
    """
    import json as _json
    import time as _time

    points = []
    deadline = _time.time() + duration

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect((HAND_TRACKER_HOST, HAND_TRACKER_PORT))
        sock.settimeout(1.0)
        buf = ""
        print(f"Collecting palm points for {duration}s from hand tracker...")

        while _time.time() < deadline:
            try:
                chunk = sock.recv(4096).decode("utf-8")
                if not chunk:
                    break
                buf += chunk
                while "\n" in buf:
                    line, buf = buf.split("\n", 1)
                    line = line.strip()
                    if not line:
                        continue
                    hands = _json.loads(line)
                    if hands:
                        palm = hands[0]["palm_position"]
                        points.append(DollarPoint(palm["x"], palm["y"], 1))
            except socket.timeout:
                continue
    except Exception as e:
        print(f"Hand tracker connection error: {e}")
    finally:
        try: sock.close()
        except: pass

    print(f"Collected {len(points)} palm points")
    return points

def scan_gesture():
    """
    Collect palm trajectory from hand_tracker.py, run DollarPy + trajectory analysis.
    Returns: RESULT:gesture:confidence:motion_type:path_length:straightness
    """
    if not _gesture_ready:
        return "ERROR:dollarpy not installed"
    if not _gesture_recognizer:
        return "ERROR:No gesture templates registered. Use gesture_train first."
    try:
        points = _collect_palm_points()
        if len(points) < 3:
            return "ERROR:Not enough points — is hand_tracker.py running?"

        result = _gesture_recognizer.recognize(points)
        gesture_name = result[0] or "unknown"
        confidence   = result[1]
        features     = analyze_trajectory(points)

        return (f"RESULT:{gesture_name}:{confidence:.4f}:"
                f"{features['motion_type']}:{features['path_length']}:"
                f"{features['straightness']}")
    except Exception as e:
        return f"ERROR:{str(e)}"

def train_gesture(label):
    """
    Collect palm trajectory from hand_tracker.py and save as a named template.
    Returns: TRAINED:label:point_count  or  ERROR:message
    """
    if not _gesture_ready:
        return "ERROR:dollarpy not installed"
    try:
        print(f"Training gesture: {label}")
        points = _collect_palm_points()
        if len(points) < 3:
            return "ERROR:Not enough points — is hand_tracker.py running?"
        register_gesture_template(label, points)
        return f"TRAINED:{label}:{len(points)}"
    except Exception as e:
        return f"ERROR:{str(e)}"


# ── Socket Server Thread ───────────────────────────────────────────────────
def handle_client(conn, addr):
    # Handle one client connection
    print(f"Client connected: {addr}")
    buffer = ""  # Buffer for partial messages
    
    try:
        while True:
            # Receive data from client
            data = conn.recv(1024)
            if not data:
                break
            
            # Add to buffer and process complete lines
            buffer += data.decode("utf-8")
            
            # Process all complete lines (ending with \n)
            while "\n" in buffer or buffer.strip():
                if "\n" in buffer:
                    line, buffer = buffer.split("\n", 1)
                else:
                    line = buffer
                    buffer = ""
                
                command = line.strip().strip('\r')
                if not command:
                    continue
                
                print(f"Received: {command}")
                print(f"  Command (repr): {repr(command)}")  # Debug: show exact string
                
                # Parse command: "bluetooth_scan MAC" or "face_id_scan" or "exit"
                parts = command.split()
                print(f"  Parts after split: {parts}")  # Debug: show parsed parts
                
                print(f"  First part: {repr(parts[0])}")  # Debug: show first part
                print(f"  Is face_id_scan: {parts[0] == 'face_id_scan'}")
                if parts[0] == "bluetooth_scan" and len(parts) >= 2:
                    target_mac = parts[1]
                    result = scan_bluetooth(target_mac)
                    conn.send(result.encode("utf-8"))
                
                elif parts[0] == "face_id_scan":
                    result = scan_face_id()
                    print(f"Face ID result: {result}")
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "gesture_scan":
                    result = scan_gesture()
                    print(f"Gesture result: {result}")
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "gesture_train" and len(parts) >= 2:
                    label = " ".join(parts[1:])
                    result = train_gesture(label)
                    print(f"Train result: {result}")
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "exit":
                    conn.send(b"BYE")
                    break
                
                else:
                    print(f"  ERROR: Unknown command '{parts[0]}'")
                    conn.send(b"ERROR:Unknown command")
    
    except Exception as e:
        print(f"Error: {e}")
    
    finally:
        conn.close()
        print(f"Client disconnected: {addr}")


def start_server(host, port):
    # Start the socket server
    server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_socket.bind((host, port))
    server_socket.listen(5)
    
    print(f"Server listening on {host}:{port}")
    print("Press Ctrl+C to stop")
    
    try:
        while True:
            conn, addr = server_socket.accept()
            # Handle each client in a new thread
            client_thread = threading.Thread(target=handle_client, args=(conn, addr))
            client_thread.daemon = True
            client_thread.start()
    
    except KeyboardInterrupt:
        print("\nServer stopping...")
    
    finally:
        server_socket.close()


if __name__ == "__main__":
    start_server("localhost", 5000)
