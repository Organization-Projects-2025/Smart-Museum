import socket
import threading
import re
import os
import csv
import cv2
import numpy as np
import face_recognition

try:
    import bluetooth
except ImportError:
    bluetooth = None

from auth_lobby_cam import run_face_auth_lobby_via_worker

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PYTHON_ROOT_DIR = os.path.dirname(SCRIPT_DIR)
WORKSPACE_ROOT_DIR = os.path.dirname(PYTHON_ROOT_DIR)
USERS_CSV_PATH = os.path.join(WORKSPACE_ROOT_DIR, "C#", "content", "auth", "users.csv")
FACES_DIR = os.path.join(WORKSPACE_ROOT_DIR, "python", "data", "faces")
TOLERANCE = 0.60
known_face_names = []
known_face_encodings = []


def _ensure_faces_dir():
    try:
        os.makedirs(FACES_DIR, exist_ok=True)
    except Exception:
        pass


def next_face_user_id():
    """Next userN id from users.csv (user0, user1, ...)."""
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


def face_register_scan():
    """
    Capture face: if matches known user -> FOUND:userId
    If face visible but no match -> save image as python/data/faces/{nextId}.jpg and return NEW:userId
    If no usable face -> NOT_FOUND or ERROR
    """
    try:
        load_known_faces()
        _ensure_faces_dir()

        video_capture = cv2.VideoCapture(0)
        if not video_capture.isOpened():
            return "ERROR:Could not open camera"

        chosen_encoding = None
        chosen_frame_bgr = None
        face_locations_small = None
        max_frames = 120
        frame_count = 0

        try:
            while frame_count < max_frames and chosen_encoding is None:
                ret, frame = video_capture.read()
                frame_count += 1
                if not ret:
                    continue

                small_frame = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
                rgb_small_frame = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)
                face_locations = face_recognition.face_locations(rgb_small_frame)
                if len(face_locations) == 0:
                    continue

                encodings = face_recognition.face_encodings(rgb_small_frame, face_locations)
                if len(encodings) == 0:
                    continue

                face_encoding = encodings[0]
                loc = face_locations[0]

                if len(known_face_encodings) > 0:
                    matches = face_recognition.compare_faces(
                        known_face_encodings, face_encoding, tolerance=TOLERANCE
                    )
                    face_distances = face_recognition.face_distance(known_face_encodings, face_encoding)
                    best_match_index = int(np.argmin(face_distances))
                    if matches[best_match_index]:
                        return "FOUND:" + known_face_names[best_match_index]

                chosen_encoding = face_encoding
                chosen_frame_bgr = frame.copy()
                face_locations_small = loc
                break

        finally:
            video_capture.release()
            cv2.destroyAllWindows()

        if chosen_encoding is None or chosen_frame_bgr is None:
            return "NOT_FOUND"

        new_id = next_face_user_id()
        rel_path = "python/data/faces/" + new_id + ".jpg"
        abs_path = os.path.join(WORKSPACE_ROOT_DIR, rel_path.replace("/", os.sep))
        _ensure_faces_dir()
        top, right, bottom, left = face_locations_small
        top *= 4
        right *= 4
        bottom *= 4
        left *= 4
        h, w = chosen_frame_bgr.shape[:2]
        pad = 40
        top = max(0, int(top) - pad)
        left = max(0, int(left) - pad)
        bottom = min(h, int(bottom) + pad)
        right = min(w, int(right) + pad)
        crop = chosen_frame_bgr[top:bottom, left:right]
        if crop.size == 0:
            crop = chosen_frame_bgr
        ok = cv2.imwrite(abs_path, crop)
        if not ok:
            return "ERROR:Could not save face image"

        return "NEW:" + new_id

    except Exception as e:
        return "ERROR:" + str(e)


def bluetooth_register_pick():
    """Uses the same discovery call as python_server_legacy scan_bluetooth (duration=8)."""
    try:
        if bluetooth is None:
            return "ERROR:PyBluez is not installed (pip install pybluez2)"
        devices = bluetooth.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        if not devices:
            return "NOT_FOUND"
        picked_addr, picked_name = None, None
        for addr, name in devices:
            if name and str(name).strip():
                picked_addr, picked_name = addr, name
                break
        if picked_addr is None:
            picked_addr, picked_name = devices[0]
        display_name = (picked_name or "Unknown").replace("\t", " ").replace("\n", " ").strip() or "Unknown"
        return "FOUND\t" + display_name + "\t" + picked_addr
    except Exception as e:
        return "ERROR:" + str(e)


