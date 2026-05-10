"""
Dynamic Gesture Template Builder for Smart Museum

Processes video files to extract gesture templates using MediaPipe Hands + dollarpy.

IMPORTANT:
- This module MUST use the same preprocessing as gesture_service.py
- All preprocessing is in gesture_preprocessing.py
- Any change to preprocessing requires rebuilding all templates
"""

import os
import cv2
from dollarpy import Template
from gesture_preprocessing import preprocess_gesture_frames
from gesture_config import (
    GESTURE_MODE, GESTURE_NORMALIZATION_MODE, MIN_GESTURE_POINTS, 
    MIN_MOTION_DISTANCE, DEBUG_GESTURES
)
import mediapipe_compat as mp


class GestureProcessor:
    """Build gesture templates from video files."""

    def __init__(self):
        """Initialize MediaPipe Hands detector."""
        self.mp_hands = mp.solutions.hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )

    def process_video(self, video_path: str, gesture_name: str) -> Template:
        """
        Process a single video file and extract a gesture template.

        Uses the EXACT same preprocessing as live recognition (gesture_service.py).

        Args:
            video_path: Path to video file
            gesture_name: Name of the gesture (e.g., "swipe_left")

        Returns:
            Template object for dollarpy, or None if processing failed
        """
        if not os.path.exists(video_path):
            print(f"Error: Video file not found: {video_path}")
            return None

        frames_data = []
        cap = cv2.VideoCapture(video_path)

        if not cap.isOpened():
            print(f"Error: Could not open video: {video_path}")
            return None

        # Determine frame skip to process at ~60 FPS equivalent
        video_fps = cap.get(cv2.CAP_PROP_FPS) or 60.0
        TARGET_FPS = 60.0
        step = max(1, round(video_fps / TARGET_FPS))

        frame_count = 0

        while cap.isOpened():
            ret, frame = cap.read()
            if not ret:
                break

            frame_count += 1
            if (frame_count - 1) % step != 0:
                continue

            # Resize to standard resolution
            frame = cv2.resize(frame, (640, 480))

            # Convert to RGB for MediaPipe
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.hands.process(rgb_frame)

            if results.multi_hand_landmarks:
                # Process first hand only
                hand_landmarks = results.multi_hand_landmarks[0]
                
                # Store frame data for preprocessing
                frames_data.append({
                    "hand_landmarks": hand_landmarks,
                    "frame": frame
                })

        cap.release()

        if not frames_data:
            print(f"Warning: No hand landmarks detected in {video_path}")
            return None

        # Use the SHARED preprocessing pipeline
        points = preprocess_gesture_frames(
            frames_data,
            mode=GESTURE_MODE,
            normalize_mode=GESTURE_NORMALIZATION_MODE,
            min_motion=MIN_MOTION_DISTANCE
        )

        if points is None:
            print(f"Warning: Preprocessing failed for {video_path}")
            return None

        if len(points) < MIN_GESTURE_POINTS:
            print(f"Warning: Not enough points in {video_path} ({len(points)} < {MIN_GESTURE_POINTS})")
            return None

        if DEBUG_GESTURES:
            from gesture_preprocessing import log_gesture_stats
            log_gesture_stats(points, f"Template: {gesture_name}")

        # Create template
        template = Template(gesture_name, points)
        return template

    def process_gesture_folder(self, folder_path: str, gesture_name: str):
        """
        Process all video files in a folder.

        Args:
            folder_path: Path to folder with video files
            gesture_name: Base name of gesture (used for all templates from this folder)

        Returns:
            List of Template objects
        """
        templates = []

        if not os.path.isdir(folder_path):
            print(f"Error: Folder not found: {folder_path}")
            return templates

        # Find all video files
        video_extensions = ('.mp4', '.avi', '.mov', '.mkv', '.flv')
        video_files = [
            f for f in os.listdir(folder_path)
            if f.lower().endswith(video_extensions)
        ]

        if not video_files:
            print(f"Warning: No video files found in {folder_path}")
            return templates

        print(f"\nProcessing {len(video_files)} videos for gesture '{gesture_name}'...")

        for idx, video_file in enumerate(video_files, 1):
            video_path = os.path.join(folder_path, video_file)
            print(f"  [{idx}/{len(video_files)}] {video_file}...", end=" ")

            template = self.process_video(video_path, gesture_name)
            if template:
                templates.append(template)
                print("✓")
            else:
                print("✗")

        print(f"Created {len(templates)} templates for '{gesture_name}'")
        return templates

    def process_all_gestures(self, base_path: str):
        """
        Process all gesture folders in a directory.

        Automatically discovers gesture classes from folder names.

        Args:
            base_path: Root path containing gesture subdirectories

        Returns:
            List of all Template objects
        """
        all_templates = []

        if not os.path.isdir(base_path):
            print(f"Error: Base path not found: {base_path}")
            return all_templates

        # Get all subdirectories
        gesture_folders = []
        for item in os.listdir(base_path):
            item_path = os.path.join(base_path, item)
            if os.path.isdir(item_path):
                gesture_name = item.lower().replace(' ', '_').replace('-', '_')
                gesture_folders.append((item, gesture_name))

        if not gesture_folders:
            print(f"Warning: No gesture folders found in {base_path}")
            return all_templates

        print(f"\nDiscovered {len(gesture_folders)} gesture classes:")
        for folder_name, gesture_name in gesture_folders:
            print(f"  - {folder_name:30} → {gesture_name}")

        # Process each gesture folder
        for folder_name, gesture_name in gesture_folders:
            folder_path = os.path.join(base_path, folder_name)
            templates = self.process_gesture_folder(folder_path, gesture_name)
            all_templates.extend(templates)

        print(f"\n{'='*60}")
        print(f"Total templates created: {len(all_templates)}")
        print(f"{'='*60}")

        return all_templates


def main():
    """Example: process all gesture videos."""
    import pickle
    from gesture_config import get_config_summary

    print("="*60)
    print("Smart Museum - Gesture Template Builder")
    print("="*60)
    print()
    print(get_config_summary())
    print()

    # Paths
    base_path = "./gesture_videos"
    output_path = "./gesture_templates.pkl"

    if not os.path.exists(base_path):
        print(f"Error: {base_path} not found")
        print("Create this folder and organize videos by gesture:")
        print("  gesture_videos/")
        print("    swipe_left/")
        print("      video1.mp4")
        print("      video2.mp4")
        print("    swipe_right/")
        print("      video1.mp4")
        print("      video2.mp4")
        return

    # Process all gestures
    processor = GestureProcessor()
    templates = processor.process_all_gestures(base_path)

    if templates:
        # Save templates
        with open(output_path, 'wb') as f:
            pickle.dump(templates, f)
        print(f"\nTemplates saved to: {output_path}")
    else:
        print(f"\nNo templates created. Check your video files and folders.")


if __name__ == "__main__":
    main()
