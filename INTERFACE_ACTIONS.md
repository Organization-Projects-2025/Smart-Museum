# Smart Grand Egyptian Museum - Interface Actions & Interaction Types

## Interaction Type Definitions

- **TUIO Marker Placement**: Physical marker placed on table
- **TUIO Marker Removal**: Physical marker lifted from table
- **TUIO Marker Rotation**: Physical marker rotated on table
- **TUIO Marker Movement**: Physical marker moved on table
- **Gesture - Swipe Up**: Marker moved upward (decreasing Y)
- **Gesture - Swipe Down**: Marker moved downward (increasing Y)
- **Gesture - Rotation**: Marker rotated to specific angle
- **Keyboard Input**: Key pressed on keyboard
- **Face Recognition**: Face detected and matched
- **Bluetooth Connection**: Bluetooth device verified
- **Hand Gesture**: Hand motion detected
- **Time-based Trigger**: Automatic action after duration
- **State Change**: System state transition
- **Menu Selection**: Item selected from menu

---

## AUTHENTICATION & LOGIN INTERFACE ACTIONS

### ACTION 1: "Start Face ID Login"
**Interaction Type:** Face Recognition

### ACTION 2: "Scan Face for Authentication"
**Interaction Type:** Face Recognition

### ACTION 3: "Match Face to User Database"
**Interaction Type:** Face Recognition

### ACTION 4: "Verify Bluetooth Device"
**Interaction Type:** Bluetooth Connection

### ACTION 5: "Complete Login & Load Profile"
**Interaction Type:** State Change

### ACTION 6: "Logout from System"
**Interaction Type:** Menu Selection

---

## MARKER PLACEMENT & RECOGNITION INTERFACE ACTIONS

### ACTION 7: "Place Single Figure Marker"
**Interaction Type:** TUIO Marker Placement

### ACTION 8: "Place Menu Marker"
**Interaction Type:** TUIO Marker Placement

### ACTION 9: "Place Two Figure Markers"
**Interaction Type:** TUIO Marker Placement (x2)

### ACTION 10: "Lift Marker from Table"
**Interaction Type:** TUIO Marker Removal

### ACTION 11: "Move Marker on Table"
**Interaction Type:** TUIO Marker Movement

### ACTION 12: "Rotate Marker on Table"
**Interaction Type:** TUIO Marker Rotation

---

## RECOGNITION & STATE TRANSITION INTERFACE ACTIONS

### ACTION 13: "Recognize Single Figure"
**Interaction Type:** State Change + Time-based Trigger

### ACTION 14: "Recognize Two Figures Not Facing"
**Interaction Type:** State Change + Time-based Trigger

### ACTION 15: "Recognize Two Figures Facing"
**Interaction Type:** State Change + Time-based Trigger

---

## SLIDESHOW INTERFACE ACTIONS

### ACTION 16: "Display Figure Introduction Slideshow"
**Interaction Type:** State Change

### ACTION 17: "Display Figure Solo Content"
**Interaction Type:** State Change

### ACTION 18: "Display Relationship Content Slideshow"
**Interaction Type:** State Change

### ACTION 19: "Display Text Slide"
**Interaction Type:** Time-based Trigger

### ACTION 20: "Display Image Slide"
**Interaction Type:** Time-based Trigger

### ACTION 21: "Auto-Advance to Next Slide"
**Interaction Type:** Time-based Trigger

### ACTION 22: "Complete Slideshow"
**Interaction Type:** Time-based Trigger

---

## CIRCULAR MENU INTERFACE ACTIONS

### ACTION 23: "Open Circular Menu"
**Interaction Type:** TUIO Marker Placement

### ACTION 24: "Close Circular Menu"
**Interaction Type:** TUIO Marker Removal

### ACTION 25: "Rotate Menu - Select Top Item"
**Interaction Type:** Gesture - Rotation

### ACTION 26: "Swipe Up on Menu - Enter Submenu"
**Interaction Type:** Gesture - Swipe Up

### ACTION 27: "Swipe Down on Menu - Exit Submenu"
**Interaction Type:** Gesture - Swipe Down

### ACTION 28: "Select 'Favorite' from Top Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 29: "Select 'Favorites' from Top Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 30: "Select Favorite Item and Play"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 31: "Select 'Watched' from Top Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 32: "Select Watched Item and Play"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 33: "Select 'Home' from Top Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 34: "Select 'Logout' from Top Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

### ACTION 35: "Unfavorite Item from Favorites Menu"
**Interaction Type:** Menu Selection + Gesture - Swipe Up

---

## SCENE OBJECT INTERACTION INTERFACE ACTIONS

### ACTION 36: "Display Scene Objects"
**Interaction Type:** State Change

