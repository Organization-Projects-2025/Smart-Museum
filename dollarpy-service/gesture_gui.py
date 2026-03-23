"""
GUI Application for Testing Smart Museum Gesture Recognition
"""
import os
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import cv2
import mediapipe as mp
from PIL import Image, ImageTk
from dollarpy import Point
from gesture_recognizer import SmartMuseumGestureRecognizer
import threading

class GestureRecognitionGUI:
    def __init__(self, root):
        self.root = root
        self.root.title("Smart Museum - Gesture Recognition Tester")
        self.root.geometry("1000x700")
        
        # Initialize recognizer (will use default path in dollarpy-service folder)
        self.recognizer = SmartMuseumGestureRecognizer()
        
        # MediaPipe setup - match template building and service configuration
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,  # Single hand for consistency
            min_detection_confidence=0.6,  # Match service
            min_tracking_confidence=0.6  # Match service
        )
        
        # Video capture
        self.cap = None
        self.is_running = False
        self.is_recording = False
        self.recorded_points = []
        
        self.setup_ui()
        
        # Auto-load templates if they exist
        self.auto_load_templates()
    
    def setup_ui(self):
        # Top control panel
        control_frame = ttk.Frame(self.root, padding="10")
        control_frame.pack(side=tk.TOP, fill=tk.X)
        
        # Build/Load templates section
        ttk.Label(control_frame, text="Templates:", font=('Arial', 10, 'bold')).pack(side=tk.LEFT, padx=5)
        
        ttk.Button(control_frame, text="Build from Videos", 
                   command=self.build_templates).pack(side=tk.LEFT, padx=5)
        
        ttk.Button(control_frame, text="Load Templates", 
                   command=self.load_templates).pack(side=tk.LEFT, padx=5)
        
        ttk.Separator(control_frame, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=10)
        
        # Camera controls
        self.start_btn = ttk.Button(control_frame, text="Start Camera", 
                                     command=self.start_camera)
        self.start_btn.pack(side=tk.LEFT, padx=5)
        
        self.stop_btn = ttk.Button(control_frame, text="Stop Camera", 
                                    command=self.stop_camera, state=tk.DISABLED)
        self.stop_btn.pack(side=tk.LEFT, padx=5)
        
        ttk.Separator(control_frame, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=10)
        
        # Recording controls
        self.record_btn = ttk.Button(control_frame, text="Start Recording Gesture", 
                                      command=self.toggle_recording, state=tk.DISABLED)
        self.record_btn.pack(side=tk.LEFT, padx=5)
        
        self.recognize_btn = ttk.Button(control_frame, text="Recognize", 
                                        command=self.recognize_gesture, state=tk.DISABLED)
        self.recognize_btn.pack(side=tk.LEFT, padx=5)
        
        ttk.Separator(control_frame, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=10)
        
        # Real-time detection
        self.realtime_btn = ttk.Button(control_frame, text="Start Real-Time Detection", 
                                        command=self.toggle_realtime, state=tk.DISABLED)
        self.realtime_btn.pack(side=tk.LEFT, padx=5)
        
        ttk.Separator(control_frame, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=10)
        
        # FPS control
        ttk.Label(control_frame, text="FPS:", font=('Arial', 9)).pack(side=tk.LEFT, padx=(5, 2))
        self.fps_var = tk.StringVar(value="30")
        self.fps_combo = ttk.Combobox(control_frame, textvariable=self.fps_var, 
                                       values=["10", "15", "20", "25", "30", "60"], 
                                       width=5, state="readonly")
        self.fps_combo.pack(side=tk.LEFT, padx=2)
        self.fps_combo.bind("<<ComboboxSelected>>", self.on_fps_changed)
        
        # Real-time detection state
        self.realtime_active = False
        self.realtime_points = []
        self.realtime_last_recognition = 0
        self.realtime_cooldown = 2.0  # 2 seconds between recognitions
        self.realtime_min_points = 80  # Minimum points before recognition (increased from 60)
        self.realtime_max_points = 80  # Maximum points to collect (changed from 180)
        self.realtime_no_hand_frames = 0  # Count frames without hand detection
        self.frame_delay = 33  # milliseconds between frames (default ~30 FPS)
        
        # Main content area
        content_frame = ttk.Frame(self.root)
        content_frame.pack(side=tk.TOP, fill=tk.BOTH, expand=True, padx=10, pady=10)
        
        # Left side - Video feed
        video_frame = ttk.LabelFrame(content_frame, text="Camera Feed", padding="10")
        video_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)
        
        self.video_label = ttk.Label(video_frame)
        self.video_label.pack()
        
        # Right side - Results and info
        info_frame = ttk.Frame(content_frame, width=300)
        info_frame.pack(side=tk.RIGHT, fill=tk.BOTH, padx=(10, 0))
        
        # Status
        status_frame = ttk.LabelFrame(info_frame, text="Status", padding="10")
        status_frame.pack(fill=tk.X, pady=(0, 10))
        
        self.status_label = ttk.Label(status_frame, text="Ready", 
                                       font=('Arial', 10), foreground='blue')
        self.status_label.pack()
        
        # Recognition result
        result_frame = ttk.LabelFrame(info_frame, text="Recognition Result", padding="10")
        result_frame.pack(fill=tk.X, pady=(0, 10))
        
        self.result_label = ttk.Label(result_frame, text="No gesture recognized", 
                                       font=('Arial', 12, 'bold'), wraplength=250)
        self.result_label.pack()
        
        self.score_label = ttk.Label(result_frame, text="Score: 0.00", 
                                      font=('Arial', 10))
        self.score_label.pack()
        
        # Instructions
        instructions_frame = ttk.LabelFrame(info_frame, text="Instructions", padding="10")
        instructions_frame.pack(fill=tk.BOTH, expand=True)
        
        instructions = """
1. Build templates from videos or load existing ones

2. Start the camera

3. Click "Start Recording Gesture"

4. Perform a gesture with your hand

5. Click "Stop Recording Gesture"

6. Click "Recognize" to identify the gesture

Gestures are automatically 
detected from video folders.

System tracks 6 hand landmarks
for accurate recognition.
        """
        
        ttk.Label(instructions_frame, text=instructions, 
                  justify=tk.LEFT, wraplength=250).pack()
    
    def build_templates(self):
        # Use default folder path
        script_dir = os.path.dirname(os.path.abspath(__file__))
        default_folder = os.path.join(os.path.dirname(script_dir), "Public", "Data", "Videos", "Moves")
        
        folder = filedialog.askdirectory(
            title="Select Moves Folder",
            initialdir=default_folder
        )
        
        if folder:
            self.status_label.config(text="Building templates...", foreground='orange')
            self.root.update()
            
            success = self.recognizer.build_templates(folder)
            
            if success:
                self.recognizer.save_templates()
                self.status_label.config(text="Templates built successfully!", foreground='green')
                messagebox.showinfo("Success", 
                                    f"Created {len(self.recognizer.templates)} gesture templates")
            else:
                self.status_label.config(text="Failed to build templates", foreground='red')
                messagebox.showerror("Error", "Failed to build templates from videos")
    
    def load_templates(self):
        success = self.recognizer.load_templates()
        
        if success:
            self.status_label.config(text="Templates loaded successfully!", foreground='green')
            messagebox.showinfo("Success", 
                                f"Loaded {len(self.recognizer.templates)} gesture templates")
        else:
            self.status_label.config(text="Failed to load templates", foreground='red')
            messagebox.showerror("Error", 
                                 "Templates file not found. Please build templates first.")
    
    def on_fps_changed(self, event=None):
        """Update frame delay when FPS is changed"""
        fps = int(self.fps_var.get())
        self.frame_delay = int(1000 / fps)  # Convert FPS to milliseconds
        print(f"FPS changed to {fps} (delay: {self.frame_delay}ms)")
    
    def start_camera(self):
        if self.recognizer.recognizer is None:
            messagebox.showwarning("Warning", 
                                   "Please build or load templates first!")
            return
        
        self.cap = cv2.VideoCapture(0)
        if not self.cap.isOpened():
            messagebox.showerror("Error", "Could not open camera")
            return
        
        self.is_running = True
        self.start_btn.config(state=tk.DISABLED)
        self.stop_btn.config(state=tk.NORMAL)
        self.record_btn.config(state=tk.NORMAL)
        self.realtime_btn.config(state=tk.NORMAL)
        self.fps_combo.config(state=tk.DISABLED)  # Lock FPS during capture
        self.status_label.config(text="Camera running", foreground='green')
        
        self.update_frame()
    
    def stop_camera(self):
        self.is_running = False
        self.is_recording = False
        self.realtime_active = False
        
        if self.cap:
            self.cap.release()
        
        self.start_btn.config(state=tk.NORMAL)
        self.stop_btn.config(state=tk.DISABLED)
        self.record_btn.config(state=tk.DISABLED)
        self.recognize_btn.config(state=tk.DISABLED)
        self.realtime_btn.config(state=tk.DISABLED)
        self.fps_combo.config(state="readonly")  # Unlock FPS when stopped
        self.status_label.config(text="Camera stopped", foreground='blue')
        
        # Clear video label
        self.video_label.config(image='')
    
    def toggle_recording(self):
        if not self.is_recording:
            self.is_recording = True
            self.recorded_points = []
            self.record_btn.config(text="Stop Recording Gesture")
            self.recognize_btn.config(state=tk.DISABLED)
            self.status_label.config(text="Recording gesture...", foreground='orange')
        else:
            self.is_recording = False
            self.record_btn.config(text="Start Recording Gesture")
            self.recognize_btn.config(state=tk.NORMAL)
            self.status_label.config(text=f"Recorded {len(self.recorded_points)} points", 
                                     foreground='blue')
    
    def toggle_realtime(self):
        if not self.realtime_active:
            self.realtime_active = True
            self.realtime_points = []
            self.realtime_last_recognition = 0
            self.realtime_btn.config(text="Stop Real-Time Detection")
            self.record_btn.config(state=tk.DISABLED)
            self.recognize_btn.config(state=tk.DISABLED)
            self.status_label.config(text="Real-time detection active", foreground='green')
            self.result_label.config(text="Waiting for gesture...", foreground='blue')
        else:
            self.realtime_active = False
            self.realtime_points = []
            self.realtime_btn.config(text="Start Real-Time Detection")
            self.record_btn.config(state=tk.NORMAL)
            self.status_label.config(text="Real-time detection stopped", foreground='blue')
            self.result_label.config(text="No gesture recognized", foreground='black')
    
    def recognize_gesture(self):
        if len(self.recorded_points) < 2:
            messagebox.showwarning("Warning", "Not enough points recorded. Please record a gesture first.")
            return
        
        # Debug: Show point distribution
        print(f"\nDebug: Recorded {len(self.recorded_points)} points")
        
        # Check if points have movement (not all at same location) BEFORE recognition
        if len(self.recorded_points) > 1:
            x_coords = [p.x for p in self.recorded_points]
            y_coords = [p.y for p in self.recorded_points]
            
            x_range = max(x_coords) - min(x_coords)
            y_range = max(y_coords) - min(y_coords)
            
            print(f"Debug: X range = {x_range:.1f}px, Y range = {y_range:.1f}px")
            print(f"Debug: X coords: min={min(x_coords):.1f}, max={max(x_coords):.1f}")
            print(f"Debug: Y coords: min={min(y_coords):.1f}, max={max(y_coords):.1f}")
            
            # Need at least 20 pixels of movement (increased threshold for multi-point)
            if x_range < 20 and y_range < 20:
                messagebox.showwarning("Warning", 
                    f"Not enough movement detected!\n\n"
                    f"Movement: X={x_range:.1f}px, Y={y_range:.1f}px\n"
                    f"Required: At least 20px in any direction\n\n"
                    f"Tips:\n"
                    f"• Move your WHOLE HAND while recording\n"
                    f"• Try larger gestures (swipe across screen)\n"
                    f"• Keep your hand visible to camera")
                self.result_label.config(text="No movement detected", foreground='red')
                self.score_label.config(text=f"Movement: {max(x_range, y_range):.1f}px")
                return
        
        try:
            gesture_name, score = self.recognizer.recognize(self.recorded_points)
            
            print(f"Debug: Recognition result = {gesture_name}, score = {score}")
            
            # Check if recognition returned an error message
            if score == 0.0 and ("error" in gesture_name.lower() or "no movement" in gesture_name.lower()):
                self.result_label.config(text=gesture_name, foreground='red')
                self.score_label.config(text="Score: 0.00")
                return
            
            base_name = self.recognizer.get_gesture_base_name(gesture_name)
            
            self.result_label.config(text=f"{base_name.replace('_', ' ').title()}")
            self.score_label.config(text=f"Score: {score:.4f}")
            
            if score > 0.7:
                self.result_label.config(foreground='green')
            elif score > 0.5:
                self.result_label.config(foreground='orange')
            else:
                self.result_label.config(foreground='red')
        except ZeroDivisionError:
            messagebox.showerror("Error", 
                "Recognition failed. Please ensure your gesture has clear movement.")
            self.result_label.config(text="Recognition failed", foreground='red')
            self.score_label.config(text="Score: 0.00")
        except Exception as e:
            print(f"Debug: Exception during recognition: {e}")
            messagebox.showerror("Error", f"Recognition failed: {str(e)}")
            self.result_label.config(text="Error", foreground='red')
            self.score_label.config(text="Score: 0.00")
    
    def update_frame(self):
        if not self.is_running:
            return
        
        ret, frame = self.cap.read()
        if ret:
            frame = cv2.resize(frame, (640, 480))
            # Removed flip - keep camera natural orientation
            
            # Process with MediaPipe
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.hands.process(rgb_frame)
            
            if results.multi_hand_landmarks:
                image_height, image_width, _ = frame.shape
                
                for hand_landmarks in results.multi_hand_landmarks:
                    # Draw hand landmarks
                    self.mp_drawing.draw_landmarks(
                        frame, hand_landmarks, self.mp_hands.HAND_CONNECTIONS)
                    
                    # Track key landmarks for recording OR real-time detection
                    if self.is_recording or self.realtime_active:
                        # Track wrist (0), thumb tip (4), index tip (8), middle tip (12), ring tip (16), pinky tip (20)
                        key_landmarks = [0, 4, 8, 12, 16, 20]
                        
                        # Choose which points list to use
                        points_list = self.recorded_points if self.is_recording else self.realtime_points
                        
                        # Use current point count as stroke ID so all landmarks in same frame are grouped
                        stroke_id = len(points_list) // 6 + 1
                        
                        for landmark_id in key_landmarks:
                            landmark = hand_landmarks.landmark[landmark_id]
                            x = int(landmark.x * image_width)
                            y = int(landmark.y * image_height)
                            # All points in same frame get same stroke ID
                            points_list.append(Point(x, y, stroke_id))
                            
                            # Draw recording indicator on key points
                            if landmark_id == 8:  # Highlight index finger
                                cv2.circle(frame, (x, y), 10, (0, 0, 255), -1)
                            else:
                                cv2.circle(frame, (x, y), 5, (255, 0, 0), -1)
            
            # Real-time detection logic
            if self.realtime_active:
                import time
                current_time = time.time()
                
                # Check if hand is detected in this frame
                hand_detected = results.multi_hand_landmarks is not None
                
                if hand_detected:
                    self.realtime_no_hand_frames = 0
                else:
                    self.realtime_no_hand_frames += 1
                
                # Trigger recognition when:
                # 1. We have 80+ points (enough for recognition)
                # 2. Cooldown has passed
                should_recognize = (
                    len(self.realtime_points) >= self.realtime_min_points and
                    (current_time - self.realtime_last_recognition) > self.realtime_cooldown
                )
                
                if should_recognize:
                    # Try to recognize
                    try:
                        print(f"\n→ Real-time recognition with {len(self.realtime_points)} points")
                        gesture_name, score = self.recognizer.recognize(self.realtime_points)
                        
                        # Show result if confidence is above minimum threshold (0.13)
                        if score > 0.13:
                            base_name = self.recognizer.get_gesture_base_name(gesture_name)
                            
                            # Update UI - always show the best match
                            self.result_label.config(text=f"✓ {base_name.replace('_', ' ').title()}")
                            self.score_label.config(text=f"Score: {score:.4f}")
                            
                            # Color based on confidence
                            if score > 0.7:
                                self.result_label.config(foreground='green')
                            elif score > 0.4:
                                self.result_label.config(foreground='orange')
                            else:
                                self.result_label.config(foreground='red')
                            
                            print(f"✓ DETECTED: {base_name} (score: {score:.4f})")
                        else:
                            print(f"✗ Too low confidence: {score:.4f} (minimum: 0.13)")
                            self.result_label.config(text="No clear gesture", foreground='gray')
                            self.score_label.config(text=f"Score: {score:.4f}")
                        
                        # Reset for next gesture
                        self.realtime_last_recognition = current_time
                        self.realtime_points = []
                        self.realtime_no_hand_frames = 0
                        
                    except Exception as e:
                        print(f"Real-time recognition error: {e}")
                        self.realtime_points = []
                        self.realtime_no_hand_frames = 0
                
                # Reset if we reach max points (gesture complete)
                elif len(self.realtime_points) >= self.realtime_max_points:
                    print(f"→ Resetting: reached max points ({len(self.realtime_points)})")
                    self.realtime_points = []
                    self.realtime_no_hand_frames = 0
                
                # Add real-time indicator
                cv2.putText(frame, "REAL-TIME DETECTION", (10, 30), 
                            cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                cv2.putText(frame, f"Points: {len(self.realtime_points)}", (10, 60), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
                
                # Show status
                if len(self.realtime_points) < self.realtime_min_points:
                    status_text = "Collecting..."
                elif self.realtime_no_hand_frames > 0:
                    status_text = f"Recognizing... ({self.realtime_no_hand_frames}/5)"
                else:
                    status_text = "Keep going..."
                cv2.putText(frame, status_text, (10, 90), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            
            # Add recording indicator
            elif self.is_recording:
                cv2.putText(frame, "RECORDING", (10, 30), 
                            cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
                cv2.putText(frame, f"Points: {len(self.recorded_points)}", (10, 60), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 255), 2)
            
            # Convert to PhotoImage
            frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            img = Image.fromarray(frame_rgb)
            imgtk = ImageTk.PhotoImage(image=img)
            
            self.video_label.imgtk = imgtk
            self.video_label.config(image=imgtk)
        
        self.root.after(self.frame_delay, self.update_frame)
    
    def on_closing(self):
        self.stop_camera()
        self.root.destroy()
    
    def auto_load_templates(self):
        """Automatically load templates on startup if they exist"""
        import os
        if os.path.exists(self.recognizer.templates_file):
            try:
                self.recognizer.load_templates()
                self.status_label.config(text=f"Auto-loaded {len(self.recognizer.templates)} templates", 
                                         foreground='green')
            except Exception as e:
                self.status_label.config(text="Ready - No templates loaded", foreground='blue')

def main():
    root = tk.Tk()
    app = GestureRecognitionGUI(root)
    root.protocol("WM_DELETE_WINDOW", app.on_closing)
    root.mainloop()

if __name__ == "__main__":
    main()
