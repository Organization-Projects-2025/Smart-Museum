import cv2
import numpy as np
import math
import time


HOLD_SECONDS = 5


def draw_star(image, center, outer_r, inner_r, color, thickness=-1):
    cx, cy = center
    pts = []
    for i in range(10):
        angle = -90 + i * 36
        r = outer_r if i % 2 == 0 else inner_r
        x = cx + r * math.cos(math.radians(angle))
        y = cy + r * math.sin(math.radians(angle))
        pts.append([int(x), int(y)])
    pts = np.array(pts, np.int32).reshape((-1, 1, 2))
    cv2.fillPoly(image, [pts], color) if thickness == -1 else cv2.polylines(image, [pts], True, color, thickness)


def draw_star_panel(panel, rating, saved_rating, star_count=5):
    h, w = panel.shape[:2]
    pad = 30
    spacing = (w - 2 * pad) // star_count
    outer_r = min(spacing // 3, 45)
    inner_r = outer_r // 2

    for i in range(star_count):
        cx = pad + i * spacing + spacing // 2
        cy = h // 2 - 10
        if i < rating:
            draw_star(panel, (cx, cy), outer_r, inner_r, (0, 215, 255), -1)
        elif saved_rating > 0 and i < saved_rating:
            draw_star(panel, (cx, cy), outer_r, inner_r, (0, 215, 255), 2)
        else:
            draw_star(panel, (cx, cy), outer_r, inner_r, (40, 40, 40), -1)
            draw_star(panel, (cx, cy), outer_r, inner_r, (80, 80, 80), 1)


def get_rating_from_laser(cx, w):
    fraction = cx / w
    return max(1, min(5, int(fraction * 5) + 1))


def main():
    cap = cv2.VideoCapture(0, cv2.CAP_DSHOW)
    if not cap.isOpened():
        print("Error: Could not open camera.")
        return

    cv2.namedWindow("Laser Tracker")

    cv2.createTrackbar("H Low", "Laser Tracker", 0, 180, lambda x: None)
    cv2.createTrackbar("H High", "Laser Tracker", 10, 180, lambda x: None)
    cv2.createTrackbar("S Low", "Laser Tracker", 100, 255, lambda x: None)
    cv2.createTrackbar("V Low", "Laser Tracker", 150, 255, lambda x: None)
    cv2.createTrackbar("Min Area", "Laser Tracker", 50, 2000, lambda x: None)

    cv2.setTrackbarPos("H Low", "Laser Tracker", 0)
    cv2.setTrackbarPos("H High", "Laser Tracker", 10)
    cv2.setTrackbarPos("S Low", "Laser Tracker", 120)
    cv2.setTrackbarPos("V Low", "Laser Tracker", 200)
    cv2.setTrackbarPos("Min Area", "Laser Tracker", 100)

    saved_rating = 0
    last_rating = 0
    hold_start_time = None

    print("Laser Tracker started. Press 'q' to quit, 's' to save settings.")

    while True:
        ret, frame = cap.read()
        if not ret:
            print("Error: Failed to grab frame.")
            break

        frame = cv2.flip(frame, 1)
        h, w = frame.shape[:2]
        blurred = cv2.GaussianBlur(frame, (5, 5), 0)
        hsv = cv2.cvtColor(blurred, cv2.COLOR_BGR2HSV)

        h_low = cv2.getTrackbarPos("H Low", "Laser Tracker")
        h_high = cv2.getTrackbarPos("H High", "Laser Tracker")
        s_low = cv2.getTrackbarPos("S Low", "Laser Tracker")
        v_low = cv2.getTrackbarPos("V Low", "Laser Tracker")
        min_area = cv2.getTrackbarPos("Min Area", "Laser Tracker")

        lower_red1 = np.array([0, s_low, v_low])
        upper_red1 = np.array([h_high, 255, 255])
        lower_red2 = np.array([170, s_low, v_low])
        upper_red2 = np.array([180, 255, 255])

        mask1 = cv2.inRange(hsv, lower_red1, upper_red1)
        mask2 = cv2.inRange(hsv, lower_red2, upper_red2)
        mask = cv2.bitwise_or(mask1, mask2)

        kernel = np.ones((5, 5), np.uint8)
        mask = cv2.erode(mask, kernel, iterations=1)
        mask = cv2.dilate(mask, kernel, iterations=2)

        rating = 0
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

        for cnt in contours:
            area = cv2.contourArea(cnt)
            if area < min_area:
                continue

            x, y, bw, bh = cv2.boundingRect(cnt)
            cx = x + bw // 2
            cy = y + bh // 2

            cv2.rectangle(frame, (x, y), (x + bw, y + bh), (0, 255, 0), 2)
            cv2.circle(frame, (cx, cy), 4, (0, 255, 0), -1)

            rating = get_rating_from_laser(cx, w)

            info = f"Rating: {rating}/5"
            cv2.putText(frame, info, (x, y - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)

        if rating > 0:
            if rating != last_rating:
                last_rating = rating
                hold_start_time = time.time()
            else:
                elapsed = time.time() - hold_start_time
                if elapsed >= HOLD_SECONDS and rating != saved_rating:
                    saved_rating = rating
        else:
            last_rating = 0
            hold_start_time = None

        panel_w = 400
        panel = np.zeros((h, panel_w, 3), dtype=np.uint8)

        if saved_rating > 0:
            saved_label = f"SAVED: {saved_rating}/5"
            (sw, sh), _ = cv2.getTextSize(saved_label, cv2.FONT_HERSHEY_SIMPLEX, 0.8, 2)
            cv2.putText(panel, saved_label, ((panel_w - sw) // 2, 35),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)
        else:
            label = "RATING SYSTEM"
            (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.7, 2)
            cv2.putText(panel, label, ((panel_w - tw) // 2, 35),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (200, 200, 200), 2)

        draw_star_panel(panel, rating, saved_rating)

        if rating > 0:
            elapsed = time.time() - hold_start_time if hold_start_time else 0
            progress = min(elapsed / HOLD_SECONDS, 1.0)

            if progress < 1.0:
                fill_w = int((panel_w - 100) * progress)
                cv2.rectangle(panel, (50, h - 120), (50 + fill_w, h - 105),
                              (0, 215, 255), -1)
                cv2.rectangle(panel, (50, h - 120), (panel_w - 50, h - 105),
                              (80, 80, 80), 1)

                remain = HOLD_SECONDS - elapsed
                timer_text = f"Hold {remain:.1f}s"
                (tw2, th2), _ = cv2.getTextSize(timer_text, cv2.FONT_HERSHEY_SIMPLEX, 0.5, 1)
                cv2.putText(panel, timer_text, ((panel_w - tw2) // 2, h - 130),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
            else:
                cv2.putText(panel, "LOCKED!", (panel_w // 2 - 40, h - 115),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

        rating_text = f"{rating} / 5"
        (rw, rh), _ = cv2.getTextSize(rating_text, cv2.FONT_HERSHEY_SIMPLEX, 1.2, 3)
        cv2.putText(panel, rating_text, ((panel_w - rw) // 2, h - 50),
                    cv2.FONT_HERSHEY_SIMPLEX, 1.2, (0, 215, 255), 3)

        if rating > 0:
            bar_w = int((panel_w - 80) * (rating / 5))
            cv2.rectangle(panel, (40, h - 80), (40 + bar_w, h - 65),
                          (0, 215, 255), -1)
            cv2.rectangle(panel, (40, h - 80), (panel_w - 40, h - 65),
                          (80, 80, 80), 1)

        combined = np.hstack((frame, panel))

        if combined.shape[1] > 1800:
            scale = 1800 / combined.shape[1]
            combined = cv2.resize(combined, None, fx=scale, fy=scale)

        cv2.imshow("Laser Tracker", combined)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q'):
            break
        elif key == ord('s'):
            print(f"Settings - H: {h_low}-{h_high}, S:{s_low}, V:{v_low}, MinArea:{min_area}")

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
