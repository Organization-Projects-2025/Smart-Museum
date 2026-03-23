# Simple Python Socket Server for Bluetooth and Face ID
# Follows pattern from Code Samples/serverCSharp.ipynb

import socket
import threading
import re
import os
import cv2
import numpy as np

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
    # Scan for Bluetooth device with matching MAC
    if not bluetooth:
        return "ERROR:PyBluez not installed"
    
    try:
        target_normalized = normalize_mac(target_mac)
        if not target_normalized:
            return "ERROR:Invalid MAC format"
        
        devices = bluetooth.discover_devices(lookup_names=True, duration=8, flush_cache=True)
        
        for addr, name in devices:
            if normalize_mac(addr) == target_normalized:
                return f"FOUND:{name}:{addr}"
        
        return "NOT_FOUND"
    except Exception as e:
        return f"ERROR:{str(e)}"


# ── Face ID Helper ────────────────────────────────────────────────────────
def load_known_faces():
    # Load all face encodings from people directory
    global known_face_names, known_face_encodings
    known_face_names = []
    known_face_encodings = []

    if not face_recognition or not os.path.isdir(PEOPLE_DIR):
        return

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
        except Exception as e:
            print(f"Error loading face {name}: {e}")


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
                face_encodings = face_recognition.face_encodings(rgb_small_frame, face_locations)
                
                # Match against known faces
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
                
                command = line.strip()
                if not command:
                    continue
                
                print(f"Received: {command}")
                print(f"  Command (repr): {repr(command)}")  # Debug: show exact string
                
                # Parse command: "bluetooth_scan MAC" or "face_id_scan" or "exit"
                parts = command.split()
                print(f"  Parts after split: {parts}")  # Debug: show parsed parts
                
                print(f"  First part: {repr(parts[0])}")  # Debug: show first part
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
