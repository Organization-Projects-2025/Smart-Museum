"""
Rebuild Gesture Templates Script

Rebuilds gesture templates from video recordings.
Use this after changing gesture_preprocessing.py or gesture_config.py.

Usage:
    python rebuild_templates.py
    python rebuild_templates.py --input ./my_videos --output ./my_templates.pkl
    DEBUG_GESTURES=1 python rebuild_templates.py
"""

import os
import sys
import argparse
import pickle
from pathlib import Path

# Add service to path
script_dir = os.path.dirname(os.path.abspath(__file__))
if script_dir not in sys.path:
    sys.path.insert(0, script_dir)

from gesture_processor import GestureProcessor
from gesture_config import get_config_summary, DEBUG_GESTURES


def rebuild_templates(input_dir: str, output_file: str, verbose: bool = False):
    """
    Rebuild gesture templates from video files.
    
    Args:
        input_dir: Directory containing gesture subdirectories with videos
        output_file: Where to save templates.pkl
        verbose: Print detailed progress
    """
    print("=" * 70)
    print("Smart Museum - Gesture Template Builder")
    print("=" * 70)
    print()
    print(get_config_summary())
    print()

    if not os.path.isdir(input_dir):
        print(f"Error: Input directory not found: {input_dir}")
        print("\nExpected structure:")
        print("  gesture_videos/")
        print("    swipe_left/")
        print("      video1.mp4")
        print("      video2.mp4")
        print("      video3.mp4")
        print("    swipe_right/")
        print("      video1.mp4")
        print("      video2.mp4")
        print("    circle/")
        print("      video1.mp4")
        print("      ...")
        return False

    print(f"Input directory:  {input_dir}")
    print(f"Output file:      {output_file}")
    print()

    # Build templates
    processor = GestureProcessor()
    templates = processor.process_all_gestures(input_dir)

    if not templates:
        print("\n✗ Failed: No templates created")
        print("Check your video files and directory structure")
        return False

    # Save templates
    try:
        with open(output_file, 'wb') as f:
            pickle.dump(templates, f)
        print(f"\n✓ Templates saved: {output_file}")
        print(f"  Total templates: {len(templates)}")

        # Print summary
        template_names = {}
        for template in templates:
            name = template.name
            template_names[name] = template_names.get(name, 0) + 1

        print("\nTemplate summary:")
        for name in sorted(template_names.keys()):
            count = template_names[name]
            print(f"  - {name:<25} {count:>3} templates")

        return True

    except Exception as e:
        print(f"\n✗ Failed to save templates: {e}")
        return False


def main():
    """Parse arguments and rebuild templates."""
    parser = argparse.ArgumentParser(
        description="Rebuild gesture templates from video recordings"
    )
    parser.add_argument(
        "--input",
        type=str,
        default="./gesture_videos",
        help="Input directory with gesture subdirectories (default: ./gesture_videos)"
    )
    parser.add_argument(
        "--output",
        type=str,
        default="./gesture_templates.pkl",
        help="Output template file (default: ./gesture_templates.pkl)"
    )
    parser.add_argument(
        "-v", "--verbose",
        action="store_true",
        help="Verbose output"
    )

    args = parser.parse_args()

    # Run rebuild
    success = rebuild_templates(args.input, args.output, args.verbose)

    if not success:
        sys.exit(1)

    print("\n✓ Template rebuild complete")
    print("\nNext steps:")
    print("  1. Test with: python evaluate_gestures.py")
    print("  2. Run service: python gesture_service_refactored.py")


if __name__ == "__main__":
    main()
