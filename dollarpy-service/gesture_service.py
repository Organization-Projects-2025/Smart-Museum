"""
Gesture Recognition Service for C# Integration
Runs a socket server that C# can connect to for real-time gesture recognition
"""
import os
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
    def __init__(self, host="127.0.0.1", port=5001, camera_hub=None):
        self.host = host
        self.port = port
        self.server_socket = None
        self.is_running = False
        # When set (e.g. museum_vision_server), frames come from SharedCameraHub — no local VideoCapture.
        self.camera_hub = camera_hub
        
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
        client_id = f"gesture:{addr}"
        hub_acquired = False
        # Per-client state
        hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )
        cap = None
        is_tracking = False
        
        # SLIDING WINDOW APPROACH (matches gesture_gui.py)
        gesture_frames_data = []  # Store MediaPipe landmarks + frame
        MAX_WINDOW_FRAMES = 60  # Strict 60-frame sliding window
        
        last_gesture = None
        last_gesture_time = 0
        gesture_cooldown = 3.0  # 3 seconds cooldown after detection
        last_recognition_time = 0
        recognition_interval = 0.05  # Recognize every 50ms (match GUI)
        confidence_threshold = 0.4  # Only trigger if confidence > 0.4
        
        camera_thread = None
        camera_running = False

        def camera_loop():
            """Continuously process camera frames for this client - SLIDING WINDOW"""
            nonlocal is_tracking, gesture_frames_data, camera_running
            nonlocal last_recognition_time, last_gesture, last_gesture_time

            while camera_running:
                if self.camera_hub is not None:
                    frame = self.camera_hub.get_latest_bgr_copy()
                    if frame is None:
                        time.sleep(0.016)
                        continue
                else:
                    if cap is None or not cap.isOpened():
                        break
                    ret, frame = cap.read()
                    if not ret:
                        continue

                frame = cv2.resize(frame, (640, 480))
                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                results = hands.process(rgb_frame)

                if not is_tracking:
                    time.sleep(0.016)
                    continue

                # SLIDING WINDOW: Always collect frames when hand is detected
                if results.multi_hand_landmarks:
                    for hand_landmarks in results.multi_hand_landmarks:
                        # Store MediaPipe landmarks + frame (same format as GUI)
                        gesture_frames_data.append({
                            "hand_landmarks": hand_landmarks,
                            "frame": frame
                        })
                        
                        # Maintain strict sliding window: remove oldest when exceeding limit
                        if len(gesture_frames_data) > MAX_WINDOW_FRAMES:
                            gesture_frames_data.pop(0)  # Remove frame 0 when frame 61 arrives
                        
                        break  # Only process first hand

                # CONTINUOUS RECOGNITION (like GUI real-time mode)
                current_time = time.time()
                
                # Check if we're in cooldown period (3 seconds after last gesture)
                in_cooldown = (current_time - last_gesture_time) < gesture_cooldown
                
                if (len(gesture_frames_data) >= 10 and 
                    (current_time - last_recognition_time) >= recognition_interval and
                    not in_cooldown):  # Only recognize if NOT in cooldown
                    
                    # Import preprocessing here to avoid circular imports
                    from gesture_preprocessing import preprocess_gesture_frames
                    from gesture_config import GESTURE_MODE, GESTURE_NORMALIZATION_MODE, MIN_MOTION_DISTANCE
                    
                    # Preprocess frames to get points
                    points = preprocess_gesture_frames(
                        gesture_frames_data,
                        mode=GESTURE_MODE,
                        normalize_mode=GESTURE_NORMALIZATION_MODE,
                        min_motion=MIN_MOTION_DISTANCE
                    )
                    
                    if points is not None and len(points) >= 10:
                        try:
                            gesture_name, score = self.recognizer.recognize(points)
                            
                            # Only trigger if confidence > 0.4
                            if score > confidence_threshold:
                                base_name = self.recognizer.get_gesture_base_name(gesture_name)
                                
                                # Determine confidence level
                                if score > 0.7:
                                    confidence = "high"
                                elif score > 0.55:
                                    confidence = "medium"
                                else:
                                    confidence = "low"
                                
                                print(f"  ✓ GESTURE TRIGGERED: {base_name} (score: {score:.4f}, confidence: {confidence})")
                                print(f"  → Cooldown active for 3 seconds...")
                                
                                last_gesture = base_name
                                last_gesture_time = current_time
                                
                                # Clear buffer after successful detection to start fresh
                                gesture_frames_data.clear()
                            
                        except Exception as e:
                            print(f"  Recognition error: {e}")
                        
                        last_recognition_time = current_time
                
                # Show cooldown status periodically
                elif in_cooldown and len(gesture_frames_data) % 30 == 0:
                    remaining = gesture_cooldown - (current_time - last_gesture_time)
                    if remaining > 0:
                        print(f"  ⏸ Cooldown: {remaining:.1f}s remaining...")

                time.sleep(0.016)  # ~60 FPS
        
        def stop_camera_pipeline():
            """Stop worker thread and release shared hub / local camera so Face ID can use the webcam."""
            nonlocal cap, camera_thread, camera_running, hub_acquired
            camera_running = False
            if camera_thread is not None:
                try:
                    camera_thread.join(timeout=2.5)
                except Exception:
                    pass
                camera_thread = None
            if hub_acquired and self.camera_hub is not None:
                try:
                    self.camera_hub.release(client_id)
                except Exception:
                    pass
                hub_acquired = False
            if cap is not None:
                try:
                    cap.release()
                except Exception:
                    pass
                cap = None

        def start_camera():
            """Start camera capture for this client"""
            nonlocal cap, camera_thread, camera_running, hub_acquired

            if self.camera_hub is not None:
                self.camera_hub.acquire(client_id)
                hub_acquired = True
                cap = None
                camera_running = True
                camera_thread = threading.Thread(target=camera_loop, daemon=True)
                camera_thread.start()
                return True

            cam_idx = int(os.environ.get("GESTURE_CAMERA", "0"))
            cap = cv2.VideoCapture(cam_idx)
            if not cap.isOpened():
                print(f"Error: Could not open camera index {cam_idx} for client {addr} (set GESTURE_CAMERA)")
                return False

            camera_running = True
            camera_thread = threading.Thread(target=camera_loop, daemon=True)
            camera_thread.start()
            return True
        
        def process_command(command):
            """Process commands from C# client"""
            nonlocal is_tracking, gesture_frames_data, last_gesture, last_gesture_time

            if command == "START_TRACKING":
                # Hub mode keeps cap=None forever — use camera_running, not cap.
                if not camera_running:
                    if not start_camera():
                        return {"status": "error", "message": "Failed to start camera"}

                is_tracking = True
                gesture_frames_data = []
                return {"status": "ok", "message": "Tracking started"}

            elif command == "STOP_TRACKING":
                is_tracking = False
                stop_camera_pipeline()
                return {"status": "ok", "message": "Tracking stopped", "frames": len(gesture_frames_data)}
            
            elif command == "RECOGNIZE":
                # With sliding window, recognition happens continuously in camera_loop
                # This command just returns the last detected gesture
                
                current_time = time.time()
                
                # Check if we're in cooldown
                if (current_time - last_gesture_time) < gesture_cooldown:
                    cooldown_remaining = gesture_cooldown - (current_time - last_gesture_time)
                    return {
                        "status": "cooldown",
                        "gesture": None,
                        "score": 0.0,
                        "confidence": "cooldown",
                        "cooldown_remaining": round(cooldown_remaining, 1),
                        "message": f"Cooldown active ({cooldown_remaining:.1f}s remaining)"
                    }
                
                if last_gesture is None:
                    return {
                        "status": "ok",
                        "gesture": None,
                        "score": 0.0,
                        "confidence": "none",
                        "message": "No gesture detected yet"
                    }
                
                # Check if gesture is recent (within last 3 seconds)
                if (current_time - last_gesture_time) > 3.0:
                    return {
                        "status": "ok",
                        "gesture": None,
                        "score": 0.0,
                        "confidence": "stale",
                        "message": "Last gesture too old"
                    }
                
                # Return last detected gesture (only if confidence was > 0.4)
                return {
                    "status": "ok",
                    "gesture": last_gesture,
                    "score": 1.0,  # Score not stored in continuous mode
                    "confidence": "high",
                    "message": f"Last gesture: {last_gesture}"
                }
            
            elif command == "RESET":
                gesture_frames_data = []
                is_tracking = False
                last_gesture = None
                last_gesture_time = 0
                stop_camera_pipeline()
                return {"status": "ok", "message": "Reset complete"}

            elif command == "STATUS":
                current_time = time.time()
                in_cooldown = (current_time - last_gesture_time) < gesture_cooldown
                cooldown_remaining = max(0, gesture_cooldown - (current_time - last_gesture_time))
                
                status_info = {
                    "status": "ok",
                    "tracking": is_tracking,
                    "frames": len(gesture_frames_data),
                    "templates": len(self.recognizer.templates) if self.recognizer.templates else 0,
                    "last_gesture": last_gesture,
                    "sliding_window": len(gesture_frames_data) >= MAX_WINDOW_FRAMES,
                    "window_size": f"{len(gesture_frames_data)}/{MAX_WINDOW_FRAMES}",
                    "in_cooldown": in_cooldown,
                    "cooldown_remaining": round(cooldown_remaining, 1),
                }
                # Only print if tracking and has significant frames (reduce spam)
                if is_tracking and len(gesture_frames_data) > 0 and len(gesture_frames_data) % 30 == 0:
                    if in_cooldown:
                        print(f"  INFO: Cooldown active ({cooldown_remaining:.1f}s remaining)")
                    else:
                        print(f"  INFO: Tracking: {len(gesture_frames_data)} frames collected")
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
            stop_camera_pipeline()

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
    print("via socket communication on 127.0.0.1:5001\n")
    print("Tip: run python/server/museum_vision_server.py for one shared camera with gaze + YOLO.\n")

    service = GestureRecognitionService(host="127.0.0.1", port=5001, camera_hub=None)
    
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
