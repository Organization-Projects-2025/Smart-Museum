╔════════════════════════════════════════════════════════════════════════════╗
║ SOCKET & THREAD IMPLEMENTATION SUMMARY ║
║ Following Code Samples patterns - SIMPLE socket communication ║
║ NOW INCLUDES: Bluetooth 2FA + Face ID Recognition ║
╚════════════════════════════════════════════════════════════════════════════╝

═══════════════════════════════════════════════════════════════════════════════

1. PYTHON SOCKET SERVER (Backend) - BOTH Bluetooth & Face ID
   ═══════════════════════════════════════════════════════════════════════════════

📄 FILE: python/server/python_server.py
LOCATION: C:\Personal Files\Education final\HCI\Project\Project\python\server\python_server.py

BLUETOOTH COMPONENTS:
✓ Line 26-31: normalize_mac() function - Normalizes MAC addresses to uppercase with colons

✓ Line 34-47: scan_bluetooth() function

- Scans for Bluetooth device with specific MAC - Returns "FOUND:Name:MAC" or "NOT_FOUND" or "ERROR:message"

FACE ID COMPONENTS (from Code Samples/simple_face_id.py):
✓ Line 50-67: load_known_faces() function - Loads face encodings from people/ directory - Uses face_recognition library

✓ Line 70-116: scan_face_id() function - Opens camera and captures frames - Detects faces and matches against known encodings - Returns "FOUND:user_id" or "NOT_FOUND" or "ERROR:message"

THREADING & CONNECTION HANDLING:
✓ Line 119-152: handle_client() function (Thread handler) - Handles each client connection in separate thread - Supports commands:
• "bluetooth_scan MAC" → Bluetooth scanning
• "face_id_scan" → Face recognition
• "exit" → Close connection

✓ Line 155-171: start_server() function - Binding to localhost:5000 - Listen for connections - Accept clients in loop, spawn thread for each

HOW TO RUN:
.\.venv\Scripts\python.exe python\server\python_server.py

Server will output: "Server listening on localhost:5000"

REQUIRED PACKAGES:

- pybluez2 (for Bluetooth)
- face_recognition (for Face ID)
- opencv-python (for camera access)
- numpy

═══════════════════════════════════════════════════════════════════════════════ 2. C# SOCKET CLIENT (Frontend)
═══════════════════════════════════════════════════════════════════════════════

📄 FILE: SocketClient.cs
LOCATION: C:\Personal Files\Education final\HCI\Project\Project\C#\SocketClient.cs

COMPONENTS:
✓ Line 5: Public variables: stream, client

✓ Line 7-22: connectToSocket(host, port) method - Connects to Python server - Returns true/false

✓ Line 24-35: sendMessage(msg) method - Sends string message to server

✓ Line 37-49: recieveMessage() method - Receives string message from server

✓ Line 51-61: closeConnection() method - Closes socket connection

✓ Line 63-72: sendCommandAndWait(cmd) method (NEW) - Convenience method: send command and receive response in one call

═══════════════════════════════════════════════════════════════════════════════ 3. FACE ID SERVICE - NEW (Updated to use Sockets)
═══════════════════════════════════════════════════════════════════════════════

📄 FILE: AuthIntegration.cs
LOCATION: C:\Personal Files\Education final\HCI\Project\Project\C#\AuthIntegration.cs

COMPONENTS:
✓ Line 376-430: FaceIdService class (NEW)

     Line 381-382:   Comment explaining socket connection

     Line 385-402:   SOCKET CONNECTION CODE
                     - Creates SocketClient instance
                     - Connects to localhost:5000
                     - Sends "face_id_scan" command
                     - Receives response

     Line 404-410:   Response parsing
                     - Checks "FOUND:" prefix
                     - Extracts user_id

     Line 412-414:   NOT_FOUND handling

     Line 416-419:   ERROR handling

     Line 421-424:   Exception handling

═══════════════════════════════════════════════════════════════════════════════ 4. BLUETOOTH 2FA SERVICE (Updated to use Sockets)
═══════════════════════════════════════════════════════════════════════════════

📄 FILE: AuthIntegration.cs
LOCATION: C:\Personal Files\Education final\HCI\Project\Project\C#\AuthIntegration.cs

