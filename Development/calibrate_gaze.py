"""
Development/calibrate_gaze.py
==============================
Run this ONCE before using the application to calibrate EyeTrax gaze tracking.
A 9-point calibration window will open — look at each dot when it appears.

The calibrated model is saved to:
    python/server/eyetrax_model.pkl

Usage:
    .venv\Scripts\python.exe Development\calibrate_gaze.py
    .venv\Scripts\python.exe Development\calibrate_gaze.py --camera 2
"""

import argparse
import os
import sys

# Add server path so we can use the same face_landmarker.task
_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
_SERVER = os.path.join(_ROOT, "python", "server")
_MODEL_PATH = os.path.join(_SERVER, "eyetrax_model.pkl")
_LANDMARKER = os.path.join(_SERVER, "face_landmarker.task")

try:
    from eyetrax import GazeEstimator, run_9_point_calibration
except ImportError:
    print("ERROR: eyetrax not installed. Run: pip install eyetrax")
    sys.exit(1)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--camera", type=int,
                        default=int(os.environ.get("MUSEUM_CAMERA", "0")))
    args = parser.parse_args()

    print("=" * 50)
    print("EyeTrax Gaze Calibration")
    print("=" * 50)
    print(f"Camera index: {args.camera}")
    print(f"Model will be saved to: {_MODEL_PATH}")
    print()
    print("Instructions:")
    print("  - Sit at your normal viewing distance from the screen")
    print("  - Keep your head still during calibration")
    print("  - Look at each dot when it appears and hold your gaze")
    print("  - Press any key to start")
    print()
    input("Press Enter to begin calibration...")

    est = GazeEstimator(face_landmarker_model=_LANDMARKER)

    print("Opening calibration window...")
    run_9_point_calibration(est, camera_index=args.camera)

    # Save the trained model
    est.save_model(_MODEL_PATH)
    print(f"\nCalibration complete! Model saved to: {_MODEL_PATH}")
    print("You can now run start.bat — EyeTrax will load automatically.")

if __name__ == "__main__":
    main()