def scan_bluetooth(target_mac):
    """Same logic as python_server_legacy.py scan_bluetooth (exact discover + addr == target_mac)."""
    try:
        if bluetooth is None:
            return "ERROR:PyBluez is not installed (pip install pybluez2)"
        devices = bluetooth.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        for addr, name in devices:
            if addr == target_mac:
                return f"FOUND:{name}:{addr}"
        return "NOT_FOUND"
    except Exception as e:
        return f"ERROR:{str(e)}"

def load_known_faces():
    global known_face_names, known_face_encodings
    known_face_names = []
    known_face_encodings = []

    if os.path.isfile(USERS_CSV_PATH):
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
                            known_face_encodings.append(encodings[0])
                            known_face_names.append(user_id)
                    except Exception as e:
                        print(f"Error loading face {user_id} from {abs_path}: {e}")
        except Exception as e:
            print(f"Error reading users CSV {USERS_CSV_PATH}: {e}")

def scan_face_id():
    try:
        if len(known_face_encodings) == 0:
            load_known_faces()
        
        if len(known_face_encodings) == 0:
            return "ERROR:No known faces loaded"
        
        video_capture = cv2.VideoCapture(0)
        if not video_capture.isOpened():
            return "ERROR:Could not open camera"
        
        face_found = False
        found_user = None
        max_frames = 100
        frame_count = 0
        
        try:
            while not face_found and frame_count < max_frames:
                ret, frame = video_capture.read()
                frame_count += 1
                if not ret:
                    continue
                
                small_frame = cv2.resize(frame, (0, 0), fx=0.25, fy=0.25)
                rgb_small_frame = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)
                
                face_locations = face_recognition.face_locations(rgb_small_frame)
                face_encodings = face_recognition.face_encodings(rgb_small_frame, face_locations)
                
                for face_encoding in face_encodings:
                    matches = face_recognition.compare_faces(known_face_encodings, face_encoding, tolerance=TOLERANCE)
                    face_distances = face_recognition.face_distance(known_face_encodings, face_encoding)
                    best_match_index = int(np.argmin(face_distances))
                    
                    if matches[best_match_index]:
                        found_user = known_face_names[best_match_index]
                        face_found = True
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

def handle_client(conn, addr):
    print(f"Client connected: {addr}")
    buffer = ""
    
    try:
        while True:
            data = conn.recv(1024)
            if not data:
                break
            
            buffer += data.decode("utf-8")
            
            while "\n" in buffer or buffer.strip():
                if "\n" in buffer:
                    line, buffer = buffer.split("\n", 1)
                else:
                    line = buffer
                    buffer = ""
                
                command = line.strip()
                if not command:
                    continue
        
                parts = command.split()                
                if parts[0] == "bluetooth_scan" and len(parts) >= 2:
                    target_mac = parts[1]
                    result = scan_bluetooth(target_mac)
                    conn.send(result.encode("utf-8"))
                
                elif parts[0] == "face_id_scan":
                    result = scan_face_id()
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "face_register_scan":
                    result = face_register_scan()
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "face_auth_lobby":
                    result = run_face_auth_lobby_via_worker()
                    conn.send(result.encode("utf-8"))

                elif parts[0] == "bluetooth_register_pick":
                    result = bluetooth_register_pick()
                    conn.send(result.encode("utf-8"))
                
                elif parts[0] == "exit":
                    conn.send(b"BYE")
                    break
    
    except Exception as e:
        print(f"Error: {e}")
    
    finally:
        conn.close()
        print(f"Client disconnected: {addr}")

def start_server(host, port):
    server_socket = socket.socket()
    try:
        server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    except OSError:
        pass
    try:
        server_socket.bind((host, port))
    except OSError as e:
        winerr = getattr(e, "winerror", None)
        if winerr == 10048 or "10048" in str(e) or "already in use" in str(e).lower():
            print(
                f"Port {port} is already in use. Another python_server (or another program) is still running.\n"
                f"  • Close that window/process, then start again.\n"
                f"  • Or set PYTHON_SERVER_PORT to a free port (you must use the same port in the C# app)."
            )
        raise
    server_socket.listen(5)
    
    try:
        while True:
            conn, addr = server_socket.accept()
            client_thread = threading.Thread(target=handle_client, args=(conn, addr))
            client_thread.daemon = True
            client_thread.start()
    
    except KeyboardInterrupt:
        print("\nServer stopping...")
    
    finally:
        server_socket.close()


if __name__ == "__main__":
    _port = int(os.environ.get("PYTHON_SERVER_PORT", "5000"))
    start_server("localhost", _port)
