"""
Shared MediaPipe Tasks hand-landmarker wrapper for gesture_service and hand_service.
"""

import os
import urllib.request

import cv2
import mediapipe
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

_SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
_PYTHON_ROOT = os.path.dirname(_SCRIPT_DIR)
DATA_DIR = os.path.join(_PYTHON_ROOT, "data")
MODEL_PATH = os.path.join(DATA_DIR, "hand_landmarker.task")

if not os.path.exists(MODEL_PATH):
    print("[MediaPipe] Downloading hand_landmarker.task ...")
    os.makedirs(DATA_DIR, exist_ok=True)
    url = (
        "https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
        "hand_landmarker/float16/1/hand_landmarker.task"
    )
    urllib.request.urlretrieve(url, MODEL_PATH)
    print(f"[MediaPipe] Model saved to {MODEL_PATH}")


class HandLandmarks:
    def __init__(self, landmarks):
        self.landmark = landmarks


class HandsResults:
    def __init__(self):
        self.multi_hand_landmarks = None


class Hands:
    HAND_CONNECTIONS = [
        (0, 1), (1, 2), (2, 3), (3, 4),
        (0, 5), (5, 6), (6, 7), (7, 8),
        (0, 9), (9, 10), (10, 11), (11, 12),
        (0, 13), (13, 14), (14, 15), (15, 16),
        (0, 17), (17, 18), (18, 19), (19, 20),
        (5, 9), (9, 13), (13, 17),
    ]

    def __init__(
        self,
        static_image_mode=False,
        max_num_hands=1,
        min_detection_confidence=0.5,
        min_tracking_confidence=0.5,
    ):
        base_options = mp_python.BaseOptions(model_asset_path=MODEL_PATH)
        options = vision.HandLandmarkerOptions(
            base_options=base_options,
            running_mode=vision.RunningMode.IMAGE,
            num_hands=max_num_hands,
            min_hand_detection_confidence=min_detection_confidence,
            min_tracking_confidence=min_tracking_confidence,
        )
        self.detector = vision.HandLandmarker.create_from_options(options)

    def process(self, image):
        mp_image = mediapipe.Image(image_format=mediapipe.ImageFormat.SRGB, data=image)
        detection_result = self.detector.detect(mp_image)
        results = HandsResults()
        if detection_result.hand_landmarks:
            results.multi_hand_landmarks = [
                HandLandmarks(landmarks) for landmarks in detection_result.hand_landmarks
            ]
        return results

    def __del__(self):
        if hasattr(self, "detector"):
            self.detector.close()


class DrawingUtils:
    @staticmethod
    def draw_landmarks(image, hand_landmarks, connections):
        if not hand_landmarks:
            return
        h, w, _ = image.shape
        landmarks = hand_landmarks.landmark
        for connection in connections:
            start_idx, end_idx = connection
            if start_idx < len(landmarks) and end_idx < len(landmarks):
                start = landmarks[start_idx]
                end = landmarks[end_idx]
                start_point = (int(start.x * w), int(start.y * h))
                end_point = (int(end.x * w), int(end.y * h))
                cv2.line(image, start_point, end_point, (0, 200, 255), 2)
        for landmark in landmarks:
            x = int(landmark.x * w)
            y = int(landmark.y * h)
            cv2.circle(image, (x, y), 4, (255, 255, 255), -1)
            cv2.circle(image, (x, y), 4, (0, 150, 255), 1)


class Solutions:
    class hands:
        Hands = Hands
        HAND_CONNECTIONS = Hands.HAND_CONNECTIONS

    drawing_utils = DrawingUtils()


mp = Solutions()
