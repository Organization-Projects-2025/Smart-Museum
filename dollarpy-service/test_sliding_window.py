"""
Quick test to verify gesture service is working with sliding window
"""
import socket
import json
import time

def send_command(sock, command):
    """Send command and get response"""
    sock.send((command + "\n").encode('utf-8'))
    response = sock.recv(4096).decode('utf-8').strip()
    return json.loads(response)

def main():
    print("Testing Gesture Service Connection...")
    
    try:
        # Connect to service
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect(("127.0.0.1", 5001))
        print("✓ Connected to gesture service")
        
        # Ping
        response = send_command(sock, "PING")
        print(f"PING: {response}")
        
        # Start tracking
        response = send_command(sock, "START_TRACKING")
        print(f"START_TRACKING: {response}")
        
        # Check status periodically
        print("\nMonitoring for 30 seconds... Perform gestures now!")
        for i in range(30):
            time.sleep(1)
            response = send_command(sock, "STATUS")
            
            frames = response.get('frames', 0)
            last_gesture = response.get('last_gesture')
            in_cooldown = response.get('in_cooldown', False)
            cooldown_remaining = response.get('cooldown_remaining', 0)
            
            status_text = f"[{i+1}s] Frames: {frames}/60"
            if last_gesture:
                status_text += f" | Last: {last_gesture}"
            if in_cooldown:
                status_text += f" | Cooldown: {cooldown_remaining:.1f}s"
            
            print(status_text)
        
        # Stop tracking
        response = send_command(sock, "STOP_TRACKING")
        print(f"\nSTOP_TRACKING: {response}")
        
        sock.close()
        print("✓ Test complete")
        
    except ConnectionRefusedError:
        print("✗ Connection refused - is the gesture service running?")
        print("  Run: python python/server/unified_museum_server.py")
    except Exception as e:
        print(f"✗ Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    main()
