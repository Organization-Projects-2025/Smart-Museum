#!/usr/bin/env python3
"""
Quick diagnostic to test gesture service connectivity.
Run this to verify the service is accepting connections.
"""

import socket
import time
import sys

def test_connection(host="127.0.0.1", port=5001, timeout=5):
    """Test socket connection to gesture service."""
    print(f"╔══════════════════════════════════════════════════════╗")
    print(f"║  Gesture Service Connection Diagnostic")
    print(f"╚══════════════════════════════════════════════════════╝\n")
    
    print(f"Testing connection to: {host}:{port}")
    print(f"Timeout: {timeout}s\n")
    
    try:
        print("① Creating socket...")
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(timeout)
        
        print(f"② Attempting to connect to {host}:{port}...")
        s.connect((host, port))
        print("   ✓ Connection successful!\n")
        
        print("③ Sending PING command...")
        s.send(b"PING\n")
        
        print("④ Waiting for response...")
        response = s.recv(1024).decode()
        print(f"   ✓ Received: {response}\n")
        
        print("═══════════════════════════════════════════════════════")
        print("✓ All tests passed! Service is working correctly.")
        print("═══════════════════════════════════════════════════════\n")
        
        s.close()
        return True
        
    except socket.timeout:
        print(f"   ✗ Connection timed out after {timeout}s")
        print(f"   → Server not responding at {host}:{port}")
        print(f"   → Make sure unified_museum_server.py is running\n")
        return False
        
    except ConnectionRefusedError:
        print(f"   ✗ Connection refused at {host}:{port}")
        print(f"   → Server is not listening on that port")
        print(f"   → Make sure unified_museum_server.py is running\n")
        return False
        
    except socket.gaierror:
        print(f"   ✗ Could not resolve hostname: {host}")
        print(f"   → Try using 127.0.0.1 instead of localhost\n")
        return False
        
    except Exception as e:
        print(f"   ✗ Connection failed: {e}\n")
        return False

if __name__ == "__main__":
    # Test default connection
    success = test_connection()
    
    # If failed, try alternatives
    if not success:
        print("Trying alternative addresses...\n")
        
        # Try localhost if 127.0.0.1 failed
        print("Attempt 2: Testing with 'localhost'")
        test_connection("localhost", 5001)

