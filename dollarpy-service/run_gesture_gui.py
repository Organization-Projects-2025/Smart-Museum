"""
Quick start script for Smart Museum Gesture Recognition GUI
"""
import sys
import os

# Add current directory to path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Check if dollarpy is installed
try:
    import dollarpy
except ImportError:
    print("Error: dollarpy is not installed")
    print("Please install it with: pip install dollarpy")
    sys.exit(1)

# Check other dependencies
try:
    import cv2
    import mediapipe
    from PIL import Image
except ImportError as e:
    print(f"Error: Missing dependency - {e}")
    print("Please install requirements with: pip install -r requirements.txt")
    sys.exit(1)

# Run the GUI
from gesture_gui import main

if __name__ == "__main__":
    print("Starting Smart Museum Gesture Recognition GUI...")
    main()
