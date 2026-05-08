
## Configured Historical Figures

The system includes the following Egyptian historical figures:

1. **Cleopatra VII** (Symbol ID: 1)
   - Period: 69 BC - 30 BC
   - Last Pharaoh of Ancient Egypt

2. **Nefertiti** (Symbol ID: 2)
   - Period: c. 1370 BC - 1330 BC
   - Great Royal Wife of Akhenaten

3. **Tutankhamun** (Symbol ID: 7)
   - Period: c. 1341 BC - 1323 BC
   - The Boy King

4. **Ramesses II** (Symbol ID: 4)
   - Period: c. 1303 BC - 1213 BC
   - Ramesses the Great

5. **Hatshepsut** (Symbol ID: 5)
   - Period: c. 1507 BC - 1458 BC
   - Egypt's Longest-Reigning Female Pharaoh

6. **Akhenaten** (Symbol ID: 6)
   - Period: c. 1380 BC - 1336 BC
   - The Heretic Pharaoh

---

## AUTHENTICATION & LOGIN ACTIONS

### ACTION 1: Face Recognition Login
**Physical User Action:** User's face is detected by camera

**System Response:**
1. Face recognition system activates
2. Face is matched against user database
3. User profile is loaded
4. Login success message displayed

---

### ACTION 2: Bluetooth Device Verification (2FA)
**Physical User Action:** User's registered Bluetooth device comes within range

**System Response:**
1. Bluetooth connection established
2. Device ID verified against user profile
3. Two-factor authentication completed
4. Full system access granted

---

### ACTION 3: Logout via Menu
**Physical User Action:** User rotates menu marker to "Logout" slice and swipes up

**System Response:**
1. Logout confirmation displayed
2. User session terminated
3. User data saved
4. System returns to idle/login screen

---

## OBJECT DETECTION (YOLO) ACTIONS

### ACTION 4: Show Single Figure via Object Detection
**Physical User Action:** User holds or places a physical object representing a historical figure in camera view

**System Response:**
1. Camera detects object via YOLO
2. Figure identified (Cleopatra, Nefertiti, Tutankhamun, Ramesses II, Hatshepsut, or Akhenaten)
3. Recognition countdown begins (3 seconds)
4. Figure introduction slideshow starts
5. Figure name, period, and description displayed
6. Solo content slides play automatically
7. Content remains visible while object is detected

---

### ACTION 5: Show Two Figures via Object Detection (Relationship)
**Physical User Action:** User holds or places two physical objects representing historical figures in camera view simultaneously

**System Response:**
1. Camera detects both objects via YOLO
2. Both figures identified
3. System recognizes relationship exists between the two figures
4. Relationship content slideshow begins
5. Relationship title displayed (e.g., "Husband & Wife — The Revolutionary Royal Couple")
6. Relationship story slides play automatically
7. Images and text about their connection shown
8. Content remains visible while both objects are detected

---

### ACTION 6: Open Circular Menu via Object Detection
**Physical User Action:** User holds or places menu object (e.g., apple) in camera view

**System Response:**
1. Camera detects menu object via YOLO
2. Circular menu appears on screen
3. Menu items displayed with icons (Favorites, Watched, Home, Logout)
4. First item highlighted by default
5. Menu ready for navigation

---

### ACTION 7: Navigate Menu - Move Object Up
**Physical User Action:** User moves menu object upward in camera view

**System Response:**
1. Upward movement detected
2. If submenu exists for selected item: submenu opens showing items
3. If no submenu: selected action executes (e.g., play content, logout)
4. Transition animation plays

---

### ACTION 8: Navigate Menu - Move Object Right
**Physical User Action:** User moves menu object to the right in camera view

**System Response:**
1. Rightward movement detected
2. Menu selection moves to next option
3. Next menu item highlights
4. Menu icons update to show new selection
5. Second-level menu preview appears for newly selected item

---

### ACTION 9: Navigate Menu - Move Object Left
**Physical User Action:** User moves menu object to the left in camera view

**System Response:**
1. Leftward movement detected
2. Menu selection moves to previous option
3. Previous menu item highlights
4. Menu icons update to show new selection
5. Second-level menu preview appears for newly selected item

---

### ACTION 10: Navigate Menu - Move Object Down
**Physical User Action:** User moves menu object downward in camera view

**System Response:**
1. Downward movement detected
2. Current submenu closes (if in submenu)
3. Returns to parent menu level
4. Previous menu state restored
5. Transition animation plays

---

<!-- ### ACTION 11: Alternative Menu Navigation - Two Objects (Left/Right Arrows)
**Physical User Action:** User shows left arrow or right arrow object in camera view

**System Response:**
1. Arrow object detected via YOLO
2. If left arrow: menu selection moves to previous option
3. If right arrow: menu selection moves to next option
4. Selected menu item highlights
5. Menu updates to show new selection -->

---

### ACTION 12: Remove Object from Detection
**Physical User Action:** User removes object from camera view

**System Response:**
1. Object removal detected
2. Associated content fades out
3. If menu object: circular menu closes
4. If figure object: figure content clears
5. System returns to previous state or idle

## TUIO MARKER PLACEMENT & MOVEMENT ACTIONS

### ACTION 14: Place Single Figure Marker
**Physical User Action:** User places one figure marker (Cleopatra, Nefertiti, etc.) on table

**System Response:**
1. Marker detected and figure identified
2. Recognition countdown begins (3 seconds)
3. Idle animation displays with countdown
4. After countdown: Figure introduction slideshow starts
5. Figure name, period, and description displayed
6. Solo content slides play automatically

---

### ACTION 16: Place Two Figure Markers (Not Facing)
**Physical User Action:** User places two figure markers on table, not facing each other

