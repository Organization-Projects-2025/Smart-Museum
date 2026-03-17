"""
Grand Egyptian Museum - Face Recognition with Demographics
Auto Sign-In / Sign-Up with Age, Gender, Emotion Detection
"""

import cv2
import face_recognition
import numpy as np
import tkinter as tk
from tkinter import font as tkfont
from PIL import Image, ImageTk
import os
import time
import json
from datetime import datetime
from deepface import DeepFace

# Configuration
PEOPLE_DIR = "people"
ASSETS_DIR = "assets"
USER_DATA_FILE = "user_database.json"
COUNTDOWN_SECONDS = 3
RECOGNITION_TIMEOUT = 10
FACE_DISTANCE_THRESHOLD = 0.6

# Global variables
known_face_encodings = []
known_face_names = []
user_database = {}


def load_user_database():
    """Load user database with demographics"""
    global user_database
    if os.path.exists(USER_DATA_FILE):
        with open(USER_DATA_FILE, 'r') as f:
            user_database = json.load(f)
    else:
        user_database = {}


def save_user_database():
    """Save user database"""
    with open(USER_DATA_FILE, 'w') as f:
        json.dump(user_database, f, indent=2)


def analyze_face_demographics(frame, face_location):
    """Analyze face for age, gender, emotion using DeepFace"""
    try:
        top, right, bottom, left = face_location
        face_img = frame[top:bottom, left:right]
        
        # Analyze with DeepFace (no race/ethnicity)
        result = DeepFace.analyze(face_img, actions=['age', 'gender', 'emotion'],
                                 enforce_detection=False, silent=True)
        
        # Extract first result if list
        if isinstance(result, list):
            result = result[0]
        
        demographics = {
            'age': result.get('age', 'Unknown'),
            'gender': result.get('dominant_gender', 'Unknown'),
            'emotion': result.get('dominant_emotion', 'Unknown'),  # Current emotion only
            'gender_confidence': result.get('gender', {}).get(result.get('dominant_gender', ''), 0),
            'emotion_confidence': result.get('emotion', {}).get(result.get('dominant_emotion', ''), 0)
        }
        
        return demographics
    except Exception as e:
        print(f"Demographics analysis error: {str(e)}")
        return {
            'age': 'Unknown',
            'gender': 'Unknown',
            'emotion': 'Unknown'
        }



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
                num_str = filename.replace("user", "").split(".")[0]
                user_numbers.append(int(num_str))
            except:
                pass
    
    return max(user_numbers) + 1 if user_numbers else 0


def save_new_user(frame, user_name, demographics):
    """Save a new user's face image and demographics (age, gender only)"""
    filepath = os.path.join(PEOPLE_DIR, f"{user_name}.jpg")
    cv2.imwrite(filepath, frame)
    
    # Check if user already exists in database (edge case)
    if user_name in user_database:
        # User exists, increment visit
        user_database[user_name]['visit_count'] = user_database[user_name].get('visit_count', 0) + 1
        user_database[user_name]['last_visit'] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    else:
        # New user - start at visit 1
        user_database[user_name] = {
            'registered_date': datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            'age': demographics.get('age', 'Unknown'),
            'gender': demographics.get('gender', 'Unknown'),
            'visit_count': 1,
            'last_visit': datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        }
    save_user_database()
    
    print(f"✓ Saved new user: {user_name}")
    print(f"  Age: {demographics.get('age')}, Gender: {demographics.get('gender')}")
    print(f"  Visit count: {user_database[user_name]['visit_count']}")
    return filepath


