"""
Gesture Recognition Evaluation Script

Evaluates accuracy of gesture recognition on test recordings.
Tests on recordings NOT used for template creation.

Outputs:
- Per-gesture accuracy percentage
- Confusion matrix
- Detailed results log
"""

import os
import sys
import cv2
import pickle
from typing import Dict, List, Tuple
from collections import defaultdict
from datetime import datetime
import numpy as np

from gesture_recognizer import SmartMuseumGestureRecognizer
from gesture_preprocessing import preprocess_gesture_frames
from gesture_config import (
    GESTURE_MODE, GESTURE_NORMALIZATION_MODE, MIN_MOTION_DISTANCE,
    DEBUG_GESTURES, get_config_summary
)
import mediapipe_compat as mp


class GestureEvaluator:
    """Evaluate gesture recognition accuracy."""

    def __init__(self, templates_path: str = "gesture_templates.pkl"):
        """Initialize evaluator with recognizer."""
        self.recognizer = SmartMuseumGestureRecognizer(templates_path)
        if not self.recognizer.load_templates():
            raise RuntimeError(f"Failed to load templates from {templates_path}")

        self.mp_hands = mp.solutions.hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )

        # Results tracking
        self.results = []
        self.confusion_matrix = defaultdict(lambda: defaultdict(int))
        self.per_gesture_accuracy = {}

    def evaluate_video(self, video_path: str, ground_truth_label: str) -> Tuple[bool, str, float]:
        """
        Evaluate recognition on a single video.

        Args:
            video_path: Path to test video
            ground_truth_label: Expected gesture name

        Returns:
            (is_correct, predicted_label, score)
        """
        # Extract frames and landmarks
        frames_data = []
        cap = cv2.VideoCapture(video_path)

        if not cap.isOpened():
            print(f"  ✗ Could not open: {video_path}")
            return False, "error", 0.0

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

            frame = cv2.resize(frame, (640, 480))
            rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            results = self.hands.process(rgb_frame)

            if results.multi_hand_landmarks:
                hand_landmarks = results.multi_hand_landmarks[0]
                frames_data.append({
                    "hand_landmarks": hand_landmarks,
                    "frame": frame
                })

        cap.release()

        if not frames_data:
            print(f"  ✗ No hand landmarks detected in {os.path.basename(video_path)}")
            return False, "no_hand", 0.0

        # Preprocess using shared pipeline
        points = preprocess_gesture_frames(
            frames_data,
            mode=GESTURE_MODE,
            normalize_mode=GESTURE_NORMALIZATION_MODE,
            min_motion=MIN_MOTION_DISTANCE
        )

        if points is None:
            print(f"  ✗ Preprocessing failed for {os.path.basename(video_path)}")
            return False, "preprocess_failed", 0.0

        # Recognize
        predicted_label, score = self.recognizer.recognize(points)

        is_correct = (predicted_label == ground_truth_label)

        return is_correct, predicted_label or "unknown", score

    def evaluate_folder(self, folder_path: str, gesture_name: str) -> Tuple[int, int]:
        """
        Evaluate all test videos in a folder.

        Assumes video files are named: [gesture_name]_test_*.mp4

        Args:
            folder_path: Path to folder with test videos
            gesture_name: Expected gesture name

        Returns:
            (correct_count, total_count)
        """
        video_extensions = ('.mp4', '.avi', '.mov', '.mkv', '.flv')
        video_files = [
            f for f in os.listdir(folder_path)
            if f.lower().endswith(video_extensions)
        ]

        if not video_files:
            print(f"  Warning: No test videos found in {folder_path}")
            return 0, 0

        print(f"  Testing {len(video_files)} videos for '{gesture_name}'...")

        correct = 0
        for idx, video_file in enumerate(video_files, 1):
            video_path = os.path.join(folder_path, video_file)
            is_correct, predicted, score = self.evaluate_video(video_path, gesture_name)

            status = "✓" if is_correct else "✗"
            confidence = "high" if score > 0.8 else "med" if score > 0.6 else "low"

            print(f"    [{idx:2d}] {status} {video_file:30s} "
                  f"→ {predicted:15s} (score: {score:.4f}, {confidence})")

            if is_correct:
                correct += 1

            # Record in confusion matrix
            self.confusion_matrix[gesture_name][predicted] += 1
            self.results.append({
                "video": video_file,
                "ground_truth": gesture_name,
                "predicted": predicted,
                "score": score,
                "correct": is_correct
            })

        return correct, len(video_files)

    def evaluate_all_gestures(self, test_base_path: str) -> Dict[str, float]:
        """
        Evaluate all gesture test folders.

        Directory structure:
            test_base_path/
              gesture_name_test/
                gesture_name_test_01.mp4
                gesture_name_test_02.mp4
              gesture_name_test/
                ...

        Args:
            test_base_path: Root path for test data

        Returns:
            Dict mapping gesture_name → accuracy_percentage
        """
        if not os.path.isdir(test_base_path):
            print(f"Error: Test path not found: {test_base_path}")
            return {}

        # Find test folders
        test_folders = []
        for item in os.listdir(test_base_path):
            item_path = os.path.join(test_base_path, item)
            if os.path.isdir(item_path):
                # Extract gesture name from folder name (e.g., "swipe_left_test" → "swipe_left")
                gesture_name = item.replace("_test", "").lower()
                test_folders.append((item, gesture_name))

        if not test_folders:
            print(f"Warning: No test folders found in {test_base_path}")
            return {}

        print(f"\nFound {len(test_folders)} gesture test categories:")
        for folder_name, gesture_name in test_folders:
            print(f"  - {folder_name:30s} → {gesture_name}")

        print("\n" + "=" * 80)
        print("EVALUATION RESULTS")
        print("=" * 80 + "\n")

        # Evaluate each gesture
        all_correct = 0
        all_total = 0

        for folder_name, gesture_name in test_folders:
            folder_path = os.path.join(test_base_path, folder_name)
            correct, total = self.evaluate_folder(folder_path, gesture_name)
            all_correct += correct
            all_total += total

            if total > 0:
                accuracy = (correct / total) * 100
                self.per_gesture_accuracy[gesture_name] = accuracy
            else:
                self.per_gesture_accuracy[gesture_name] = 0.0

        return self.per_gesture_accuracy

    def print_summary(self):
        """Print evaluation summary."""
        print("\n" + "=" * 80)
        print("ACCURACY SUMMARY")
        print("=" * 80 + "\n")

        if not self.per_gesture_accuracy:
            print("No results to summarize")
            return

        # Print per-gesture accuracy
        print("Per-Gesture Accuracy:")
        print("-" * 50)
        print(f"{'Gesture':<25} {'Accuracy':<15} {'Tests'}")
        print("-" * 50)

        total_correct = 0
        total_tests = 0

        for gesture_name in sorted(self.per_gesture_accuracy.keys()):
            accuracy = self.per_gesture_accuracy[gesture_name]

            # Count tests for this gesture
            gesture_tests = sum(1 for r in self.results if r["ground_truth"] == gesture_name)
            total_tests += gesture_tests
            if r["correct"] for r in self.results if r["ground_truth"] == gesture_name:
                total_correct += sum(1 for r in self.results 
                                    if r["ground_truth"] == gesture_name and r["correct"])

            print(f"{gesture_name:<25} {accuracy:>6.1f}%         {gesture_tests:>3d}")

        print("-" * 50)
        if total_tests > 0:
            overall_accuracy = (total_correct / total_tests) * 100
        else:
            overall_accuracy = 0.0
        print(f"{'OVERALL':<25} {overall_accuracy:>6.1f}%         {total_tests:>3d}")
        print()

        # Print confusion matrix
        self._print_confusion_matrix()

    def _print_confusion_matrix(self):
        """Print confusion matrix."""
        print("\n" + "=" * 80)
        print("CONFUSION MATRIX")
        print("=" * 80 + "\n")

        if not self.confusion_matrix:
            print("No confusion matrix data")
            return

        # Get all gesture names
        all_gestures = sorted(set(self.confusion_matrix.keys()) | 
                             set(g for cm in self.confusion_matrix.values() for g in cm.keys()))

        # Print header
        print(f"{'GT / Predicted':<20}", end="")
        for gesture in all_gestures:
            print(f"{gesture[:10]:>12}", end="")
        print()

        print("-" * (20 + 12 * len(all_gestures)))

        # Print rows
        for gt_gesture in all_gestures:
            print(f"{gt_gesture:<20}", end="")
            for pred_gesture in all_gestures:
                count = self.confusion_matrix[gt_gesture].get(pred_gesture, 0)
                print(f"{count:>12}", end="")
            print()

    def export_results(self, output_file: str = "gesture_evaluation_results.txt"):
        """Export detailed results to file."""
        print(f"\nExporting results to {output_file}...")

        with open(output_file, 'w') as f:
            f.write("=" * 80 + "\n")
            f.write("GESTURE RECOGNITION EVALUATION RESULTS\n")
            f.write("=" * 80 + "\n\n")

            f.write(f"Timestamp: {datetime.now().isoformat()}\n")
            f.write(f"Mode: {GESTURE_MODE}\n")
            f.write(f"Normalization: {GESTURE_NORMALIZATION_MODE}\n")
            f.write(f"Templates: {len(self.recognizer.templates)}\n\n")

            f.write("Per-Gesture Accuracy:\n")
            f.write("-" * 50 + "\n")
            for gesture in sorted(self.per_gesture_accuracy.keys()):
                accuracy = self.per_gesture_accuracy[gesture]
                f.write(f"{gesture:<25} {accuracy:>6.1f}%\n")

            f.write("\n\nDetailed Results:\n")
            f.write("-" * 80 + "\n")
            f.write(f"{'Video':<30} {'GT':<15} {'Predicted':<15} {'Score':<10} {'Result'}\n")
            f.write("-" * 80 + "\n")

            for result in self.results:
                status = "✓" if result["correct"] else "✗"
                f.write(
                    f"{result['video']:<30} "
                    f"{result['ground_truth']:<15} "
                    f"{result['predicted']:<15} "
                    f"{result['score']:<10.4f} "
                    f"{status}\n"
                )

        print(f"✓ Results exported to {output_file}")


def main():
    """Main entry point."""
    print("=" * 80)
    print("Smart Museum - Gesture Recognition Evaluator")
    print("=" * 80)
    print()
    print(get_config_summary())
    print()

    # Paths
    templates_path = "gesture_templates.pkl"
    test_base_path = "./gesture_videos_test"  # Separate from template videos

    if not os.path.exists(templates_path):
        print(f"Error: Templates not found: {templates_path}")
        print("Please build templates first with gesture_processor.py")
        return

    if not os.path.isdir(test_base_path):
        print(f"Error: Test data path not found: {test_base_path}")
        print(f"Please organize test videos in: {test_base_path}/")
        print("Expected structure:")
        print(f"  {test_base_path}/")
        print("    swipe_left_test/")
        print("      swipe_left_test_01.mp4")
        print("      swipe_left_test_02.mp4")
        print("    swipe_right_test/")
        print("      swipe_right_test_01.mp4")
        print("      ...")
        return

    # Run evaluation
    try:
        evaluator = GestureEvaluator(templates_path)
        evaluator.evaluate_all_gestures(test_base_path)
        evaluator.print_summary()
        evaluator.export_results("gesture_evaluation_results.txt")
    except Exception as e:
        print(f"Error: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    main()
