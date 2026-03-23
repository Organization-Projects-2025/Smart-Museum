"""
Dynamic Gesture Recognition Processor for Smart Museum
Processes video files to extract gesture templates using MediaPipe
"""
import os
import cv2
import mediapipe as mp
from dollarpy import Template, Point

class GestureProcessor:
    def __init__(self):
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_hands = mp.solutions.hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            min_detection_confidence=0.5,
            min_tracking_confidence=0.5
        )
    
    def process_video(self, video_path, gesture_name):
        """
        Process a single video file and extract hand tracking points
        Returns a Template object for the gesture
        """
        points = []
        cap = cv2.VideoCapture(video_path)
        
        if not cap.isOpened():
            print(f"Error: Could not open video {video_path}")
            return None
        
        frame_count = 0
        
        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                break
            
            frame = cv2.resize(frame, (480, 320))
            frame_count += 1
            
            # Convert to RGB for MediaPipe
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.hands.process(rgb_frame)
            
            if results.multi_hand_landmarks:
                image_height, image_width, _ = frame.shape
                
                for hand_landmarks in results.multi_hand_landmarks:
                    # Extract MULTIPLE key landmarks (like the notebook example)
                    # Track: wrist (0), thumb tip (4), index tip (8), middle tip (12), ring tip (16), pinky tip (20)
                    key_landmarks = [0, 4, 8, 12, 16, 20]
                    
                    for landmark_id in key_landmarks:
                        landmark = hand_landmarks.landmark[landmark_id]
                        x = int(landmark.x * image_width)
                        y = int(landmark.y * image_height)
                        # Use frame_count as stroke ID so all points in same frame are grouped
                        points.append(Point(x, y, frame_count))
        
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
