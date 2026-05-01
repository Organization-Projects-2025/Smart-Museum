#!/usr/bin/env python3
"""
Test script for Unified Museum Server.

This script tests connectivity to all services exposed by the unified server.
Run this while the unified server is running to verify all services are accessible.
"""

import socket
import json
import time
import sys
from typing import Dict, Any


def test_service(host: str, port: int, service_name: str, test_commands: list = None) -> bool:
    """Test connectivity to a service and optionally send test commands."""
    print(f"Testing {service_name} on {host}:{port}...", end=" ")

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))

        # Send test commands if provided
        if test_commands:
            for cmd in test_commands:
                try:
                    sock.sendall((cmd + "\n").encode())
                    time.sleep(0.1)
                    # Try to read response
                    sock.settimeout(1.0)
                    response = sock.recv(4096).decode().strip()
                    if response:
                        print(f"✓ Connected (response: {response[:50]}...)")
                        return True
                except socket.timeout:
                    continue
                except Exception as e:
                    print(f"✗ Error: {e}")
                    return False

        print("✓ Connected")
        sock.close()
        return True

    except socket.timeout:
        print("✗ Timeout")
        return False
    except ConnectionRefusedError:
        print("✗ Connection refused")
        return False
    except Exception as e:
        print(f"✗ Error: {e}")
        return False


def test_json_service(host: str, port: int, service_name: str, ping_cmd: str = "PING") -> bool:
    """Test a JSON-based service with ping command."""
    print(f"Testing {service_name} on {host}:{port}...", end=" ")

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5.0)
        sock.connect((host, port))

        # Send ping command
        sock.sendall((ping_cmd + "\n").encode())
        sock.settimeout(2.0)

        # Read response
        response = sock.recv(4096).decode().strip()
        if response:
            try:
                data = json.loads(response)
                if data.get("status") == "ok":
                    print("✓ Connected (JSON OK)")
                    sock.close()
                    return True
                else:
                    print(f"✓ Connected (JSON response: {response[:50]}...)")
                    sock.close()
                    return True
            except json.JSONDecodeError:
                print(f"✓ Connected (non-JSON response: {response[:50]}...)")
                sock.close()
                return True

        print("✓ Connected (no response)")
        sock.close()
        return True

    except socket.timeout:
        print("✗ Timeout")
        return False
    except ConnectionRefusedError:
        print("✗ Connection refused")
        return False
    except Exception as e:
        print(f"✗ Error: {e}")
        return False


def main():
    """Run all service tests."""
    print("=" * 60)
    print("Unified Museum Server - Service Test")
    print("=" * 60)
    print()

    host = "127.0.0.1"
    results = {}

    # Test all services
    print("Testing Services:")
    print("-" * 60)

    # Face Auth Service (5000)
    results["FACE_AUTH"] = test_service(
        host, 5000, "FACE_AUTH",
        test_commands=["PING"]
    )

    # Gesture Service (5001)
    results["GESTURE"] = test_json_service(
        host, 5001, "GESTURE", "PING"
    )

    # Gaze Emotion Service (5002)
    results["GAZE_EMOTION"] = test_json_service(
        host, 5002, "GAZE_EMOTION", "PING"
    )

    # YOLO Context Service (5003)
    results["YOLO_CONTEXT"] = test_json_service(
        host, 5003, "YOLO_CONTEXT", "PING"
    )

    # Hand Track Service (5004)
    results["HAND_TRACK"] = test_service(
        host, 5004, "HAND_TRACK",
        test_commands=["PING"]
    )

    print()
    print("=" * 60)
    print("Test Results:")
    print("-" * 60)

    for service, passed in results.items():
        status = "✓ PASS" if passed else "✗ FAIL"
        print(f"  {service:15} - {status}")

    print("-" * 60)

    total = len(results)
    passed = sum(1 for v in results.values() if v)
    failed = total - passed

    print(f"  Total: {total} | Passed: {passed} | Failed: {failed}")
    print("=" * 60)

    if failed == 0:
        print()
        print("✓ All services are running correctly!")
        return 0
    else:
        print()
        print(f"✗ {failed} service(s) failed to respond.")
        print("Make sure the unified server is running:")
        print("  python python/server/unified_museum_server.py")
        return 1


if __name__ == "__main__":
    sys.exit(main())