def update_user_visit(user_name, demographics=None):
    """Update user visit information and demographics if missing"""
    if user_name in user_database:
        # User exists - increment visit count
        user_database[user_name]['visit_count'] = user_database[user_name].get('visit_count', 0) + 1
        user_database[user_name]['last_visit'] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        
        # Update age and gender ONLY if they are Unknown and demographics are provided
        if demographics:
            if user_database[user_name].get('age') == 'Unknown' and demographics.get('age') != 'Unknown':
                user_database[user_name]['age'] = demographics.get('age')
                print(f"  Recorded age: {demographics.get('age')}")
            
            if user_database[user_name].get('gender') == 'Unknown' and demographics.get('gender') != 'Unknown':
                user_database[user_name]['gender'] = demographics.get('gender')
                print(f"  Recorded gender: {demographics.get('gender')}")
    else:
        # User recognized but not in database (legacy user from people folder)
        # Create database entry with demographics if available
        age = demographics.get('age', 'Unknown') if demographics else 'Unknown'
        gender = demographics.get('gender', 'Unknown') if demographics else 'Unknown'
        
        user_database[user_name] = {
            'registered_date': datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
            'age': age,
            'gender': gender,
            'visit_count': 1,
            'last_visit': datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        }
    save_user_database()
    print(f"  Updated visit count: {user_database[user_name]['visit_count']}")



def draw_face_guide(frame):
    """Draw face positioning guide on frame"""
    height, width = frame.shape[:2]
    center_x, center_y = width // 2, height // 2
    oval_width, oval_height = 200, 250
    
    cv2.ellipse(frame, (center_x, center_y), (oval_width, oval_height), 
                0, 0, 360, (255, 255, 255), 2)
    cv2.line(frame, (center_x - 20, center_y), (center_x + 20, center_y), (255, 255, 255), 1)
    cv2.line(frame, (center_x, center_y - 20), (center_x, center_y + 20), (255, 255, 255), 1)
    
    return frame


def check_face_position(face_location, frame_shape):
    """Check if face is well-positioned"""
    height, width = frame_shape[:2]
    top, right, bottom, left = face_location
    
    face_center_x = (left + right) // 2
    face_center_y = (top + bottom) // 2
    face_width = right - left
    
    frame_center_x = width // 2
    frame_center_y = height // 2
    
    x_offset = abs(face_center_x - frame_center_x)
    y_offset = abs(face_center_y - frame_center_y)
    
    is_centered = x_offset < width * 0.2 and y_offset < height * 0.2
    is_good_size = 0.15 < (face_width / width) < 0.4
    
    feedback = []
    if not is_centered:
        feedback.append("Move to center")
    if not is_good_size:
        if face_width / width < 0.15:
            feedback.append("Move closer")
        else:
            feedback.append("Move back")
    
    return is_centered and is_good_size, feedback