COMPONENTS:
✓ Line 303-374: BluetoothTwoFactorService class (REFACTORED)

     Line 308-309:   Comment explaining socket connection

     Line 313-331:   SOCKET CONNECTION CODE
                     - Creates SocketClient instance
                     - Connects to localhost:5000
                     - Sends "bluetooth_scan MAC" command
                     - Receives response

     Line 333-336:   Response parsing
                     - Checks "FOUND:" prefix
                     - Extracts DeviceName and MAC

     Line 338-340:   NOT_FOUND handling

     Line 342-345:   ERROR handling

     Line 347-349:   Exception handling

═══════════════════════════════════════════════════════════════════════════════ 5. MAIN FORM INTEGRATION
═══════════════════════════════════════════════════════════════════════════════

📄 FILE: TuioDemo.cs
LOCATION: C:\Personal Files\Education final\HCI\Project\Project\C#\TuioDemo.cs

COMPONENTS:
✓ Line 318: BluetoothTwoFactorService instantiation (UPDATED)
BEFORE: new BluetoothTwoFactorService(workspaceRoot)
AFTER: new BluetoothTwoFactorService()

✓ Line 319-320: Socket-based Bluetooth verification
Calls btService.Verify() which uses SocketClient

═══════════════════════════════════════════════════════════════════════════════ 6. THREADING MODEL
═══════════════════════════════════════════════════════════════════════════════

PATTERN USED (from Code Samples/Threads.ipynb):

Python Server Side:
✓ python/server/python_server.py Line 141: threading.Thread() for each client
Line 143: client_thread.daemon = True

C# Side:
✓ TuioDemo.cs Line 284: Thread t = new Thread(() => { ... })
(already existed in login flow)

═══════════════════════════════════════════════════════════════════════════════ 7. SIMPLE COMMUNICATION PROTOCOL
═══════════════════════════════════════════════════════════════════════════════

BLUETOOTH REQUEST (C# → Python Server):
bluetooth_scan F4:AF:E7:CA:CE:CA

BLUETOOTH RESPONSES (Python Server → C#):
✓ FOUND:SarahPhone:F4:AF:E7:CA:CE:CA
✓ NOT_FOUND
✓ ERROR:PyBluez not installed

FACE ID REQUEST (C# → Python Server):
face_id_scan

FACE ID RESPONSES (Python Server → C#):
✓ FOUND:user0
✓ NOT_FOUND
✓ ERROR:face_recognition not installed

═══════════════════════════════════════════════════════════════════════════════ 8. SETUP REQUIREMENTS
═══════════════════════════════════════════════════════════════════════════════

PYTHON PACKAGES (in .venv):
pip install pybluez2 face_recognition opencv-python numpy

FACE ID SETUP:

1.  Create "people" directory in project root:
    mkdir people
2.  Add face images named like: user0.jpg, user1.jpg, etc.
    File name (without extension) = user_id returned by Face ID service

═══════════════════════════════════════════════════════════════════════════════ 9. USAGE FLOW
═══════════════════════════════════════════════════════════════════════════════

1.  START PYTHON SERVER (once, in background):
    .\.venv\Scripts\python.exe python\server\python_server.py

2.  RUN C# APPLICATION:

    FACE ID FLOW:
    a) FaceIdService.Scan() called
    b) Creates SocketClient
    c) Connects to localhost:5000
    d) Sends "face_id_scan"
    e) Receives "FOUND:user_id" or "NOT_FOUND"
    f) Returns user_id if recognized
    g) User record loaded from CSV

    BLUETOOTH 2FA FLOW:
    h) BluetoothTwoFactorService.Verify() called with user's MAC
    i) Creates SocketClient
    j) Connects to localhost:5000
    k) Sends "bluetooth_scan MAC"
    l) Receives "FOUND:Name:MAC" or "NOT_FOUND"
    m) Returns true/false for login gate

═══════════════════════════════════════════════════════════════════════════════
✓ ALL COMPONENTS COMPILED SUCCESSFULLY
✓ FOLLOWS EXACT PATTERNS FROM CODE SAMPLES (CSharpClient.txt, serverCSharp.ipynb, simple_face_id.py)
✓ SIMPLE, HUMAN-READABLE CODE WITH COMMENTS
✓ THREADS USED FOR CONCURRENT CLIENT HANDLING
✓ FACE ID + BLUETOOTH 2FA FULLY INTEGRATED
└─ Ready for full end-to-end testing!