**System Response:**
1. Both markers detected and figures identified
2. System recognizes figures are not in relationship position
3. Each figure displays independently
4. Individual figure content shown for each marker
5. Hint may display: "Rotate figures to face each other"

---

### ACTION 17: Place Two Figure Markers (Facing Each Other)
**Physical User Action:** User places two figure markers facing each other (rotation aligned)

**System Response:**
1. Both markers detected and figures identified
2. System recognizes relationship configuration
3. Relationship content slideshow begins
4. Relationship title displayed (e.g., "Husband & Wife — The Revolutionary Royal Couple")
5. Relationship story slides play automatically
6. Images and text about their connection shown

---

### ACTION 18: Lift Marker from Table
**Physical User Action:** User removes a marker from the table surface

**System Response:**
1. Marker removal detected
2. Associated content fades out
3. If menu marker: circular menu closes
4. If figure marker: figure content clears
5. System returns to previous state or idle

---

### ACTION 19: Move Marker on Table
**Physical User Action:** User slides marker to different position on table

**System Response:**
1. Marker position tracked continuously
2. Associated content moves with marker
3. No state change unless moved off table

---

### ACTION 20: Rotate Marker on Table
**Physical User Action:** User rotates marker while it remains on table

**System Response:**
1. Marker rotation angle tracked
2. If two figures: relationship detection updates based on facing angle
3. If menu marker: menu items rotate, different item moves to top position
4. Selected menu item highlights
5. Second-level menu preview may appear

---

## CIRCULAR MENU ACTIONS

### ACTION 21: Open Circular Menu
**Physical User Action:** User places menu marker on table

**System Response:**
1. Menu marker detected
2. Circular menu appears around marker
3. Menu items displayed with icons (Favorites, Watched, Home, Logout)
4. Top item highlighted by default
5. Menu ready for interaction

---

### ACTION 22: Rotate Menu to Select Item
**Physical User Action:** User rotates menu marker to bring desired item to top

**System Response:**
1. Menu rotates with marker
2. Items cycle through top position
3. Currently selected item highlights
4. Second-level menu preview appears for selected item
5. Menu icons update to show selection

---

### ACTION 23: Swipe Up on Menu (Enter Submenu)
**Physical User Action:** User moves menu marker upward (decreasing Y coordinate)

**System Response:**
1. Swipe up gesture detected
2. Selected menu item activates
3. If submenu exists: submenu opens showing items
4. If action: action executes (e.g., play content, logout)
5. Transition animation plays

---

### ACTION 24: Swipe Down on Menu (Exit Submenu)
**Physical User Action:** User moves menu marker downward (increasing Y coordinate)

**System Response:**
1. Swipe down gesture detected
2. Current submenu closes
3. Returns to parent menu level
4. Previous menu state restored
5. Transition animation plays



### ACTION 28: Close Circular Menu
**Physical User Action:** User lifts menu marker from table

**System Response:**
1. Menu marker removal detected
2. Circular menu fades out
3. Menu state saved
4. System returns to previous content view
5. Figure content remains if figure marker still present

---

## HAND GESTURE ACTIONS

### ACTION 29: Hand Tracking Display
**Physical User Action:** User moves hand over table surface (detected by camera/sensor)

**System Response:**
1. Hand position detected
2. Hand position overlay appears on screen
3. Visual indicator follows hand movement
4. Used for 3D object manipulation or gesture detection

---

### ACTION 30: Wave Gesture
**Physical User Action:** User performs waving motion with hand

**System Response:**
1. Wave gesture pattern recognized
2. Gesture-specific action triggers (context-dependent)
3. May activate help, dismiss notifications, or trigger navigation
4. Visual feedback confirms gesture detected

---

## SYSTEM-INITIATED RESPONSES (No Direct Physical Action)

These are automatic system behaviors triggered by time, state changes, or as consequences of previous actions:

### Slideshow Auto-Advance
- **Trigger:** Time-based (each slide has duration)
- **Response:** Next slide in sequence displays automatically

### Idle Animation Display
- **Trigger:** No markers on table for extended period
- **Response:** Starfield animation with twinkling stars, pulsing idle graphics

### Recognition Countdown
- **Trigger:** New marker placed
- **Response:** 3-2-1 countdown displayed before content loads

### Toast Notifications Auto-Dismiss
- **Trigger:** 3 seconds after toast appears
- **Response:** Toast fades out automatically

### Theme Application
- **Trigger:** User login completed
- **Response:** User's saved theme colors and font sizes applied to interface

### Content Fade Animations
- **Trigger:** Content transitions
- **Response:** Fade in/out effects between slides and states

### Figure Information Display
- **Trigger:** Figure recognized
- **Response:** Figure name, period, description automatically shown

### Relationship Title Display
- **Trigger:** Two figures facing detected
- **Response:** Relationship title automatically appears

---

## SUMMARY

**Total Physical User Actions: 30**

**Physical Action Types:**
- Face Recognition: 1 action
- Bluetooth Connection: 1 action
- Object Detection (YOLO): 10 actions
- TUIO Marker Placement: 4 actions
- TUIO Marker Removal: 2 actions
- TUIO Marker Rotation: 4 actions
- TUIO Marker Movement: 2 actions
- Gesture - Swipe Up: 3 actions
- Gesture - Swipe Down: 1 action
- Hand Gesture: 2 actions
- Menu Selection (rotation + swipe): 1 action



**System Response Categories:**
- Content Display (slideshows, text, images)
- Menu Navigation (open, close, rotate, select)
- State Transitions (login, logout, idle)
- Visual Feedback (highlights, overlays, animations)
- Notifications (toasts, confirmations)
- Auto-behaviors (timers, countdowns, auto-advance)

---

## END OF INTERFACE ACTIONS DOCUMENTATION