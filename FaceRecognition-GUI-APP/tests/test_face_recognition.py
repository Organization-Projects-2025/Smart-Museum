"""
Quick test script for the face_recognition system
"""
from Detector_face_recognition import main_app

if __name__ == "__main__":
    print("=" * 60)
    print("Face Recognition Test")
    print("=" * 60)
    print("\nThis will:")
    print("1. Load all faces from the 'people' folder")
    print("2. Open your webcam")
    print("3. Recognize faces for 10 seconds")
    print("4. Press 'q' to quit early")
    print("\n" + "=" * 60 + "\n")
    
    input("Press ENTER to start...")
    
    # Run face recognition (no specific person, just general recognition)
    main_app(name=None, timeout=10, people_dir="people")
    
    print("\n" + "=" * 60)
    print("Test complete!")
    print("=" * 60)