def auto_face_recognition():
    """Main automatic face recognition with demographics"""
    
    cap = cv2.VideoCapture(0)
    if not cap.isOpened():
        print("Error: Could not open camera!")
        return None, None, None
    
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    
    print("\n" + "="*60)
    print("Grand Egyptian Museum - Face Recognition")
    print("="*60)
    print("\nAnalyzing: Face Recognition + Age + Gender + Emotion")
    print("Press 'Q' to cancel\n")
    
    start_time = time.time()
    recognized_user = None
    countdown_started = False
    countdown_start_time = None
    capture_frame = None
    face_stable_time = None
    demographics = None
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        frame = cv2.flip(frame, 1)
        display_frame = frame.copy()
        height, width = display_frame.shape[:2]
        
        rgb_frame = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        small_frame = cv2.resize(rgb_frame, (0, 0), fx=0.25, fy=0.25)
        
        face_locations = face_recognition.face_locations(small_frame)
        face_locations = [(top*4, right*4, bottom*4, left*4) for (top, right, bottom, left) in face_locations]
        
        if len(face_locations) == 0:
            draw_face_guide(display_frame)
            cv2.putText(display_frame, "No face detected", (50, 50),
                       cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
            face_stable_time = None
            countdown_started = False
            
        elif len(face_locations) == 1:
            face_location = face_locations[0]
            top, right, bottom, left = face_location
            
            is_good_position, feedback = check_face_position(face_location, frame.shape)
            
            if is_good_position:
                cv2.rectangle(display_frame, (left, top), (right, bottom), (0, 255, 0), 3)
                
                if face_stable_time is None:
                    face_stable_time = time.time()
                
                time_stable = time.time() - face_stable_time
                
                if time_stable < 1.0:
                    cv2.putText(display_frame, "Hold still...", (50, 50),
                               cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)
                else:
                    if not countdown_started:
                        face_encodings = face_recognition.face_encodings(rgb_frame, [face_location])
                        
                        if len(face_encodings) > 0:
                            face_encoding = face_encodings[0]
                            
                            if len(known_face_encodings) > 0:
                                matches = face_recognition.compare_faces(known_face_encodings, face_encoding, 
                                                                        tolerance=FACE_DISTANCE_THRESHOLD)
                                face_distances = face_recognition.face_distance(known_face_encodings, face_encoding)
                                
                                if len(face_distances) > 0:
                                    best_match_index = np.argmin(face_distances)
                                    
                                    if matches[best_match_index]:
                                        recognized_user = known_face_names[best_match_index]
                                        capture_frame = frame.copy()
                                        
                                        # Analyze demographics for display
                                        print("Analyzing demographics...")
                                        demographics = analyze_face_demographics(frame, face_location)
                                        break
                            
                            countdown_started = True
                            countdown_start_time = time.time()
                    
                    if countdown_started:
                        elapsed = time.time() - countdown_start_time
                        remaining = COUNTDOWN_SECONDS - int(elapsed)
                        
                        if remaining > 0:
                            cv2.putText(display_frame, f"New User - Analyzing & Capturing in {remaining}...", (50, 50),
                                       cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 255), 2)
                            cv2.putText(display_frame, "Stay still!", (50, 90),
                                       cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
                        else:
                            capture_frame = frame.copy()
                            print("Analyzing demographics...")
                            demographics = analyze_face_demographics(frame, face_location)
                            break
            else:
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
            cv2.putText(display_frame, "Multiple faces detected!", (50, 50),
                       cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 0, 255), 2)
            face_stable_time = None
            countdown_started = False
        
        cv2.imshow("Grand Egyptian Museum - Face Recognition", display_frame)
        
        if cv2.waitKey(1) & 0xFF == ord('q'):
            print("\nCancelled by user")
            cap.release()
            cv2.destroyAllWindows()
            return None, None, None
        
        if time.time() - start_time > RECOGNITION_TIMEOUT + COUNTDOWN_SECONDS + 5:
            print("\nTimeout")
            break
    
    cap.release()
    cv2.destroyAllWindows()
    
    if recognized_user:
        print(f"\n✓ Welcome back, {recognized_user}!")
        update_user_visit(recognized_user, demographics)  # Pass demographics to update if needed
        visit_num = user_database.get(recognized_user, {}).get('visit_count', 1)  # Now read updated count
        print(f"  Visit #{visit_num}")
        return recognized_user, False, demographics
    elif capture_frame is not None and demographics:
        user_number = get_next_user_number()
        new_user_name = f"user{user_number}"
        save_new_user(capture_frame, new_user_name, demographics)
        load_all_faces()
        
        print(f"\n✓ Welcome, new user! Registered as {new_user_name}")
        return new_user_name, True, demographics
    
    return None, None, None



