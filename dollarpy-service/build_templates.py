"""
Command-line script to build gesture templates from videos
"""
import sys
import os
from gesture_recognizer import SmartMuseumGestureRecognizer

def main():
    print("=" * 60)
    print("Smart Museum - Gesture Template Builder")
    print("=" * 60)
    
    # Get script directory and construct absolute default path
    script_dir = os.path.dirname(os.path.abspath(__file__))
    default_path = os.path.join(os.path.dirname(script_dir), "Public", "Data", "Videos", "Moves")
    
    # Check if custom path provided
    if len(sys.argv) > 1:
        videos_path = sys.argv[1]
    else:
        videos_path = default_path
        print(f"\nUsing default path: {videos_path}")
    
    # Verify path exists
    if not os.path.exists(videos_path):
        print(f"\nERROR: Path does not exist: {videos_path}")
        print("\nUsage:")
        print(f"  python build_templates.py [path_to_videos]")
        print(f"\nDefault path: {default_path}")
        sys.exit(1)
    
    print(f"\nProcessing videos from: {videos_path}")
    print("-" * 60)
    
    # Initialize recognizer
    recognizer = SmartMuseumGestureRecognizer()
    
    # Build templates
    print("\nBuilding templates...")
    success = recognizer.build_templates(videos_path)
    
    if success:
        print("\n" + "=" * 60)
        print(f"OK: Successfully created {len(recognizer.templates)} templates")
        print("=" * 60)
        
        # Show template breakdown
        gesture_counts = {}
        for template in recognizer.templates:
            base_name = recognizer.get_gesture_base_name(template.name)
            gesture_counts[base_name] = gesture_counts.get(base_name, 0) + 1
        
        print("\nTemplates per gesture:")
        for gesture, count in sorted(gesture_counts.items()):
            print(f"  {gesture:.<30} {count} templates")
        
        # Save templates
        print("\nSaving templates...")
        recognizer.save_templates()
        print(f"OK: Templates saved to: {recognizer.templates_file}")
        
        print("\n" + "=" * 60)
        print("OK: Template building complete!")
        print("\nYou can now run the GUI:")
        print("  python run_gesture_gui.py")
        print("=" * 60)
    else:
        print("\n" + "=" * 60)
        print("ERROR: Failed to build templates")
        print("=" * 60)
        print("\nPossible issues:")
        print("  - No video files found in the specified path")
        print("  - Video files are corrupted or in wrong format")
        print("  - No hand landmarks detected in videos")
        print("\nPlease check the videos and try again.")
        sys.exit(1)

if __name__ == "__main__":
    main()