### ACTION 37: "Hover Over Scene Object"
**Interaction Type:** TUIO Marker Rotation

### ACTION 38: "Hold on Scene Object"
**Interaction Type:** Time-based Trigger

---

## TOAST NOTIFICATION INTERFACE ACTIONS

### ACTION 39: "Show Success Toast - Added to Favorites"
**Interaction Type:** Time-based Trigger

### ACTION 40: "Show Success Toast - Already in Favorites"
**Interaction Type:** Time-based Trigger

### ACTION 41: "Show Info Toast - No Active Figure"
**Interaction Type:** Time-based Trigger

### ACTION 42: "Show Info Toast - Removed from Favorites"
**Interaction Type:** Time-based Trigger

---

## IDLE STATE INTERFACE ACTIONS

### ACTION 43: "Display Idle Animation"
**Interaction Type:** State Change

### ACTION 44: "Display Recognition Countdown"
**Interaction Type:** Time-based Trigger

### ACTION 45: "Display Hint - Rotate Figures"
**Interaction Type:** State Change

---

## HAND TRACKING INTERFACE ACTIONS

### ACTION 46: "Display Hand Position Overlay"
**Interaction Type:** Hand Gesture

### ACTION 47: "Detect Wave Gesture"
**Interaction Type:** Hand Gesture

---

## KEYBOARD INTERFACE ACTIONS

### ACTION 48: "Toggle Fullscreen"
**Interaction Type:** Keyboard Input

### ACTION 49: "Exit Application"
**Interaction Type:** Keyboard Input

---

## THEME & PERSONALIZATION INTERFACE ACTIONS

### ACTION 50: "Apply User Theme Colors"
**Interaction Type:** State Change

### ACTION 51: "Apply User Font Sizes"
**Interaction Type:** State Change

---

## ANIMATION INTERFACE ACTIONS

### ACTION 52: "Fade In Slide Content"
**Interaction Type:** Time-based Trigger

### ACTION 53: "Fade Out Slide Content"
**Interaction Type:** Time-based Trigger

### ACTION 54: "Pulsing Idle Animation"
**Interaction Type:** Time-based Trigger

### ACTION 55: "Starfield Twinkling"
**Interaction Type:** Time-based Trigger

---

## MENU PREVIEW INTERFACE ACTIONS

### ACTION 56: "Show Second-Level Menu Preview"
**Interaction Type:** Gesture - Rotation

### ACTION 57: "Highlight Selected Menu Item"
**Interaction Type:** Gesture - Rotation

### ACTION 58: "Display Menu Icons"
**Interaction Type:** State Change

---

## FIGURE INFORMATION INTERFACE ACTIONS

### ACTION 59: "Display Figure Name"
**Interaction Type:** State Change

### ACTION 60: "Display Figure Period"
**Interaction Type:** State Change

### ACTION 61: "Display Figure Description"
**Interaction Type:** State Change

### ACTION 62: "Display Relationship Title"
**Interaction Type:** State Change

---

## SUMMARY

**Total Interface Actions: 62**

**Interaction Type Breakdown:**
- TUIO Marker Placement: 3 actions
- TUIO Marker Removal: 2 actions
- TUIO Marker Rotation: 2 actions
- TUIO Marker Movement: 1 action
- Gesture - Swipe Up: 5 actions
- Gesture - Swipe Down: 1 action
- Gesture - Rotation: 4 actions
- Keyboard Input: 2 actions
- Face Recognition: 3 actions
- Bluetooth Connection: 1 action
- Hand Gesture: 2 actions
- Time-based Trigger: 20 actions
- State Change: 12 actions
- Menu Selection: 5 actions

---

## INTERACTION TYPE REFERENCE

| Interaction Type | Count | Examples |
|---|---|---|
| Time-based Trigger | 20 | Slide auto-advance, toast display, animations |
| State Change | 12 | Login complete, figure recognized, menu opened |
| Gesture - Swipe Up | 5 | Enter submenu, select item, play slideshow |
| Gesture - Rotation | 4 | Menu rotation, scene object hover |
| TUIO Marker Placement | 3 | Open menu, place figure, place two figures |
| Menu Selection | 5 | Select favorite, select home, select logout |
| TUIO Marker Removal | 2 | Close menu, clear table |
| Gesture - Swipe Down | 1 | Exit submenu |
| TUIO Marker Movement | 1 | Move marker on table |
| Face Recognition | 3 | Start login, scan face, match user |
| Hand Gesture | 2 | Display hand overlay, detect wave |
| Keyboard Input | 2 | Toggle fullscreen, exit app |
| Bluetooth Connection | 1 | Verify device |

---

## END OF INTERFACE ACTIONS DOCUMENTATION
