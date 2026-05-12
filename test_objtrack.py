"""
Quick diagnostic for object tracking service.
Run from project root:
  .venv\Scripts\python.exe test_objtrack.py
"""
import os, sys, time

ROOT = os.path.dirname(os.path.abspath(__file__))
SERVER_DIR = os.path.join(ROOT, "python", "server")
sys.path.insert(0, SERVER_DIR)

print("=== 1. YOLO model load ===")
try:
    from ultralytics import YOLO
    model_path = os.path.join(ROOT, "yolo11s.pt")
    print(f"Looking for model at: {model_path}")
    print(f"Exists: {os.path.exists(model_path)}")
    model = YOLO(model_path)
    print("YOLO loaded OK")
except Exception as e:
    print(f"YOLO FAILED: {e}")
    sys.exit(1)

print("\n=== 2. Camera hub frames ===")
try:
    from camera_hub import CameraHub
    hub = CameraHub(camera_index=0)
    hub.start()
    time.sleep(1.0)
    frame = hub.get_frame()
    if frame is not None:
        print(f"Frame OK: shape={frame.shape}")
    else:
        print("ERROR: No frame received from camera hub")
        sys.exit(1)
except Exception as e:
    print(f"CameraHub FAILED: {e}")
    sys.exit(1)

print("\n=== 3. YOLO inference on live frame ===")
try:
    frame = hub.get_frame()
    results = model.track(frame, persist=True, verbose=False,
                          classes=[74], conf=0.15, tracker="bytetrack.yaml")
    detected = results[0].boxes is not None and len(results[0].boxes) > 0
    print(f"Inference OK — watch detected: {detected}")
    if detected:
        box = results[0].boxes.xywh.cpu()[0]
        print(f"  Box center: ({float(box[0]):.1f}, {float(box[1]):.1f})")
    else:
        print("  (Point your watch at the camera and re-run to confirm detection)")
except Exception as e:
    print(f"Inference FAILED: {e}")
    sys.exit(1)

print("\n=== All checks passed ===")
hub.stop()
