"""
Grand Egyptian Museum - Automatic Face Recognition System
Auto Sign-In / Sign-Up with Face Recognition
"""

import cv2
import face_recognition
import numpy as np
import tkinter as tk
from tkinter import font as tkfont
from PIL import Image, ImageTk
import os
import time
from datetime import datetime


# Configuration
PEOPLE_DIR = "people"
ASSETS_DIR = "assets"
COUNTDOWN_SECONDS = 3
RECOGNITION_TIMEOUT = 10  # seconds to try recognizing
FACE_DISTANCE_THRESHOLD = 0.6

# Global variables
known_face_encodings = []
known_face_names = []


def load_all_faces():
    """Load all face encodings from people directory"""
    global known_face_encodings, known_face_names
    
    known_face_encodings = []
    known_face_names = []
    
    if not os.path.exists(PEOPLE_DIR):
        os.makedirs(PEOPLE_DIR)
        return
    
    image_files = [f for f in os.listdir(PEOPLE_DIR) if f.lower().endswith(('.jpg', '.jpeg', '.png'))]
    
    for image_file in image_files:
        try:
            name = os.path.splitext(image_file)[0]
            image_path = os.path.join(PEOPLE_DIR, image_file)
            image = face_recognition.load_image_file(image_path)
            face_encodings = face_recognition.face_encodings(image)
            
            if len(face_encodings) > 0:
                known_face_encodings.append(face_encodings[0])
                known_face_names.append(name)
                print(f"  ✓ Loaded: {name}")
        except Exception as e:
            print(f"  ✗ Error loading {image_file}: {str(e)}")
    
    print(f"Loaded {len(known_face_encodings)} registered user(s)\n")


def get_next_user_number():
    """Get the next user number for new users"""
    if not os.path.exists(PEOPLE_DIR):
        return 0
    
    user_numbers = []
    for filename in os.listdir(PEOPLE_DIR):
        if filename.startswith("user") and filename.lower().endswith(('.jpg', '.jpeg', '.png')):
            try:
                # Extract number from "userN.jpg"
                num_str = filename.replace("user", "").split(".")[0]
                user_numbers.append(int(num_str))
            except:
                pass
    
    return max(user_numbers) + 1 if user_numbers else 0


def save_new_user(frame, user_name):
    """Save a new user's face image"""
    filepath = os.path.join(PEOPLE_DIR, f"{user_name}.jpg")
    cv2.imwrite(filepath, frame)
    print(f"✓ Saved new user: {user_name}")
    return filepath


def draw_face_guide(frame, face_location=None):
    """Draw face positioning guide on frame"""
    height, width = frame.shape[:2]
    
    # Draw center guide oval
    center_x, center_y = width // 2, height // 2
    oval_width, oval_height = 200, 250
    
    # Draw guide oval
    cv2.ellipse(frame, (center_x, center_y), (oval_width, oval_height), 
                0, 0, 360, (255, 255, 255), 2)
    
    # Draw crosshair
    cv2.line(frame, (center_x - 20, center_y), (center_x + 20, center_y), (255, 255, 255), 1)
    cv2.line(frame, (center_x, center_y - 20), (center_x, center_y + 20), (255, 255, 255), 1)
    
    return frame


def check_face_position(face_location, frame_shape):
    """Check if face is well-positioned"""
    height, width = frame_shape[:2]
    top, right, bottom, left = face_location
    
    # Calculate face center and size
    face_center_x = (left + right) // 2
    face_center_y = (top + bottom) // 2
    face_width = right - left
    face_height = bottom - top
    
    frame_center_x = width // 2
    frame_center_y = height // 2
    
    # Check if face is centered (within 20% of frame center)
    x_offset = abs(face_center_x - frame_center_x)
    y_offset = abs(face_center_y - frame_center_y)
    
    is_centered = x_offset < width * 0.2 and y_offset < height * 0.2
    
    # Check if face is good size (15-40% of frame width)
    is_good_size = 0.15 < (face_width / width) < 0.4
    
    # Generate feedback
    feedback = []
    if not is_centered:
        if x_offset > width * 0.2:
            feedback.append("Move to center" if face_center_x < frame_center_x else "Move to center")
        if y_offset > height * 0.2:
            feedback.append("Move up" if face_center_y > frame_center_y else "Move down")
    
    if not is_good_size:
        if face_width / width < 0.15:
            feedback.append("Move closer")
        elif face_width / width > 0.4:
            feedback.append("Move back")
    
    is_good = is_centered and is_good_size
    
    return is_good, feedback


