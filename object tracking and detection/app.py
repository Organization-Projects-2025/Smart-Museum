import cv2
import numpy as np
from ultralytics import YOLO
from collections import defaultdict
import socket
import threading
import json

# ── Object-Swipe TCP Server (port 5005) ──────────────────────────────────────
# Completely separate from the hand-gesture service on port 5001.
# Publishes "objectswipeleft", "objectswiperight", "objectswipeup" to C#.

_obj_gesture = None
_obj_visible = False          # True while the watch is in frame
_obj_gesture_lock = threading.Lock()

def set_object_gesture(name):
    global _obj_gesture
    with _obj_gesture_lock:
        _obj_gesture = name

def _obj_tcp_server():
    """Minimal GestureClient-compatible TCP server for object-swipe events."""
    global _obj_gesture
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        srv.bind(("127.0.0.1", 5005))
        srv.listen(5)
        print("[ObjectSwipe] TCP server listening on port 5005")
    except Exception as e:
        print(f"[ObjectSwipe] Could not bind port 5005: {e}")
        return
    while True:
        try:
            conn, _ = srv.accept()
            buf = ""
            while True:
                data = conn.recv(4096)
                if not data:
                    break
                buf += data.decode("utf-8")
                while "\n" in buf:
                    line, buf = buf.split("\n", 1)
                    cmd = line.strip()
                    if cmd == "STATUS":
                        with _obj_gesture_lock:
                            g = _obj_gesture
                            vis = _obj_visible
                        resp = {"status": "ok", "tracking": True,
                                "last_gesture": g, "frames_collected": 60,
                                "templates": 4, "waiting_for_motion": False,
                                "capturing": True,
                                "object_visible": vis}
                    elif cmd == "RECOGNIZE":
                        with _obj_gesture_lock:
                            g = _obj_gesture
                            _obj_gesture = None
                        resp = {"status": "ok", "gesture": g,
                                "score": 1.0, "confidence": "high"}
                    else:  # PING, START_TRACKING, STOP_TRACKING, RESET, etc.
                        resp = {"status": "ok"}
                    try:
                        conn.sendall((json.dumps(resp) + "\n").encode("utf-8"))
                    except Exception:
                        break
        except Exception:
            pass


def main():
    # Start the object-swipe TCP bridge in the background (doesn't touch port 5001)
    threading.Thread(target=_obj_tcp_server, daemon=True).start()
    print("Loading YOLO11 Small model (great balance of speed and accuracy)...")
    # Load the YOLO11 Small model (smarter than Nano, but still much faster than Medium)
    model = YOLO("yolo11s.pt")
    
    print("Opening webcam...")
    cap = cv2.VideoCapture(0)
    
    if not cap.isOpened():
        print("Error: Could not open webcam.")
        return
        
    print("Starting object tracking and detection...")
    print("Press 'q' to quit the application.")
    
    # Store the tracking history globally so it doesn't reset when YOLO loses the ID
    global_track = []
    
    # Store the latest recognized gesture to display it
    gesture_state = {"text": "", "timer": 0}
    
    while True:
        ret, frame = cap.read()
        if not ret:
            print("Failed to grab frame.")
            break
            
        # Run YOLO inference with tracking enabled
        # classes=[74] filters exclusively for clocks/watches (74)
        # conf=0.15 allows the tracker to hold onto the object even when it gets blurry
        # tracker="bytetrack.yaml" uses the ByteTrack algorithm which is much better at continuous tracking
        results = model.track(frame, persist=True, verbose=False, classes=[74], conf=0.15, tracker="bytetrack.yaml")
        
        # The plot() method automatically draws the bounding boxes and labels
        annotated_frame = results[0].plot()
        
        # --- GESTURE TRACKING LOGIC ---
        # Update visibility flag so C# can prioritize object over hand gestures
        object_detected = results[0].boxes is not None and len(results[0].boxes) > 0
        with _obj_gesture_lock:
            _obj_visible = object_detected

        # If any objects were found, we just take the first one (assuming only 1 watch)
        if object_detected:
            box = results[0].boxes.xywh.cpu()[0] # x_center, y_center, width, height
            center_x = float(box[0])
            center_y = float(box[1])
            
            global_track.append((center_x, center_y))
            
            # Keep up to 60 frames (roughly 2-3 seconds of movement history)
            if len(global_track) > 60:
                global_track.pop(0)
            
            # Check for swipe if we have enough history frames
            if len(global_track) >= 5:
                # Calculate movement from the oldest recorded position
                dx = global_track[-1][0] - global_track[0][0]
                dy = global_track[-1][1] - global_track[0][1]
                
                # Base threshold was 150. Increased by 30% is 195.
                THRESHOLD = 195
                
                # Check which direction the movement is strongest in
                if abs(dx) > abs(dy):
                    # Horizontal swipe
                    if dx > THRESHOLD: 
                        gesture_state["text"] = "Swipe Right Detected!"
                        gesture_state["timer"] = 30
                        set_object_gesture("objectswiperight")
                        global_track.clear()
                    elif dx < -THRESHOLD: 
                        gesture_state["text"] = "Swipe Left Detected!"
                        gesture_state["timer"] = 30
                        set_object_gesture("objectswipeleft")
                        global_track.clear()
                else:
                    # Vertical swipe (in OpenCV, negative Y is UP, positive Y is DOWN)
                    if dy > THRESHOLD:
                        gesture_state["text"] = "Swipe Down Detected!"
                        gesture_state["timer"] = 30
                        global_track.clear()
                    elif dy < -THRESHOLD:
                        gesture_state["text"] = "Swipe Up Detected!"
                        gesture_state["timer"] = 30
                        set_object_gesture("objectswipeup")
                        global_track.clear()
                        
        # Display the gesture state bar/text on the top right
        if gesture_state["timer"] > 0:
            # Draw a dark background bar behind the text
            overlay = annotated_frame.copy()
            cv2.rectangle(overlay, (annotated_frame.shape[1] - 350, 10), (annotated_frame.shape[1] - 10, 60), (0, 0, 0), -1)
            cv2.addWeighted(overlay, 0.6, annotated_frame, 0.4, 0, annotated_frame)
            
            # Put the green text over it
            cv2.putText(annotated_frame, gesture_state["text"], (annotated_frame.shape[1] - 330, 43),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2, cv2.LINE_AA)
            gesture_state["timer"] -= 1
            
        # Display the resulting frame
        cv2.imshow("YOLO11 Object Tracking & Detection", annotated_frame)
        
        # Break the loop if 'q' is pressed
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break
            
    # Clean up
    cap.release()
    cv2.destroyAllWindows()

if __name__ == "__main__":
    main()