"""
Compatibility layer for mediapipe 0.10.30+ (new API)
Provides a simple interface similar to the old mp.solutions.hands API
"""
import cv2
import mediapipe as mp
from mediapipe.tasks import python
from mediapipe.tasks.python import vision
import os

# Get the model path
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
MODEL_PATH = os.path.join(SCRIPT_DIR, "hand_landmarker.task")

# Download model if not present
if not os.path.exists(MODEL_PATH):
    print(f"Downloading hand_landmarker.task model...")
    import urllib.request
    url = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
    urllib.request.urlretrieve(url, MODEL_PATH)
    print(f"Model downloaded to {MODEL_PATH}")


class HandLandmarks:
    """Wrapper for hand landmarks to match old API"""
    def __init__(self, landmarks):
        self.landmark = landmarks


class HandsResults:
    """Wrapper for results to match old API"""
    def __init__(self):
        self.multi_hand_landmarks = None


class Hands:
    """Compatibility wrapper for mediapipe HandLandmarker"""
    
    # Hand connections for drawing (21 landmarks, 0-20)
    HAND_CONNECTIONS = [
        (0, 1), (1, 2), (2, 3), (3, 4),  # Thumb
        (0, 5), (5, 6), (6, 7), (7, 8),  # Index
        (0, 9), (9, 10), (10, 11), (11, 12),  # Middle
        (0, 13), (13, 14), (14, 15), (15, 16),  # Ring
        (0, 17), (17, 18), (18, 19), (19, 20),  # Pinky
        (5, 9), (9, 13), (13, 17)  # Palm
    ]
    
    def __init__(self, static_image_mode=False, max_num_hands=1,
                 min_detection_confidence=0.5, min_tracking_confidence=0.5):
        
        base_options = python.BaseOptions(model_asset_path=MODEL_PATH)
        options = vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.IMAGE,
            num_hands=max_num_hands,
            min_hand_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence
        )
        self.detector = vision.HandLandmarker.create_from_options(options)
    
    def process(self, image):
        """Process an RGB image and return results"""
        # Convert numpy array to MediaPipe Image
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=image)
        
        # Detect hands
        detection_result = self.detector.detect(mp_image)
        
        # Convert to old API format
        results = HandsResults()
        
        if detection_result.hand_landmarks:
            results.multi_hand_landmarks = [
                HandLandmarks(landmarks)
                for landmarks in detection_result.hand_landmarks
            ]
        
        return results
    
    def __del__(self):
        if hasattr(self, 'detector'):
            self.detector.close()


class DrawingUtils:
    """Drawing utilities to match old API"""
    
    @staticmethod
    def draw_landmarks(image, hand_landmarks, connections):
        """Draw hand landmarks and connections on image"""
        if not hand_landmarks:
            return
        
        h, w, _ = image.shape
        landmarks = hand_landmarks.landmark
        
        # Draw connections
        for connection in connections:
            start_idx, end_idx = connection
            if start_idx < len(landmarks) and end_idx < len(landmarks):
                start = landmarks[start_idx]
                end = landmarks[end_idx]
                
                start_point = (int(start.x * w), int(start.y * h))
                end_point = (int(end.x * w), int(end.y * h))
                
                cv2.line(image, start_point, end_point, (0, 200, 255), 2)
        
        # Draw landmarks
        for landmark in landmarks:
            x = int(landmark.x * w)
            y = int(landmark.y * h)
            cv2.circle(image, (x, y), 4, (255, 255, 255), -1)
            cv2.circle(image, (x, y), 4, (0, 150, 255), 1)


# Create module-level objects to match old API
class Solutions:
    class hands:
        Hands = Hands
        HAND_CONNECTIONS = Hands.HAND_CONNECTIONS
    
    drawing_utils = DrawingUtils()


solutions = Solutions()
