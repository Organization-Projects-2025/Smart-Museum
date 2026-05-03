"""
GUI Application for Testing Smart Museum Gesture Recognition
"""
import os
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import cv2
from PIL import Image, ImageTk
from dollarpy import Point
from gesture_recognizer import SmartMuseumGestureRecognizer
from gesture_preprocessing import preprocess_gesture_frames
from gesture_config import GESTURE_MODE, GESTURE_NORMALIZATION_MODE, MIN_MOTION_DISTANCE, DEBUG_GESTURES
import threading
from copy import deepcopy
from dollarpy import Recognizer
# Use compatibility layer for mediapipe 0.10.30+
import mediapipe_compat as mp

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
        self.recorded_frames_data = []  # Store MediaPipe landmarks + frame, not raw Points
        
        # Real-time detection state
        self.realtime_active = False
        self.realtime_frames_data = []  # Store MediaPipe landmarks + frame, not raw Points
        self.realtime_min_points = 10  # Minimum points after preprocessing
        self.realtime_max_points = 150  # Max points before forced recognition
        self.realtime_last_recognition = 0
        self.realtime_cooldown = 0.5  # Cooldown between recognitions
        self.realtime_no_hand_frames = 0
        
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
        self.fps_var = tk.StringVar(value="60")
        self.fps_combo = ttk.Combobox(control_frame, textvariable=self.fps_var, 
                                       values=["10", "15", "20", "25", "30", "45", "60"], 
                                       width=5, state="readonly")
        self.fps_combo.pack(side=tk.LEFT, padx=2)
        self.fps_combo.bind("<<ComboboxSelected>>", self.on_fps_changed)
        
        # Real-time detection state is initialized in __init__ after recognizer setup
        self.realtime_no_hand_frames = 0  # Count frames without hand detection
        self.frame_delay = 16  # milliseconds between frames (default ~60 FPS)
        
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
                self._configure_realtime_point_window()
                messagebox.showinfo("Success", 
                                    f"Created {len(self.recognizer.templates)} gesture templates")
            else:
                self.status_label.config(text="Failed to build templates", foreground='red')
                messagebox.showerror("Error", "Failed to build templates from videos")
    
    def load_templates(self):
        success = self.recognizer.load_templates()
        
        if success:
            self.status_label.config(text="Templates loaded successfully!", foreground='green')
            self._configure_realtime_point_window()
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
        if not self.recognizer.templates:
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
            self.recorded_frames_data = []
            self.record_btn.config(text="Stop Recording Gesture")
            self.recognize_btn.config(state=tk.DISABLED)
            self.status_label.config(text="Recording gesture...", foreground='orange')
        else:
            self.is_recording = False
            self.record_btn.config(text="Start Recording Gesture")
            self.recognize_btn.config(state=tk.NORMAL)
            self.status_label.config(text=f"Recorded {len(self.recorded_frames_data)} frames", 
                                     foreground='blue')
    
    def toggle_realtime(self):
        if not self.realtime_active:
            self.realtime_active = True
            self.realtime_frames_data = []
            self.realtime_last_recognition = 0
            self.realtime_btn.config(text="Stop Real-Time Detection")
            self.record_btn.config(state=tk.DISABLED)
            self.recognize_btn.config(state=tk.DISABLED)
            self.status_label.config(text="Real-time detection active", foreground='green')
            self.result_label.config(text="Waiting for gesture...", foreground='blue')
        else:
            self.realtime_active = False
            self.realtime_frames_data = []
            self.realtime_btn.config(text="Start Real-Time Detection")
            self.record_btn.config(state=tk.NORMAL)
            self.status_label.config(text="Real-time detection stopped", foreground='blue')
            self.result_label.config(text="No gesture recognized", foreground='black')
    
    def recognize_gesture(self):
        if len(self.recorded_frames_data) < 2:
            messagebox.showwarning("Warning", "Not enough frames recorded. Please record a gesture first.")
            return
        
        # Debug: Show frame count
        print(f"\nDebug: Recorded {len(self.recorded_frames_data)} frames")
        
        # Use shared preprocessing pipeline (MUST match template preprocessing)
        points = preprocess_gesture_frames(
            self.recorded_frames_data,
            mode=GESTURE_MODE,
            normalize_mode=GESTURE_NORMALIZATION_MODE,
            min_motion=MIN_MOTION_DISTANCE
        )
        
        if points is None:
            messagebox.showwarning("Warning", 
                "Not enough movement detected!\n\n"
                "Tips:\n"
                "• Move your WHOLE HAND while recording\n"
                "• Try larger gestures (swipe across screen)\n"
                "• Keep your hand visible to camera")
            self.result_label.config(text="No movement detected", foreground='red')
            self.score_label.config(text="Score: 0.00")
            return
        
        try:
            gesture_name, score = self.recognizer.recognize(points)
            
            print(f"Debug: Recognition result = {gesture_name}, score = {score:.4f}")
            
            # Check if recognition failed
            if gesture_name is None:
                self.result_label.config(text="No match", foreground='red')
                self.score_label.config(text=f"Score: {score:.4f}")
                return
            
            self.result_label.config(text=f"{gesture_name.replace('_', ' ').title()}")
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
                    
                    # Store frame data for preprocessing (MUST match gesture_processor.py format)
                    if self.is_recording or self.realtime_active:
                        frames_data_list = self.recorded_frames_data if self.is_recording else self.realtime_frames_data
                        
                        # Store MediaPipe landmarks + frame (same format as gesture_processor.py)
                        frames_data_list.append({
                            "hand_landmarks": hand_landmarks,
                            "frame": frame
                        })
                        
                        # Draw recording indicator on index finger
                        index_tip = hand_landmarks.landmark[8]
                        x = int(index_tip.x * image_width)
                        y = int(index_tip.y * image_height)
                        cv2.circle(frame, (x, y), 10, (0, 0, 255), -1)
            
            # Real-time detection logic - SLIDING WINDOW
            if self.realtime_active:
                import time
                current_time = time.time()
                
                # Check if hand is detected in this frame
                hand_detected = results.multi_hand_landmarks is not None
                
                if hand_detected:
                    self.realtime_no_hand_frames = 0
                else:
                    self.realtime_no_hand_frames += 1
                
                # SLIDING WINDOW APPROACH:
                # 1. Accumulate frames
                # 2. Once we have minimum frames, start recognizing every frame
                # 3. Keep buffer size constant by removing old frames
                
                # Preprocess accumulated frames to get points
                points = None
                if self.realtime_frames_data:
                    points = preprocess_gesture_frames(
                        self.realtime_frames_data,
                        mode=GESTURE_MODE,
                        normalize_mode=GESTURE_NORMALIZATION_MODE,
                        min_motion=MIN_MOTION_DISTANCE
                    )
                
                # Trigger recognition continuously once we have enough frames
                # Use adaptive minimum based on template analysis
                min_threshold = getattr(self, 'realtime_min_points', 10)
                should_recognize = (
                    points is not None and
                    len(points) >= min_threshold and
                    (current_time - self.realtime_last_recognition) > 0.05  # Even faster (50ms)
                )
                
                if should_recognize:
                    # Try to recognize
                    try:
                        if len(self.realtime_frames_data) % 5 == 0:  # Log every 5th frame to avoid spam
                            print(f"INFO: Sliding window recognition with {len(points)} points")
                        
                        gesture_name, score = self.recognizer.recognize(points)
                        
                        # Show result with better feedback
                        if gesture_name is not None:
                            # Above threshold - show in green
                            self.result_label.config(text=f"✓ {gesture_name.replace('_', ' ').title()}", foreground='green')
                            if len(self.realtime_frames_data) % 5 == 0:
                                print(f"✓ DETECTED: {gesture_name} (score: {score:.4f})")
                        else:
                            # Below threshold - show best match for feedback
                            if score > 0.1:
                                fresh_recognizer = Recognizer(deepcopy(self.recognizer.templates))
                                result = fresh_recognizer.recognize(points)
                                if result:
                                    best_gesture, _ = result
                                    self.result_label.config(text=f"≈ {best_gesture.replace('_', ' ').title()}", foreground='orange')
                            else:
                                self.result_label.config(text="No gesture", foreground='gray')
                        
                        # Always show score
                        self.score_label.config(text=f"Score: {score:.4f}")
                        
                        # Update recognition time for cooldown
                        self.realtime_last_recognition = current_time
                        
                    except Exception as e:
                        print(f"Real-time recognition error: {e}")
                
                # Maintain strict sliding window: ALWAYS keep exactly 60 frames max
                # When frame 61 arrives, remove frame 0 (FIFO queue behavior)
                MAX_WINDOW_FRAMES = 60
                if len(self.realtime_frames_data) > MAX_WINDOW_FRAMES:
                    # Remove oldest frame to maintain window size
                    self.realtime_frames_data.pop(0)
                
                # Add real-time indicator with sliding window info
                cv2.putText(frame, "REAL-TIME DETECTION (SLIDING WINDOW)", (10, 30), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
                
                # Show frame count with max limit
                frame_text = f"Frames: {len(self.realtime_frames_data)}/60"
                if len(self.realtime_frames_data) >= 60:
                    frame_text += " (SLIDING)"
                cv2.putText(frame, frame_text, (10, 60), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
                
                # Show status with adaptive thresholds
                min_pts = getattr(self, 'realtime_min_points', 10)
                if len(self.realtime_frames_data) < min_pts // 2:
                    status_text = "Collecting..."
                elif points is None or len(points) < min_pts:
                    status_text = f"Need {min_pts - (len(points) if points else 0)} more pts"
                else:
                    status_text = "Live Recognition"
                cv2.putText(frame, status_text, (10, 90), 
                            cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
            
            # Add recording indicator
            elif self.is_recording:
                cv2.putText(frame, "RECORDING", (10, 30), 
                            cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
                cv2.putText(frame, f"Frames: {len(self.recorded_frames_data)}", (10, 60), 
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
                self._configure_realtime_point_window()
            except Exception as e:
                self.status_label.config(text="Ready - No templates loaded", foreground='blue')

    def _configure_realtime_point_window(self):
        """Match GUI real-time point capture window to the largest template length.

        Using the *maximum* template length (not average) ensures the window is
        wide enough to capture even the longest gesture.  Recognition starts as
        soon as we have ~30% of that length so shorter gestures are detected
        quickly and the loop keeps re-testing iteratively until the window fills.
        """
        if not self.recognizer.templates:
            return

        template_lengths = [len(t) for t in self.recognizer.templates]
        max_len = max(template_lengths)
        avg_len = sum(template_lengths) / len(template_lengths)

        def round_to_multiple(n, m):
            return max(m, int(round(n / m)) * m)

        # More aggressive: start at 30% for faster detection
        # Use average length for typical gestures, not max
        min_points = round_to_multiple(avg_len * 0.30, 6)
        max_points = round_to_multiple(max_len * 1.10, 6)

        self.realtime_min_points = int(max(min_points, 12))  # Lower floor
        self.realtime_max_points = int(max(max_points, self.realtime_min_points + 30))
        print(f"Point window: min={self.realtime_min_points}, max={self.realtime_max_points} "
              f"(avg template: {int(avg_len)} pts, max: {max_len} pts)")

def main():
    root = tk.Tk()
    app = GestureRecognitionGUI(root)
    root.protocol("WM_DELETE_WINDOW", app.on_closing)
    root.mainloop()

if __name__ == "__main__":
    main()
