"""
Development/test_emotion.py
===========================
Live facial expression tester — shows webcam feed with real-time
emotion scores overlaid. Press Q to quit.

Uses the same HSEmotion model as the main server (gaze_emotion_service.py).

Usage:
    python Development/test_emotion.py
    python Development/test_emotion.py --camera 2
"""

import argparse
import sys
import os
import urllib.request

import cv2
import numpy as np

# ── Load model ────────────────────────────────────────────────────────────────
try:
    from hsemotion_onnx.facial_emotions import HSEmotionRecognizer
    rec = HSEmotionRecognizer(model_name="enet_b0_8_best_afew")
    print("HSEmotion model loaded")
except Exception as e:
    print(f"ERROR: Could not load HSEmotion: {e}")
    print("Run: pip install hsemotion-onnx")
    sys.exit(1)

# HSEmotion label order — from rec.idx_to_class (8 classes including Contempt)
LABELS = ["Anger", "Contempt", "Disgust", "Fear", "Happiness", "Neutral", "Sadness", "Surprise"]
COLORS = {
    "Anger":    (0,   0,   220),
    "Contempt": (0,   80,  160),
    "Disgust":  (0,   140, 0  ),
    "Fear":     (180, 0,   180),
    "Happiness":(0,   200, 0  ),
    "Neutral":  (180, 180, 180),
    "Sadness":  (200, 100, 0  ),
    "Surprise": (0,   200, 200),
}

# Face detector
face_cascade = cv2.CascadeClassifier(
    cv2.data.haarcascades + "haarcascade_frontalface_default.xml"
)


def draw_bar(img, label, score, y, bar_max_w=200):
    bar_w = int(score * bar_max_w)
    color = COLORS.get(label, (200, 200, 200))
    cv2.rectangle(img, (10, y), (10 + bar_w, y + 18), color, -1)
    cv2.rectangle(img, (10, y), (10 + bar_max_w, y + 18), (80, 80, 80), 1)
    cv2.putText(img, f"{label}: {score:.2f}", (215, y + 14),
                cv2.FONT_HERSHEY_SIMPLEX, 0.45, (240, 240, 240), 1, cv2.LINE_AA)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--camera", type=int,
                        default=int(os.environ.get("MUSEUM_CAMERA", "0")))
    args = parser.parse_args()
``
    cap = cv2.VideoCapture(args.camera, cv2.CAP_DSHOW)
    if not cap.isOpened():
        cap = cv2.VideoCapture(args.camera)
    if not cap.isOpened():
        print(f"ERROR: Cannot open camera {args.camera}")
        sys.exit(1)

    cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
    print(f"Camera {args.camera} opened — press Q to quit")

    # Rolling average for smoothing
    history = []
    SMOOTH  = 5

    while True:
        ok, frame = cap.read()
        if not ok:
            continue

        display = frame.copy()
        gray    = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces   = face_cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))

        scores_dict = None

        if len(faces) > 0:
            # Use the largest face
            x, y, w, h = max(faces, key=lambda f: f[2] * f[3])
            cv2.rectangle(display, (x, y), (x+w, y+h), (0, 255, 0), 2)

            # Crop face with padding
            pad = 20
            fh, fw = frame.shape[:2]
            x1 = max(0, x - pad)
            y1 = max(0, y - pad)
            x2 = min(fw, x + w + pad)
            y2 = min(fh, y + h + pad)
            face_crop = frame[y1:y2, x1:x2]

            if face_crop.size > 0:
                emotion, scores = rec.predict_emotions(face_crop, logits=False)

                # Normalise scores to sum to 1
                total = sum(scores) or 1.0
                norm  = [s / total for s in scores]

                # Smooth
                history.append(norm)
                if len(history) > SMOOTH:
                    history.pop(0)
                smoothed = [sum(h[i] for h in history) / len(history)
                            for i in range(len(LABELS))]
                t2 = sum(smoothed) or 1.0
                smoothed = [s / t2 for s in smoothed]

                scores_dict = dict(zip(LABELS, smoothed))
                dominant    = max(scores_dict, key=scores_dict.get)

                cv2.putText(display, dominant, (x, y - 10),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.9,
                            COLORS.get(dominant, (255,255,255)), 2, cv2.LINE_AA)
        else:
            cv2.putText(display, "No face", (10, 30),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 0, 200), 2)
            history.clear()

        # Draw score bars
        if scores_dict:
            sorted_scores = sorted(scores_dict.items(), key=lambda x: -x[1])
            for i, (label, score) in enumerate(sorted_scores):
                draw_bar(display, label, score, 10 + i * 24)

        cv2.imshow("Emotion Test — press Q to quit", display)
        if cv2.waitKey(1) & 0xFF == ord("q"):
            break

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
