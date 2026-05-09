"""
Gesture Recognizer for Smart Museum

Loads templates and performs gesture recognition using dollarpy.
All preprocessing uses gesture_preprocessing.py to ensure consistency
with template generation.

Note: dollarpy's Recognizer mutates templates during recognition.
We use a fresh recognizer per recognition window to preserve templates.
"""

import pickle
import os
from copy import deepcopy
from typing import Optional, Tuple, List
from dollarpy import Recognizer, Point
from gesture_config import (
    DEBUG_GESTURES, LOG_REJECTED, CONFIDENCE_THRESHOLD, 
    MIN_GESTURE_POINTS, GESTURE_MODE, GESTURE_NORMALIZATION_MODE
)
from gesture_preprocessing import log_gesture_stats


class SmartMuseumGestureRecognizer:
    """Gesture recognizer using dollarpy templates."""

    def __init__(self, templates_file: Optional[str] = None):
        """
        Initialize recognizer.
        
        Args:
            templates_file: Path to pickled templates. Defaults to gesture_templates.pkl
                           in the same directory as this module.
        """
        if templates_file is None:
            script_dir = os.path.dirname(os.path.abspath(__file__))
            templates_file = os.path.join(script_dir, 'gesture_templates.pkl')
        
        self.templates_file = templates_file
        self.templates = []
        
        if DEBUG_GESTURES:
            print(f"[GestureRecognizer] Initialized with templates_file: {templates_file}")

    def build_templates(self, videos_path: str) -> bool:
        """
        Build gesture templates from video files.
        
        Args:
            videos_path: Path to directory containing gesture_videos subdirectories
        
        Returns:
            True if templates were successfully created
        """
        from gesture_processor import GestureProcessor
        
        processor = GestureProcessor()
        self.templates = processor.process_all_gestures(videos_path)
        
        if len(self.templates) > 0:
            print(f"\n✓ Successfully created recognizer with {len(self.templates)} templates")
            return True
        else:
            print("✗ Error: No templates were created")
            return False

    def save_templates(self) -> bool:
        """
        Save templates to file for later use.
        
        Returns:
            True if templates were saved successfully
        """
        if not self.templates:
            print("Error: No templates to save")
            return False
        
        try:
            with open(self.templates_file, 'wb') as f:
                pickle.dump(self.templates, f)
            print(f"✓ Templates saved to {self.templates_file}")
            return True
        except Exception as e:
            print(f"✗ Error saving templates: {e}")
            return False

    def load_templates(self) -> bool:
        """
        Load templates from file.
        
        Returns:
            True if templates were loaded successfully
        """
        if not os.path.exists(self.templates_file):
            print(f"✗ Templates file not found: {self.templates_file}")
            return False
        
        try:
            with open(self.templates_file, 'rb') as f:
                self.templates = pickle.load(f)
            print(f"✓ Loaded {len(self.templates)} templates from {self.templates_file}")
            
            if DEBUG_GESTURES:
                # Print template summary
                template_names = {}
                for template in self.templates:
                    name = template.name
                    template_names[name] = template_names.get(name, 0) + 1
                
                print("  Templates by gesture:")
                for name, count in sorted(template_names.items()):
                    print(f"    - {name}: {count}")
            
            return True
        except Exception as e:
            print(f"✗ Error loading templates: {e}")
            return False

    def recognize(self, points: List[Point]) -> Tuple[Optional[str], float]:
        """
        Recognize a gesture from a list of Point objects.
        
        Uses a fresh copy of templates to prevent dollarpy mutations.
        
        Args:
            points: List of Point objects representing the gesture
        
        Returns:
            Tuple of (gesture_name, score) or (None, score) if below threshold
        """
        if not self.templates:
            if DEBUG_GESTURES:
                print("[Recognizer] No templates loaded")
            return None, 0.0
        
        if len(points) < MIN_GESTURE_POINTS:
            if DEBUG_GESTURES:
                print(f"[Recognizer] Not enough points: {len(points)} < {MIN_GESTURE_POINTS}")
            return None, 0.0
        
        if DEBUG_GESTURES:
            log_gesture_stats(points, "Input gesture")
        
        try:
            # dollarpy's Recognizer mutates template objects during recognition.
            # Use a fresh deep copy so templates remain pristine on disk.
            fresh_recognizer = Recognizer(deepcopy(self.templates))
            result = fresh_recognizer.recognize(points)
            
            # Handle result safely
            if not result or len(result) < 2:
                if DEBUG_GESTURES:
                    print("[Recognizer] Recognition returned invalid result")
                return None, 0.0
            
            gesture_name, score = result
            
            if DEBUG_GESTURES:
                print(f"[Recognizer] Recognition result: {gesture_name} (score: {score:.4f})")
            
            # Extract base gesture name (strip numbers if template has them)
            base_name = self._get_gesture_base_name(gesture_name)
            
            # Always return the gesture name and score
            # Let the caller decide if the score is good enough
            if DEBUG_GESTURES:
                status = "✓ Accepted" if score >= CONFIDENCE_THRESHOLD else "○ Low confidence"
                print(f"[Recognizer] {status}: {base_name} (score: {score:.4f}, threshold: {CONFIDENCE_THRESHOLD:.2f})")
            
            return base_name, score
        
        except ZeroDivisionError as e:
            if DEBUG_GESTURES:
                print(f"[Recognizer] ZeroDivisionError: {e}")
            return None, 0.0
        except Exception as e:
            if DEBUG_GESTURES:
                print(f"[Recognizer] Exception: {e}")
            return None, 0.0

    @staticmethod
    def _get_gesture_base_name(template_name: str) -> str:
        """
        Extract base gesture name from template name.
        
        E.g., 'swipe_left_1' -> 'swipe_left'
             'circle_02' -> 'circle'
             'swipe_left' -> 'swipe_left'
        """
        # Try to strip trailing numbers/versions
        parts = template_name.rsplit('_', 1)
        if len(parts) > 1:
            suffix = parts[-1]
            # Check if suffix is all digits (version number)
            if suffix.isdigit() or suffix.lstrip('0').isdigit():
                return parts[0]
        
        return template_name

    def get_template_summary(self) -> str:
        """Get a human-readable summary of loaded templates."""
        if not self.templates:
            return "No templates loaded"
        
        template_names = {}
        for template in self.templates:
            name = template.name
            template_names[name] = template_names.get(name, 0) + 1
        
        summary = f"Loaded {len(self.templates)} templates:\n"
        for name, count in sorted(template_names.items()):
            summary += f"  - {name}: {count}\n"
        
        return summary
