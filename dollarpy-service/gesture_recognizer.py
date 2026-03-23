"""
Gesture Recognizer for Smart Museum
Loads templates and recognizes gestures in real-time
"""
from dollarpy import Recognizer, Point
from gesture_processor import GestureProcessor
import pickle
import os

class SmartMuseumGestureRecognizer:
    def __init__(self, templates_file=None):
        # If no templates_file specified, use default in dollarpy-service folder
        if templates_file is None:
            script_dir = os.path.dirname(os.path.abspath(__file__))
            templates_file = os.path.join(script_dir, 'gesture_templates.pkl')
        
        self.templates_file = templates_file
        self.recognizer = None
        self.templates = []
    
    def build_templates(self, videos_path):
        """
        Build gesture templates from video files
        """
        processor = GestureProcessor()
        self.templates = processor.process_all_gestures(videos_path)
        
        if len(self.templates) > 0:
            self.recognizer = Recognizer(self.templates)
            print(f"\nSuccessfully created recognizer with {len(self.templates)} templates")
            return True
        else:
            print("Error: No templates were created")
            return False
    
    def save_templates(self):
        """
        Save templates to file for later use
        """
        if self.templates:
            with open(self.templates_file, 'wb') as f:
                pickle.dump(self.templates, f)
            print(f"Templates saved to {self.templates_file}")
            return True
        return False
    
    def load_templates(self):
        """
        Load templates from file
        """
        if os.path.exists(self.templates_file):
            with open(self.templates_file, 'rb') as f:
                self.templates = pickle.load(f)
            self.recognizer = Recognizer(self.templates)
            print(f"Loaded {len(self.templates)} templates from {self.templates_file}")
            return True
        else:
            print(f"Templates file {self.templates_file} not found")
            return False
    
    def recognize(self, points):
        """
        Recognize a gesture from a list of Point objects
        Returns (gesture_name, score)
        """
        if self.recognizer is None:
            return ("No recognizer loaded", 0.0)
        
        if len(points) < 2:
            return ("Insufficient points", 0.0)
        
        # Check if points have movement BEFORE calling recognizer
        # Calculate the bounding box of all points
        if len(points) > 1:
            x_coords = [p.x for p in points]
            y_coords = [p.y for p in points]
            
            x_range = max(x_coords) - min(x_coords)
            y_range = max(y_coords) - min(y_coords)
            
            # Need at least 20 pixels of movement in either direction (increased for multi-point)
            if x_range < 20 and y_range < 20:
                return ("No movement detected - move your hand!", 0.0)
        
        try:
            # First do the recognition to get the result
            result = self.recognizer.recognize(points)
            
            # Get ALL template scores for debugging by comparing with each template
            all_scores = []
            for template in self.recognizer.templates:
                try:
                    # Use the same distance calculation as dollarpy
                    from dollarpy import Recognizer
                    temp_recognizer = Recognizer([template])
                    temp_result = temp_recognizer.recognize(points)
                    all_scores.append((template.name, temp_result[1]))
                except:
                    pass
            
            # Sort by score (higher is better)
            all_scores.sort(key=lambda x: x[1], reverse=True)
            
            # Print top 5 matches
            print("\n=== Top 5 Recognition Matches ===")
            for i, (name, score) in enumerate(all_scores[:5]):
                base_name = self.get_gesture_base_name(name)
                print(f"{i+1}. {base_name:20s} - Score: {score:.4f}")
            print()
            
            return result
        except ZeroDivisionError as e:
            print(f"Debug recognizer: ZeroDivisionError caught: {e}")
            return ("Recognition error - ZeroDivisionError", 0.0)
        except Exception as e:
            print(f"Debug recognizer: Exception caught: {e}")
            return (f"Error: {str(e)}", 0.0)
    
    def get_gesture_base_name(self, template_name):
        """
        Extract base gesture name from template name
        e.g., 'swipe_left_1' -> 'swipe_left'
        """
        parts = template_name.rsplit('_', 1)
        if len(parts) > 1 and parts[-1].isdigit():
            return parts[0]
        return template_name
