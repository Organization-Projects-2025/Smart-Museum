import cv2
import mediapipe as mp

mp_holistic = mp.solutions.holistic
mp_drawing_utils = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

holistic = mp_holistic.Holistic(
    static_image_mode=True, min_detection_confidence=0.5, model_complexity=2
)

cap = cv2.VideoCapture(0)
while cap.isOpened():
    _, frame = cap.read()
    # convert to RGB
    frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)

    # process the frame for pose detection
    results = holistic.process(frame_rgb)

    # We make a copy of the frame as we don't want to override the image
    annotated_image = frame.copy()

    # Drawing Landmarks
    mp_drawing_utils.draw_landmarks(
        annotated_image,
        results.pose_landmarks,
        mp_holistic.POSE_CONNECTIONS,
        landmark_drawing_spec=mp_drawing_styles.get_default_pose_landmarks_style(),
    )

    cv2.imshow("Output", annotated_image)
    
    if cv2.waitKey(1) == ord("q"):
        cap.release()
        cv2.destroyAllWindows()
        break

cap.release()
cv2.destroyAllWindows()