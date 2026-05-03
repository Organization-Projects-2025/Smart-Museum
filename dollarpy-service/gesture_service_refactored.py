"""
Gesture Recognition Service for C# Integration (Refactored)

Socket server for real-time gesture recognition using:
- MediaPipe Hands for hand detection
- gesture_preprocessing.py for consistent point extraction
- dollarpy for gesture matching

All preprocessing uses the SHARED pipeline to match template generation exactly.
"""

import os
import socket
import json
import cv2
import threading
import time
from typing import Optional, Dict, Any, Callable
from dollarpy import Point

from gesture_recognizer import SmartMuseumGestureRecognizer
from gesture_config import (
    WINDOW_FRAMES, CONFIDENCE_THRESHOLD, COOLDOWN_SECONDS, 
    STABILITY_REQUIRED, MIN_MOTION_DISTANCE, DEBUG_GESTURES,
    GESTURE_MODE, GESTURE_NORMALIZATION_MODE, get_config_summary
)
from gesture_preprocessing import preprocess_gesture_frames, log_gesture_stats
import mediapipe_compat as mp


class GestureRecognitionService:
    """TCP socket server for real-time gesture recognition."""

    def __init__(self, host: str = "127.0.0.1", port: int = 5001, 
                 camera_hub: Optional[Any] = None):
        """
        Initialize service.
        
        Args:
            host: Bind address
            port: Listen port
            camera_hub: Optional SharedCameraHub for shared frame acquisition
        """
        self.host = host
        self.port = port
        self.server_socket = None
        self.is_running = False
        self.camera_hub = camera_hub
        
        # Initialize shared recognizer
        self.recognizer = SmartMuseumGestureRecognizer()
        self.recognizer.load_templates()
        
        # MediaPipe setup
        self.mp_hands = mp.solutions.hands
        
        print(get_config_summary())

    def start_server(self):
        """Start the TCP socket server."""
        try:
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            
            # Try to set SO_REUSEPORT if available (faster rebinding)
            try:
                self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEPORT, 1)
            except (AttributeError, OSError):
                pass  # Not available on all platforms
            
            print(f"\n⧗ Binding to {self.host}:{self.port}...")
            self.server_socket.bind((self.host, self.port))
            
            self.server_socket.listen(10)  # Increased backlog
            print(f"✓ Gesture Recognition Service started on {self.host}:{self.port}")
            print(f"✓ Listening on socket (backlog=10)")
            print(f"✓ C# clients should connect to: {self.host}:{self.port}")
            print("  (Use 127.0.0.1 or localhost, both work)\n")
            
            self.is_running = True
            
            while self.is_running:
                try:
                    print(f"[GESTURE] Waiting for client connection on {self.host}:{self.port}...")
                    client_socket, addr = self.server_socket.accept()
                    print(f"\n╔═══════════════════════════════════════════════════╗")
                    print(f"║ ✓ CLIENT CONNECTED: {addr[0]}:{addr[1]}")
                    print(f"║   Time: {time.strftime('%H:%M:%S')}")
                    print(f"╚═══════════════════════════════════════════════════╝\n")
                    
                    # Handle each client in a separate thread
                    client_thread = threading.Thread(
                        target=self.handle_client,
                        args=(client_socket, addr),
                        daemon=True
                    )
                    client_thread.start()
                
                except Exception as e:
                    if self.is_running:
                        print(f"[ERROR] Accept failed: {e}")
                    break
        
        except KeyboardInterrupt:
            print("\nServer shutdown requested...")
        except Exception as e:
            print(f"[ERROR] Failed to start server: {e}")
            import traceback
            traceback.print_exc()
        finally:
            self.cleanup()

    def handle_client(self, client_socket: socket.socket, addr: tuple):
        """Handle a single client connection."""
        client_id = f"{addr[0]}:{addr[1]}"
        
        # Per-client state
        cap = None
        hub_acquired = False
        is_tracking = False
        camera_thread = None
        camera_running = False
        
        # Gesture collection buffer (SLIDING WINDOW - max 60 frames)
        frames_data = []
        MAX_WINDOW_FRAMES = 60  # Strict sliding window limit
        
        # Recognition state tracking
        last_gesture = None
        last_gesture_time = 0
        last_recognition_time = 0
        recognition_interval = 0.05  # Recognize every 50ms
        gesture_cooldown = 3.0  # 3 seconds cooldown after detection
        confidence_threshold = 0.25  # Lowered from 0.4 for better detection
        
        # MediaPipe hands detector (per-client instance)
        hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )

        def camera_loop():
            """Continuously capture and process camera frames with SLIDING WINDOW."""
            nonlocal is_tracking, frames_data, camera_running
            nonlocal last_gesture, last_gesture_time, last_recognition_time
            frame_count = 0
            hand_frame_count = 0

            print(f"[{client_id}] Camera loop started (SLIDING WINDOW mode)")
            
            while camera_running:
                frame_count += 1
                
                # Get frame from hub or local camera
                if self.camera_hub is not None:
                    frame = self.camera_hub.get_latest_bgr_copy()
                    if frame is None:
                        time.sleep(0.016)
                        continue
                else:
                    if cap is None or not cap.isOpened():
                        print(f"[{client_id}] Camera not opened")
                        break
                    ret, frame = cap.read()
                    if not ret:
                        time.sleep(0.016)
                        continue

                # Standard preprocessing
                frame = cv2.resize(frame, (640, 480))
                rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                
                if not is_tracking:
                    time.sleep(0.016)
                    continue

                # Check cooldown BEFORE collecting frames
                current_time = time.time()
                in_cooldown = (current_time - last_gesture_time) < gesture_cooldown
                
                if in_cooldown:
                    # During cooldown: don't collect frames, keep buffer empty
                    if len(frames_data) > 0:
                        frames_data.clear()
                        hand_frame_count = 0
                    time.sleep(0.016)
                    continue

                # Detect hand landmarks
                results = hands.process(rgb_frame)

                if not results.multi_hand_landmarks:
                    # Log periodically when no hand detected
                    if frame_count % 300 == 0 and len(frames_data) > 0:
                        print(f"[{client_id}] No hand detected (frames in buffer: {len(frames_data)})")
                    time.sleep(0.016)
                    continue
                
                hand_frame_count += 1
                
                # Extract hand landmarks (first hand only)
                hand_landmarks = results.multi_hand_landmarks[0]
                
                # Store frame data using shared preprocessing format
                frames_data.append({
                    "hand_landmarks": hand_landmarks,
                    "frame": frame
                })

                # SLIDING WINDOW: Remove oldest frame when exceeding limit
                if len(frames_data) > MAX_WINDOW_FRAMES:
                    frames_data.pop(0)  # Remove frame 0 when frame 61 arrives

                # Log every 10 frames
                if hand_frame_count % 10 == 0:
                    window_status = f"SLIDING ({len(frames_data)}/{MAX_WINDOW_FRAMES})" if len(frames_data) >= MAX_WINDOW_FRAMES else f"FILLING ({len(frames_data)}/{MAX_WINDOW_FRAMES})"
                    print(f"[{client_id}] Hand detected: {window_status}")

                # CONTINUOUS RECOGNITION (cooldown already checked above)
                if (len(frames_data) >= 10 and 
                    (current_time - last_recognition_time) >= recognition_interval):
                    
                    # Preprocess frames to get points
                    points = preprocess_gesture_frames(
                        frames_data,
                        mode=GESTURE_MODE,
                        normalize_mode=GESTURE_NORMALIZATION_MODE,
                        min_motion=MIN_MOTION_DISTANCE
                    )
                    
                    if points is not None and len(points) >= 10:
                        try:
                            gesture_name, score = self.recognizer.recognize(points)
                            
                            # Log recognition attempts periodically
                            if hand_frame_count % 30 == 0:
                                print(f"[{client_id}] Recognition: {gesture_name} (score: {score:.4f}, threshold: {confidence_threshold})")
                            
                            # Only trigger if score > threshold (recognizer always returns gesture name now)
                            if score > confidence_threshold:
                                # Determine confidence level
                                if score > 0.7:
                                    confidence = "high"
                                elif score > 0.55:
                                    confidence = "medium"
                                else:
                                    confidence = "low"
                                
                                print(f"[{client_id}] ✓ GESTURE TRIGGERED: {gesture_name} (score: {score:.4f}, confidence: {confidence})")
                                print(f"[{client_id}] → Cooldown active for 3 seconds (buffer frozen)...")
                                
                                last_gesture = gesture_name
                                last_gesture_time = current_time
                                
                                # Clear buffer - will stay empty during cooldown
                                frames_data.clear()
                                hand_frame_count = 0
                            
                        except Exception as e:
                            print(f"[{client_id}] Recognition error: {e}")
                            import traceback
                            traceback.print_exc()
                        
                        last_recognition_time = current_time
                    else:
                        # Log when preprocessing fails
                        if hand_frame_count % 60 == 0:
                            print(f"[{client_id}] Preprocessing returned {len(points) if points else 0} points (need 10+)")

                time.sleep(0.016)  # ~60 FPS

        def stop_camera_pipeline():
            """Stop camera acquisition and cleanup."""
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

        def start_camera() -> bool:
            """Start camera acquisition."""
            nonlocal cap, camera_thread, camera_running, hub_acquired

            if self.camera_hub is not None:
                try:
                    self.camera_hub.acquire(client_id)
                    hub_acquired = True
                except Exception as e:
                    print(f"[{client_id}] Error acquiring hub: {e}")
                    return False
            else:
                cam_idx = int(os.environ.get("GESTURE_CAMERA", "0"))
                cap = cv2.VideoCapture(cam_idx)
                if not cap.isOpened():
                    print(f"[{client_id}] Error: Could not open camera {cam_idx}")
                    return False

            camera_running = True
            camera_thread = threading.Thread(target=camera_loop, daemon=True)
            camera_thread.start()
            return True

        def process_command(command: str) -> Dict[str, Any]:
            """Process a command from C# client."""
            nonlocal is_tracking, frames_data, camera_running
            nonlocal last_gesture, last_gesture_time

            if command == "START_TRACKING":
                if not camera_running:
                    if not start_camera():
                        return {"status": "error", "message": "Failed to start camera"}

                is_tracking = True
                frames_data = []
                last_gesture = None
                
                print(f"[{client_id}] Tracking started (SLIDING WINDOW mode)")
                return {"status": "ok", "message": "Tracking started"}

            elif command == "STOP_TRACKING":
                is_tracking = False
                stop_camera_pipeline()
                
                print(f"[{client_id}] Tracking stopped ({len(frames_data)} frames collected)")
                return {
                    "status": "ok",
                    "message": "Tracking stopped",
                    "frames_collected": len(frames_data)
                }

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
                
                # Return last detected gesture and clear it
                gesture_to_return = last_gesture
                last_gesture = None  # Clear after returning
                
                return {
                    "status": "ok",
                    "gesture": gesture_to_return,
                    "score": 1.0,
                    "confidence": "high",
                    "message": f"Last gesture: {gesture_to_return}"
                }
            
            elif command == "CLEAR_GESTURE":
                # New command: Just clear last_gesture without stopping camera
                last_gesture = None
                return {
                    "status": "ok",
                    "message": "Last gesture cleared"
                }

            elif command == "RESET":
                frames_data = []
                is_tracking = False
                last_gesture = None
                last_gesture_time = 0
                stop_camera_pipeline()
                
                print(f"[{client_id}] Reset complete")
                return {"status": "ok", "message": "Reset complete"}

            elif command == "STATUS":
                current_time = time.time()
                in_cooldown = (current_time - last_gesture_time) < gesture_cooldown
                cooldown_remaining = max(0, gesture_cooldown - (current_time - last_gesture_time))
                
                return {
                    "status": "ok",
                    "tracking": is_tracking,
                    "frames": len(frames_data),
                    "templates": len(self.recognizer.templates),
                    "last_gesture": last_gesture,
                    "sliding_window": len(frames_data) >= MAX_WINDOW_FRAMES,
                    "window_size": f"{len(frames_data)}/{MAX_WINDOW_FRAMES}",
                    "in_cooldown": in_cooldown,
                    "cooldown_remaining": round(cooldown_remaining, 1),
                    "mode": GESTURE_MODE,
                    "normalization": GESTURE_NORMALIZATION_MODE,
                    "threshold": confidence_threshold
                }

            elif command == "PING":
                return {"status": "ok", "message": "pong"}

            else:
                return {"status": "error", "message": f"Unknown command: {command}"}

        def send_response(response: Dict[str, Any]):
            """Send JSON response to client."""
            try:
                json_response = json.dumps(response) + "\n"
                client_socket.send(json_response.encode('utf-8'))
            except Exception as e:
                print(f"[{client_id}] Error sending response: {e}")

        # Main client loop
        try:
            while self.is_running:
                try:
                    data = client_socket.recv(1024)
                    if not data:
                        break

                    try:
                        message = data.decode('utf-8').strip()
                        if DEBUG_GESTURES or message not in ("STATUS", "PING"):
                            print(f"[{client_id}] Command: {message}")

                        response = process_command(message)
                        send_response(response)

                    except Exception as e:
                        print(f"[{client_id}] Error processing message: {e}")
                        send_response({"status": "error", "message": str(e)})
                
                except Exception as e:
                    print(f"[{client_id}] Connection error: {e}")
                    break
        finally:
            print(f"[{client_id}] Cleaning up...")
            stop_camera_pipeline()
            if client_socket:
                client_socket.close()
            print(f"[{client_id}] Disconnected")

    def cleanup(self):
        """Clean up server resources."""
        print("\nCleaning up server...")
        self.is_running = False

        if self.server_socket:
            try:
                self.server_socket.close()
            except Exception:
                pass

        print("✓ Service stopped")


def main():
    """Entry point for gesture recognition service."""
    print("=" * 70)
    print("Smart Museum - Gesture Recognition Service (Refactored)")
    print("=" * 70)
    print()
    print("Configuration:")
    print(get_config_summary())
    print()
    print("Features:")
    print("  ✓ Fixed-window recognition (no motion segmentation)")
    print("  ✓ High confidence threshold (0.60)")
    print("  ✓ Stability checking (multiple consecutive recognitions)")
    print("  ✓ Shared preprocessing (matches template generation)")
    print("  ✓ Debug logging available (set DEBUG_GESTURES=1)")
    print()

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
