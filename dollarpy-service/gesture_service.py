"""
Gesture Recognition Service for C# Integration
Runs a socket server that C# can connect to for real-time gesture recognition
"""
import socket
import json
import cv2
from dollarpy import Point
from gesture_recognizer import SmartMuseumGestureRecognizer
import threading
import time
# Use compatibility layer for mediapipe 0.10.30+
import mediapipe_compat as mp

class GestureRecognitionService:
    def __init__(self, host='localhost', port=5001):
        self.host = host
        self.port = port
        self.server_socket = None
        self.is_running = False
        
        # Initialize recognizer (shared across all clients)
        self.recognizer = SmartMuseumGestureRecognizer()
        self.recognizer.load_templates()
        
        # MediaPipe setup (shared)
        self.mp_hands = mp.solutions.hands
        
    def start_server(self):
        """Start the socket server"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind((self.host, self.port))
        self.server_socket.listen(5)
        
        print(f"Gesture Recognition Service started on {self.host}:{self.port}")
        print("Waiting for C# clients to connect...")
        
        self.is_running = True
        
        try:
            while self.is_running:
                client_socket, addr = self.server_socket.accept()
                print(f"Client connected from {addr}")
                
                # Handle each client in a separate thread
                client_thread = threading.Thread(
                    target=self.handle_client, 
                    args=(client_socket, addr),
                    daemon=True
                )
                client_thread.start()
        
        except KeyboardInterrupt:
            print("\nServer stopping...")
        finally:
            self.cleanup()
    
    def handle_client(self, client_socket, addr):
        """Handle commands from a C# client"""
        # Per-client state
        hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )
        cap = None
        is_tracking = False
        gesture_points = []
        last_gesture = None
        last_gesture_time = 0
        gesture_cooldown = 1.0
        camera_thread = None
        camera_running = False
        
        def camera_loop():
            """Continuously process camera frames for this client"""
            nonlocal is_tracking, gesture_points, camera_running
            
            while camera_running and cap.isOpened():
                ret, frame = cap.read()
                if not ret:
                    continue
                
                frame = cv2.resize(frame, (640, 480))
                # Removed flip - keep camera natural orientation
                
                # Process with MediaPipe
                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                results = hands.process(rgb_frame)
                
                if results.multi_hand_landmarks and is_tracking:
                    image_height, image_width, _ = frame.shape
                    
                    for hand_landmarks in results.multi_hand_landmarks:
                        # Track 6 key landmarks
                        key_landmarks = [0, 4, 8, 12, 16, 20]
                        stroke_id = len(gesture_points) // 6 + 1
                        
                        for landmark_id in key_landmarks:
                            landmark = hand_landmarks.landmark[landmark_id]
                            x = int(landmark.x * image_width)
                            y = int(landmark.y * image_height)
                            gesture_points.append(Point(x, y, stroke_id))
                
                time.sleep(0.016)  # ~60 FPS
        
        def start_camera():
            """Start camera capture for this client"""
            nonlocal cap, camera_thread, camera_running
            
            cap = cv2.VideoCapture(0)
            if not cap.isOpened():
                print(f"Error: Could not open camera for client {addr}")
                return False
            
            camera_running = True
            camera_thread = threading.Thread(target=camera_loop, daemon=True)
            camera_thread.start()
            return True
        
        def process_command(command):
            """Process commands from C# client"""
            nonlocal is_tracking, gesture_points, last_gesture, last_gesture_time
            
            if command == "START_TRACKING":
                if cap is None:
                    if not start_camera():
                        return {"status": "error", "message": "Failed to start camera"}
                
                is_tracking = True
                gesture_points = []
                return {"status": "ok", "message": "Tracking started"}
            
            elif command == "STOP_TRACKING":
                is_tracking = False
                return {"status": "ok", "message": "Tracking stopped", "points": len(gesture_points)}
            
            elif command == "RECOGNIZE":
                if len(gesture_points) < 10:
                    print(f"  INFO: Not enough points: {len(gesture_points)}")
                    return {"status": "error", "message": "Not enough points", "gesture": None, "score": 0.0}
                
                # Check cooldown
                current_time = time.time()
                if current_time - last_gesture_time < gesture_cooldown:
                    print(f"  INFO: Cooldown active")
                    return {"status": "cooldown", "message": "Gesture cooldown active", "gesture": None, "score": 0.0}
                
                # Recognize gesture
                print(f"  INFO: Recognizing with {len(gesture_points)} points...")
                gesture_name, score = self.recognizer.recognize(gesture_points)
                print(f"  INFO: Result: {gesture_name} (score: {score:.4f})")
                
                # Only return if confidence is above minimum threshold (0.08 - lowered for better detection)
                if score > 0.08:
                    base_name = self.recognizer.get_gesture_base_name(gesture_name)
                    last_gesture = base_name
                    last_gesture_time = current_time
                    
                    # Determine confidence level
                    if score > 0.7:
                        confidence = "high"
                    elif score > 0.4:
                        confidence = "medium"
                    else:
                        confidence = "low"
                    
                    print(f"  OK: GESTURE DETECTED: {base_name} (confidence: {confidence})")
                    
                    return {
                        "status": "ok",
                        "gesture": base_name,
                        "score": round(score, 4),
                        "confidence": confidence
                    }
                else:
                    print(f"  LOW_CONFIDENCE: {score:.4f} (minimum: 0.08)")
                    return {
                        "status": "ok",
                        "gesture": None,
                        "score": round(score, 4),
                        "confidence": "too_low"
                    }
            
            elif command == "RESET":
                gesture_points = []
                is_tracking = False
                return {"status": "ok", "message": "Reset complete"}
            
            elif command == "STATUS":
                status_info = {
                    "status": "ok",
                    "tracking": is_tracking,
                    "points": len(gesture_points),
                    "templates": len(self.recognizer.templates) if self.recognizer.templates else 0,
                    "last_gesture": last_gesture
                }
                # Only print if tracking and has significant points (reduce spam)
                if is_tracking and len(gesture_points) > 0 and len(gesture_points) % 30 == 0:
                    print(f"  INFO: Tracking: {len(gesture_points)} points collected")
                return status_info
            
            elif command == "PING":
                return {"status": "ok", "message": "pong"}
            
            else:
                return {"status": "error", "message": f"Unknown command: {command}"}
        
        def send_response(response):
            """Send JSON response to C# client"""
            try:
                json_response = json.dumps(response) + "\n"
                client_socket.send(json_response.encode('utf-8'))
            except Exception as e:
                print(f"Error sending response to {addr}: {e}")
        
        # Main client loop
        try:
            while self.is_running:
                data = client_socket.recv(1024)
                if not data:
                    break
                
                try:
                    message = data.decode('utf-8').strip()
                    print(f"Received from {addr}: {message}")
                    
                    response = process_command(message)
                    send_response(response)
                    
                except Exception as e:
                    print(f"Error processing message from {addr}: {e}")
                    send_response({"status": "error", "message": str(e)})
        
        except Exception as e:
            print(f"Client {addr} connection error: {e}")
        finally:
            # Cleanup client resources
            print(f"Cleaning up client {addr}...")
            camera_running = False
            
            if cap:
                cap.release()
            
            if client_socket:
                client_socket.close()
            
            print(f"Client {addr} disconnected")
    
    def cleanup(self):
        """Clean up server resources"""
        print("Cleaning up server...")
        self.is_running = False
        
        if self.server_socket:
            self.server_socket.close()
        
        print("Service stopped")

def main():
    print("=" * 60)
    print("Smart Museum - Gesture Recognition Service")
    print("=" * 60)
    print("\nThis service provides gesture recognition for C# applications")
    print("via socket communication on localhost:5001\n")
    
    service = GestureRecognitionService(host='localhost', port=5001)
    
    try:
        service.start_server()
    except KeyboardInterrupt:
        print("\nShutting down...")
        service.cleanup()
    except Exception as e:
        print(f"Error: {e}")
        service.cleanup()

if __name__ == "__main__":
    main()
