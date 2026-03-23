"""
Download MediaPipe Hand Landmarker model
"""
import urllib.request
import os

MODEL_URL = "https://storage.googleapis.com/mediapipe-models/hand_landmarker/hand_landmarker/float16/1/hand_landmarker.task"
MODEL_PATH = "hand_landmarker.task"

def download_model():
    """Download the hand landmarker model if it doesn't exist"""
    if os.path.exists(MODEL_PATH):
        print(f"✓ Model already exists: {MODEL_PATH}")
        return
    
    print(f"Downloading hand landmarker model...")
    print(f"URL: {MODEL_URL}")
    print(f"Destination: {MODEL_PATH}")
    
    try:
        urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)
        file_size = os.path.getsize(MODEL_PATH) / (1024 * 1024)
        print(f"✓ Download complete! ({file_size:.2f} MB)")
    except Exception as e:
        print(f"✗ Download failed: {e}")
        print("\nManual download instructions:")
        print(f"1. Download from: {MODEL_URL}")
        print(f"2. Save as: {MODEL_PATH}")
        raise

if __name__ == "__main__":
    download_model()
