"""Entry point for face sign-in OpenCV UI — must run as __main__ (subprocess from python_server)."""
import os
import sys

if __name__ == "__main__":
    d = os.path.dirname(os.path.abspath(__file__))
    if d not in sys.path:
        sys.path.insert(0, d)
    try:
        from auth_lobby_cam import run_face_auth_lobby

        print(run_face_auth_lobby(), flush=True)
    except Exception as e:
        print("ERROR:" + str(e), flush=True)
        sys.exit(1)
