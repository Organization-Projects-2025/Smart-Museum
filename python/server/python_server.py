import socket
import threading
import re
import os
import csv
import cv2
import numpy as np
import asyncio
from bleak import BleakScanner
import face_recognition


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
PYTHON_ROOT_DIR = os.path.dirname(SCRIPT_DIR)
WORKSPACE_ROOT_DIR = os.path.dirname(PYTHON_ROOT_DIR)
USERS_CSV_PATH = os.path.join(WORKSPACE_ROOT_DIR, "C#", "content", "auth", "users.csv")
TOLERANCE = 0.60
known_face_names = []
known_face_encodings = []

def scan_bluetooth(target_mac):
    """
    Scan for Bluetooth devices using bleak (Windows-compatible)
    """
    try:
        # Run async scan in sync context
        loop = asyncio.new_event_loop()
        asyncio.set_event_loop(loop)
        result = loop.run_until_complete(_async_scan_bluetooth(target_mac))
        loop.close()
        return result
    except Exception as e:
        return f"ERROR:{str(e)}"

async def _async_scan_bluetooth(target_mac):
    """
    Async Bluetooth scan using bleak
    """
    try:
        # Normalize MAC address format (bleak uses uppercase with colons)
        target_mac_normalized = target_mac.upper().replace("-", ":")
        
        # Scan for 8 seconds
        devices = await BleakScanner.discover(timeout=8.0)
        
        for device in devices:
            device_mac = device.address.upper().replace("-", ":")
            if device_mac == target_mac_normalized:
                device_name = device.name if device.name else "Unknown"
                return f"FOUND:{device_name}:{device.address}"
        
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
    # server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server_socket.bind((host, port))
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
    start_server("localhost", 5000)