class WelcomeScreen(tk.Tk):
    """Welcome screen with demographics display"""
    
    def __init__(self, user_name, is_new_user=False, current_emotion=None):
        super().__init__()
        
        self.title("Grand Egyptian Museum")
        self.geometry("1024x768")
        self.configure(bg="#1a1a2e")
        
        title_font = tkfont.Font(family='Arial', size=48, weight="bold")
        subtitle_font = tkfont.Font(family='Arial', size=24)
        user_font = tkfont.Font(family='Arial', size=32, weight="bold")
        info_font = tkfont.Font(family='Arial', size=18)
        
        container = tk.Frame(self, bg="#1a1a2e")
        container.pack(expand=True, fill="both", padx=50, pady=50)
        
        # Get visit count for both new and returning users
        visit_count = user_database.get(user_name, {}).get('visit_count', 1)
        
        if is_new_user:
            welcome_text = "Welcome to"
            user_text = f"Hello, {user_name}!"
            subtitle_text = f"Visit #{visit_count} • You have been registered successfully"
        else:
            welcome_text = "Welcome Back to"
            user_text = f"Hello, {user_name}!"
            subtitle_text = f"Visit #{visit_count} • We're glad to see you again"
        
        tk.Label(container, text=welcome_text, font=subtitle_font, 
                fg="#ffffff", bg="#1a1a2e").pack(pady=(50, 10))
        
        tk.Label(container, text="Grand Egyptian Museum", font=title_font,
                fg="#d4af37", bg="#1a1a2e").pack(pady=10)
        
        line_frame = tk.Frame(container, bg="#d4af37", height=3)
        line_frame.pack(fill="x", padx=200, pady=20)
        
        tk.Label(container, text=user_text, font=user_font,
                fg="#ffffff", bg="#1a1a2e").pack(pady=30)
        
        tk.Label(container, text=subtitle_text, font=subtitle_font,
                fg="#a0a0a0", bg="#1a1a2e").pack(pady=10)
        
        # Demographics display - show saved data + current emotion
        user_data = user_database.get(user_name, {})
        if user_data:
            demo_frame = tk.Frame(container, bg="#2a2a3e", relief="ridge", bd=2)
            demo_frame.pack(pady=20, padx=100, fill="x")
            
            tk.Label(demo_frame, text="Visitor Profile", font=('Arial', 16, 'bold'),
                    fg="#d4af37", bg="#2a2a3e").pack(pady=10)
            
            # Show saved age and gender
            age = user_data.get('age', 'N/A')
            gender = user_data.get('gender', 'N/A')
            demo_info = f"Age: {age} • Gender: {gender}"
            tk.Label(demo_frame, text=demo_info, font=info_font,
                    fg="#ffffff", bg="#2a2a3e").pack(pady=5)
            
            # Show current emotion (not saved, just detected now)
            if current_emotion:
                emotion_info = f"Current Mood: {current_emotion}"
                tk.Label(demo_frame, text=emotion_info, font=info_font,
                        fg="#ffcc00", bg="#2a2a3e").pack(padx=20, pady=(5, 15))
        
        info_text = "Explore the wonders of ancient Egypt\nDiscover 5,000 years of history"
        tk.Label(container, text=info_text, font=('Arial', 18),
                fg="#ffffff", bg="#1a1a2e", justify="center").pack(pady=30)
        
        timestamp = datetime.now().strftime("%B %d, %Y • %I:%M %p")
        tk.Label(container, text=timestamp, font=('Arial', 14),
                fg="#808080", bg="#1a1a2e").pack(pady=10)
        
        close_btn = tk.Button(container, text="Continue", font=('Arial', 16, 'bold'),
                             fg="#1a1a2e", bg="#d4af37", activebackground="#b8941f",
                             command=self.destroy, padx=40, pady=15, relief="flat",
                             cursor="hand2")
        close_btn.pack(pady=20)
        
        self.bind('<Escape>', lambda e: self.destroy())
        self.after(15000, self.destroy)


def main():
    """Main application entry point"""
    
    print("="*60)
    print("Grand Egyptian Museum - Face Recognition System")
    print("with Age, Gender & Emotion Detection")
    print("="*60)
    print("\nLoading registered users...")
    
    load_user_database()
    load_all_faces()
    
    print("\nStarting face recognition...")
    print("="*60)
    
    user_name, is_new_user, demographics = auto_face_recognition()
    
    if user_name:
        print("\nOpening welcome screen...")
        # Extract current emotion (not saved, just for display)
        current_emotion = demographics.get('emotion') if demographics else None
        
        app = WelcomeScreen(user_name, is_new_user, current_emotion)
        app.mainloop()
        
        print("\n" + "="*60)
        print("Session complete. Thank you for visiting!")
        print("="*60)
    else:
        print("\nNo user detected. Exiting...")


if __name__ == "__main__":
    main()
