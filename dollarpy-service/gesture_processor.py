"""
Dynamic Gesture Recognition Processor for Smart Museum
Processes video files to extract gesture templates using MediaPipe
"""
import os
import cv2
from dollarpy import Template, Point
# Use compatibility layer for mediapipe 0.10.30+
import mediapipe_compat as mp

class GestureProcessor:
    def __init__(self):
        self.mp_hands = mp.solutions.hands
        # Use same configuration as real-time recognition
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,  # Match real-time (single hand)
            min_detection_confidence=0.6,  # Match real-time
            min_tracking_confidence=0.6  # Match real-time
        )
    
    def process_video(self, video_path, gesture_name):
        """
        Process a single video file and extract hand tracking points.
        Subsamples frames to match the real-time 60 FPS capture rate so that
        template point density is consistent with live recognition.
        Returns a Template object for the gesture.
        """
        points = []
        cap = cv2.VideoCapture(video_path)
        
        if not cap.isOpened():
            print(f"Error: Could not open video {video_path}")
            return None
        
        # Determine subsampling step so we process at ~60 FPS equivalent
        TARGET_FPS = 60.0
        video_fps = cap.get(cv2.CAP_PROP_FPS) or TARGET_FPS
        # step=1 means every frame; step=2 means every other frame, etc.
        step = max(1, round(video_fps / TARGET_FPS))
        
        frame_count = 0
        
        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                break
            
            frame_count += 1
            # Skip frames to match target FPS density
            if (frame_count - 1) % step != 0:
                continue
            
            # Use same resolution as real-time recognition
            frame = cv2.resize(frame, (640, 480))
            
            # Convert to RGB for MediaPipe
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.hands.process(rgb_frame)
            
            if results.multi_hand_landmarks:
                image_height, image_width, _ = frame.shape
                
                for hand_landmarks in results.multi_hand_landmarks:
                    # Track: wrist (0), thumb tip (4), index tip (8), middle tip (12), ring tip (16), pinky tip (20)
                    key_landmarks = [0, 4, 8, 12, 16, 20]
                    
                    # All 6 landmarks in same frame share the same stroke ID (matches real-time)
                    stroke_id = len(points) // 6 + 1
                    
                    for landmark_id in key_landmarks:
                        landmark = hand_landmarks.landmark[landmark_id]
                        x = int(landmark.x * image_width)
                        y = int(landmark.y * image_height)
                        points.append(Point(x, y, stroke_id))
        
        cap.release()
        
        if len(points) > 0:
            return Template(gesture_name, points)
        else:
            print(f"Warning: No hand landmarks detected in {video_path}")
            return None
    
    def process_gesture_folder(self, folder_path, gesture_name):
        """
        Process all video files in a folder and return a list of templates
        """
        templates = []
        
        if not os.path.exists(folder_path):
            print(f"Error: Folder {folder_path} does not exist")
            return templates
        
        video_files = [f for f in os.listdir(folder_path) if f.endswith('.mp4')]
        
        for idx, video_file in enumerate(video_files):
            video_path = os.path.join(folder_path, video_file)
            template_name = f"{gesture_name}_{idx+1}"
            print(f"Processing {video_file} as {template_name}...")
            
            template = self.process_video(video_path, template_name)
            if template:
                templates.append(template)
        
        return templates
    
    def process_all_gestures(self, base_path):
        """
        Process all gesture folders in the base path DYNAMICALLY
        Automatically detects gesture classes from folder names
        Returns a list of all templates
        """
        all_templates = []
        
        # Check if base path exists
        if not os.path.exists(base_path):
            print(f"Error: Base path does not exist: {base_path}")
            return all_templates
        
        # Get all subdirectories (each is a gesture class)
        gesture_folders = []
        for item in os.listdir(base_path):
            item_path = os.path.join(base_path, item)
            if os.path.isdir(item_path):
                # Use folder name as gesture name (convert to lowercase with underscores)
                gesture_name = item.lower().replace(' ', '_').replace('-', '_')
                gesture_folders.append((item, gesture_name))
        
        if not gesture_folders:
            print(f"Warning: No gesture folders found in {base_path}")
            return all_templates
        
        print(f"\nFound {len(gesture_folders)} gesture classes:")
        for folder_name, gesture_name in gesture_folders:
            print(f"  - {folder_name} -> {gesture_name}")
        
        # Process each gesture folder
        for folder_name, gesture_name in gesture_folders:
            folder_path = os.path.join(base_path, folder_name)
            print(f"\n=== Processing {gesture_name} gestures ===")
            templates = self.process_gesture_folder(folder_path, gesture_name)
            all_templates.extend(templates)
            print(f"Created {len(templates)} templates for {gesture_name}")
        
        return all_templates
