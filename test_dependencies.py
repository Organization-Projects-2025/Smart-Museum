#!/usr/bin/env python3
"""
Test script to verify all Python dependencies are correctly installed.
Run this after installing requirements.txt to ensure everything works.
"""

import sys
import importlib
import subprocess
from typing import Tuple, List


def test_import(module_name: str, package_name: str = None) -> Tuple[bool, str]:
    """Test if a module can be imported."""
    try:
        importlib.import_module(module_name)
        return True, f"✓ {package_name or module_name} installed"
    except ImportError as e:
        return False, f"✗ {package_name or module_name} NOT installed: {e}"


def test_version(module_name: str, attr: str = None) -> Tuple[bool, str]:
    """Test if we can get version information."""
    try:
        module = importlib.import_module(module_name)
        if attr and hasattr(module, attr):
            version = getattr(module, attr)
            return True, f"  Version: {version}"
        elif hasattr(module, '__version__'):
            return True, f"  Version: {module.__version__}"
        else:
            return True, f"  Version: Unknown (installed)"
    except Exception as e:
        return False, f"  Error getting version: {e}"


def main():
    """Run all dependency tests."""
    print("=" * 70)
    print("Smart Museum - Python Dependencies Test")
    print("=" * 70)
    print()

    # Core dependencies
    print("Core Dependencies:")
    print("-" * 70)

    core_tests = [
        ("numpy", "numpy"),
        ("cv2", "opencv-python"),
        ("click", "Click"),
    ]

    for module, package in core_tests:
        success, message = test_import(module, package)
        print(message)
        if success:
            _, version_msg = test_version(module)
            print(version_msg)

    print()

    # Face recognition
    print("Face Recognition & Authentication:")
    print("-" * 70)

    face_tests = [
        ("face_recognition_models", "face-recognition-models"),
        ("PIL", "Pillow"),
    ]

    # Test dlib separately (platform-specific)
    try:
        import dlib
        print("✓ dlib installed (binary)")
        if hasattr(dlib, '__version__'):
            print(f"  Version: {dlib.__version__}")
    except ImportError:
        print("✗ dlib NOT installed (required for face recognition)")

    for module, package in face_tests:
        success, message = test_import(module, package)
        print(message)
        if success:
            _, version_msg = test_version(module)
            print(version_msg)

    print()

    # Bluetooth
    print("Bluetooth Communication:")
    print("-" * 70)

    try:
        import bluetooth
        print("✓ pybluez2 installed")
    except ImportError:
        print("⚠ pybluez2 NOT installed (Bluetooth features unavailable)")
        print("  Note: pybluez2 is Linux-only; Windows users can skip this")

    print()

    # MediaPipe
    print("MediaPipe (Hand Tracking & Pose):")
    print("-" * 70)

    success, message = test_import("mediapipe", "mediapipe")
    print(message)
    if success:
        _, version_msg = test_version("mediapipe")
        print(version_msg)

    print()

    # DollarPy
    print("DollarPy (Gesture Recognition):")
    print("-" * 70)

    success, message = test_import("dollarpy", "dollarpy")
    print(message)
    if success:
        _, version_msg = test_version("dollarpy")
        print(version_msg)

    print()

    # YOLO
    print("YOLO Object Detection:")
    print("-" * 70)

    yolo_tests = [
        ("ultralytics", "ultralytics"),
        ("torch", "torch"),
        ("torchvision", "torchvision"),
    ]

    for module, package in yolo_tests:
        success, message = test_import(module, package)
        print(message)
        if success:
            _, version_msg = test_version(module)
            print(version_msg)

    print()

    # Optional dependencies
    print("Optional Dependencies:")
    print("-" * 70)

    optional_tests = [
        ("pytest", "pytest"),
        ("black", "black"),
        ("flake8", "flake8"),
    ]

    for module, package in optional_tests:
        success, message = test_import(module, package)
        print(message)

    print()

    # Python version check
    print("Python Environment:")
    print("-" * 70)
    print(f"Python version: {sys.version}")
    print(f"Python executable: {sys.executable}")

    # Check if running in virtual environment
    if hasattr(sys, 'real_prefix') or (hasattr(sys, 'base_prefix') and sys.base_prefix != sys.prefix):
        print("✓ Running in virtual environment")
    else:
        print("⚠ NOT running in virtual environment (recommended)")

    print()

    # Summary
    print("=" * 70)
    print("Test Summary:")
    print("-" * 70)

    # Count successes and failures
    all_tests = core_tests + face_tests + [(None, None)] + yolo_tests
    passed = 0
    failed = 0

    for module, package in all_tests:
        if module is None:
            continue  # Skip dlib (handled separately)
        try:
            importlib.import_module(module)
            passed += 1
        except ImportError:
            failed += 1

    print(f"Core dependencies: {passed} passed, {failed} failed")

    # Service availability
    print()
    print("Service Availability:")
    print("-" * 70)

    services = {
        "Face Auth (5000)": ["face_recognition_models", "dlib"],
        "Gesture (5001)": ["mediapipe", "dollarpy"],
        "Gaze+Emotion (5002)": ["mediapipe"],
        "YOLO Context (5003)": ["ultralytics", "torch"],
        "Hand Track (5004)": ["mediapipe"],
    }

    for service, deps in services.items():
        available = True
        for dep in deps:
            try:
                importlib.import_module(dep)
            except ImportError:
                available = False
                break

        status = "✓ Available" if available else "✗ Missing dependencies"
        print(f"  {service}: {status}")

    print()
    print("=" * 70)

    # Final verdict
    if failed == 0:
        print("✓ All core dependencies installed successfully!")
        print()
        print("You can now run the unified server:")
        print("  python python/server/unified_museum_server.py")
        return 0
    else:
        print(f"✗ {failed} core dependencies missing")
        print()
        print("Install missing dependencies:")
        print("  pip install -r requirements.txt")
        print()
        print("For face_recognition on Windows:")
        print("  pip install 'face_recognition>=1.3.0' --no-deps")
        return 1


if __name__ == "__main__":
    sys.exit(main())