def auto_face_recognition():
    """Main automatic face recognition function"""
    
    # Open camera
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("Error: Could not open camera!")
        return None, None
    
    # Set camera resolution
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    
    print("\n" + "="*60)
    print("Grand Egyptian Museum - Face Recognition")
    print("="*60)
    print("\nInstructions:")
    print("  • Position your face in the center oval")
    print("  • Look straight at the camera")
    print("  • Ensure good lighting")
    print("  • Stay still when countdown starts")
    print("\nPress 'Q' to cancel\n")
    
    start_time = time.time()
    recognized_user = None
    countdown_started = False
    countdown_start_time = None
    capture_frame = None
    face_stable_time = None
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        # Flip frame for mirror effect
        frame = cv2.flip(frame, 1)
        
        # Create display frame
        display_frame = frame.copy()
        height, width = display_frame.shape[:2]
        
        # Detect faces
        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        small_frame = cv2.resize(rgb_frame, (0, 0), fx=0.25, fy=0.25)
        
        face_locations = face_recognition.face_locations(small_frame)
        
        # Scale back face locations
        face_locations = [(top*4, right*4, bottom*4, left*4) for (top, right, bottom, left) in face_locations]
        
        if len(face_locations) == 0:
            # No face detected
            draw_face_guide(display_frame)
            cv2.putText(display_frame, "No face detected", (50, 50),
                       cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
            cv2.putText(display_frame, "Please position your face in the oval", (50, 90),
                       cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            face_stable_time = None
            countdown_started = False
            
        elif len(face_locations) == 1:
            # One face detected
            face_location = face_locations[0]
            top, right, bottom, left = face_location
            
            # Check face position
            is_good_position, feedback = check_face_position(face_location, frame.shape)
            
            if is_good_position:
                # Good position - draw green box
                cv2.rectangle(display_frame, (left, top), (right, bottom), (0, 255, 0), 3)
                
                # Check if face has been stable
                if face_stable_time is None:
                    face_stable_time = time.time()
                
                time_stable = time.time() - face_stable_time
                
                if time_stable < 1.0:
                    # Still stabilizing
                    cv2.putText(display_frame, "Hold still...", (50, 50),
                               cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)
                else:
                    # Face is stable, try to recognize
                    if not countdown_started:
                        # Try to recognize first
                        face_encodings = face_recognition.face_encodings(rgb_frame, [face_location])
                        
                        if len(face_encodings) > 0:
                            face_encoding = face_encodings[0]
                            
                            if len(known_face_encodings) > 0:
                                # Compare with known faces
                                matches = face_recognition.compare_faces(known_face_encodings, face_encoding, 
                                                                        tolerance=FACE_DISTANCE_THRESHOLD)
                                face_distances = face_recognition.face_distance(known_face_encodings, face_encoding)
                                
                                if len(face_distances) > 0:
                                    best_match_index = np.argmin(face_distances)
                                    
                                    if matches[best_match_index]:
                                        # Recognized!
                                        recognized_user = known_face_names[best_match_index]
                                        capture_frame = frame.copy()
                                        break
                            
                            # Not recognized - start countdown for new user
                            countdown_started = True
                            countdown_start_time = time.time()
                    
                    if countdown_started:
                        # Show countdown
                        elapsed = time.time() - countdown_start_time
                        remaining = COUNTDOWN_SECONDS - int(elapsed)
                        
                        if remaining > 0:
                            cv2.putText(display_frame, f"New User - Capturing in {remaining}...", (50, 50),
                                       cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)
                            cv2.putText(display_frame, "Stay still!", (50, 90),
                                       cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
                        else:
                            # Capture!
                            capture_frame = frame.copy()
                            break
            else:
                # Bad position - draw yellow box and show feedback
                cv2.rectangle(display_frame, (left, top), (right, bottom), (0, 255, 255), 3)
                draw_face_guide(display_frame)
                
                cv2.putText(display_frame, "Adjust position:", (50, 50),
                           cv2.FONT_HERSHEY_SIMPLEX, 0.9, (0, 255, 255), 2)
                
                for i, msg in enumerate(feedback):
                    cv2.putText(display_frame, f"• {msg}", (50, 90 + i*35),
                               cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
                
                face_stable_time = None
                countdown_started = False
        else:
            # Multiple faces
            cv2.putText(display_frame, "Multiple faces detected!", (50, 50),
                       cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
            cv2.putText(display_frame, "Please ensure only one person is visible", (50, 90),
                       cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
            face_stable_time = None
            countdown_started = False
        
        # Show timeout
        elapsed_total = time.time() - start_time
        if elapsed_total > RECOGNITION_TIMEOUT and recognized_user is None and not countdown_started:
            cv2.putText(display_frame, f"Time remaining: {int(RECOGNITION_TIMEOUT - elapsed_total)}s", 
                       (width - 300, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
        
        # Display
        cv2.imshow("Grand Egyptian Museum - Face Recognition", display_frame)
        
        # Check for quit or timeout
        if cv2.waitKey(1) & 0xFF == ord('q'):
            print("\nCancelled by user")
            cap.release()
            cv2.destroyAllWindows()
            return None, None
        
        if time.time() - start_time > RECOGNITION_TIMEOUT + COUNTDOWN_SECONDS + 5:
            print("\nTimeout - no face detected")
            break
    
    cap.release()
    cv2.destroyAllWindows()
    
    # Process result
    if recognized_user:
        print(f"\n✓ Welcome back, {recognized_user}!")
        return recognized_user, None
    elif capture_frame is not None:
        # New user - save
        user_number = get_next_user_number()
        new_user_name = f"user{user_number}"
        save_new_user(capture_frame, new_user_name)
        
        # Reload faces
        load_all_faces()
        
        print(f"\n✓ Welcome, new user! Registered as {new_user_name}")
        return new_user_name, True
    
    return None, None


class WelcomeScreen(tk.Tk):
    """Welcome screen for Grand Egyptian Museum"""
    
    def __init__(self, user_name, is_new_user=False):
        super().__init__()
        
        self.title("Grand Egyptian Museum")
        self.geometry("1024x768")
        self.configure(bg="#1a1a2e")
        
        # Make fullscreen (optional)
        # self.attributes('-fullscreen', True)
        
        # Title font
        title_font = tkfont.Font(family='Arial', size=48, weight="bold")
        subtitle_font = tkfont.Font(family='Arial', size=24)
        user_font = tkfont.Font(family='Arial', size=32, weight="bold")
        
        # Main container
        container = tk.Frame(self, bg="#1a1a2e")
        container.pack(expand=True, fill="both", padx=50, pady=50)
        
        # Welcome message
        if is_new_user:
            welcome_text = "Welcome to"
            user_text = f"Hello, {user_name}!"
            subtitle_text = "You have been registered successfully"
        else:
            welcome_text = "Welcome Back to"
            user_text = f"Hello, {user_name}!"
            subtitle_text = "We're glad to see you again"
        
        tk.Label(container, text=welcome_text, font=subtitle_font, 
                fg="#ffffff", bg="#1a1a2e").pack(pady=(50, 10))
        
        tk.Label(container, text="Grand Egyptian Museum", font=title_font,
                fg="#d4af37", bg="#1a1a2e").pack(pady=10)
        
        # Decorative line
        line_frame = tk.Frame(container, bg="#d4af37", height=3)
        line_frame.pack(fill="x", padx=200, pady=20)
        
        # User greeting
        tk.Label(container, text=user_text, font=user_font,
                fg="#ffffff", bg="#1a1a2e").pack(pady=30)
        
        tk.Label(container, text=subtitle_text, font=subtitle_font,
                fg="#a0a0a0", bg="#1a1a2e").pack(pady=10)
        
        # Museum info
        info_text = "Explore the wonders of ancient Egypt\nDiscover 5,000 years of history"
        tk.Label(container, text=info_text, font=('Arial', 18),
                fg="#ffffff", bg="#1a1a2e", justify="center").pack(pady=40)
        
        # Timestamp
        timestamp = datetime.now().strftime("%B %d, %Y • %I:%M %p")
        tk.Label(container, text=timestamp, font=('Arial', 14),
                fg="#808080", bg="#1a1a2e").pack(pady=20)
        
        # Close button
        close_btn = tk.Button(container, text="Continue", font=('Arial', 16, 'bold'),
                             fg="#1a1a2e", bg="#d4af37", activebackground="#b8941f",
                             command=self.destroy, padx=40, pady=15, relief="flat",
                             cursor="hand2")
        close_btn.pack(pady=30)
        
        # Bind escape key
        self.bind('<Escape>', lambda e: self.destroy())
        
        # Auto-close after 10 seconds
        self.after(10000, self.destroy)


def main():
    """Main application entry point"""
    
    print("="*60)
    print("Grand Egyptian Museum - Face Recognition System")
    print("="*60)
    print("\nLoading registered users...")
    
    # Load existing faces
    load_all_faces()
    
    print("\nStarting face recognition...")
    print("="*60)
    
    # Run face recognition
    user_name, is_new_user = auto_face_recognition()
    
    if user_name:
        # Show welcome screen
        print("\nOpening welcome screen...")
        app = WelcomeScreen(user_name, is_new_user)
        app.mainloop()
        
        print("\n" + "="*60)
        print("Session complete. Thank you for visiting!")
        print("="*60)
    else:
        print("\nNo user detected. Exiting...")


if __name__ == "__main__":
    main()
