#!/usr/bin/env python3
"""
Unified Smart Museum Server - Single process with fault-isolated services.

All Python services run in one process but each service is isolated:
- Face ID + Bluetooth (port 5000)
- Gesture recognition (port 5001)
- Gaze + emotion (port 5002)
- YOLO context (port 5003)
- Hand tracking (port 5004)

If one service fails, it logs the error but other services continue running.
Shared camera hub for vision services to avoid camera conflicts.

Usage:
    python python/server/unified_museum_server.py

Environment variables:
    MUSEUM_CAMERA - Camera index (default: 0)
    GAZE_EMOTION_MIRROR=1 - Mirror camera frames
    DISABLE_<SERVICE>=1 - Disable specific service (e.g., DISABLE_HAND_TRACK=1)
"""

import os
import sys
import time
import threading
import socket
import json
import traceback
from datetime import datetime
from typing import Dict, Optional, Callable

# Add paths for imports
THIS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.dirname(os.path.dirname(THIS_DIR))
DOLLARPY_DIR = os.path.join(PROJECT_ROOT, "dollarpy-service")

if DOLLARPY_DIR not in sys.path:
    sys.path.insert(0, DOLLARPY_DIR)
if THIS_DIR not in sys.path:
    sys.path.insert(0, THIS_DIR)


# ============================================================================
# Logging System
# ============================================================================

class ServiceLogger:
    """Thread-safe logger with timestamps and service identification."""

    def __init__(self):
        self.lock = threading.Lock()
        self.service_colors = {
            "FACE_AUTH": "\033[92m",      # Green
            "GESTURE": "\033[93m",       # Yellow
            "GAZE_EMOTION": "\033[94m",  # Blue
            "YOLO_CONTEXT": "\033[95m",  # Magenta
            "HAND_TRACK": "\033[96m",    # Cyan
            "MAIN": "\033[97m",          # White
        }
        self.reset_color = "\033[0m"

    def _format_message(self, service: str, level: str, message: str) -> str:
        timestamp = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        color = self.service_colors.get(service, "\033[97m")
        level_symbols = {
            "INFO": "✓",
            "WARN": "⚠",
            "ERROR": "✗",
            "DEBUG": "○"
        }
        symbol = level_symbols.get(level, "•")
        return f"{color}[{timestamp}] {service} {symbol} {message}{self.reset_color}"

    def log(self, service: str, level: str, message: str):
        with self.lock:
            print(self._format_message(service, level, message))

    def info(self, service: str, message: str):
        self.log(service, "INFO", message)

    def warn(self, service: str, message: str):
        self.log(service, "WARN", message)

    def error(self, service: str, message: str):
        self.log(service, "ERROR", message)

    def debug(self, service: str, message: str):
        self.log(service, "DEBUG", message)


logger = ServiceLogger()


# ============================================================================
# Service Health Monitoring
# ============================================================================

class ServiceHealth:
    """Track health status of each service."""

    def __init__(self):
        self.lock = threading.Lock()
        self.status: Dict[str, Dict] = {}

    def register(self, service_name: str):
        with self.lock:
            self.status[service_name] = {
                "running": False,
                "last_heartbeat": None,
                "error_count": 0,
                "last_error": None
            }

    def set_running(self, service_name: str, running: bool):
        with self.lock:
            if service_name in self.status:
                self.status[service_name]["running"] = running
                self.status[service_name]["last_heartbeat"] = time.time()

    def log_error(self, service_name: str, error: str):
        with self.lock:
            if service_name in self.status:
                self.status[service_name]["error_count"] += 1
                self.status[service_name]["last_error"] = error
                self.status[service_name]["last_heartbeat"] = time.time()

    def get_status(self, service_name: str) -> Optional[Dict]:
        with self.lock:
            return self.status.get(service_name)

    def get_all_status(self) -> Dict:
        with self.lock:
            return self.status.copy()


health_monitor = ServiceHealth()


# ============================================================================
# Service Wrapper with Fault Isolation
# ============================================================================

class ServiceWrapper:
    """Wrap a service with fault isolation and automatic error handling."""

    def __init__(self, name: str, start_func: Callable, port: int):
        self.name = name
        self.start_func = start_func
        self.port = port
        self.thread: Optional[threading.Thread] = None
        self.should_stop = threading.Event()
        self.enabled = not os.environ.get(f"DISABLE_{name}", "").strip() in ("1", "true", "yes")

    def _run_with_isolation(self):
        """Run service with try-catch to prevent crashes from affecting other services."""
        health_monitor.register(self.name)
        logger.info(self.name, f"Starting service on port {self.port}")

        try:
            self.start_func()
        except Exception as e:
            error_msg = f"Service crashed: {str(e)}"
            logger.error(self.name, error_msg)
            logger.debug(self.name, traceback.format_exc())
            health_monitor.log_error(self.name, error_msg)
            health_monitor.set_running(self.name, False)
        finally:
            logger.info(self.name, f"Service stopped")

    def start(self):
        """Start service in isolated thread."""
        if not self.enabled:
            logger.info(self.name, f"Service disabled via DISABLE_{self.name}=1")
            return

        if self.thread is None or not self.thread.is_alive():
            self.should_stop.clear()
            self.thread = threading.Thread(
                target=self._run_with_isolation,
                name=f"{self.name}-service",
                daemon=True
            )
            self.thread.start()
            health_monitor.set_running(self.name, True)

    def stop(self):
        """Signal service to stop."""
        self.should_stop.set()
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=2.0)


# ============================================================================
# Shared Camera Hub
# ============================================================================

try:
    from shared_camera_hub import SharedCameraHub, default_camera_index, default_mirror
    CAMERA_HUB_AVAILABLE = True
except ImportError:
    CAMERA_HUB_AVAILABLE = False
    logger.warn("MAIN", "shared_camera_hub not available, services will use individual cameras")


def get_camera_hub():
    """Get shared camera hub or None if not available."""
    if not CAMERA_HUB_AVAILABLE:
        return None

    try:
        idx = default_camera_index()
        mirror = default_mirror()
        hub = SharedCameraHub(camera_index=idx, mirror=mirror)
        logger.info("MAIN", f"Camera hub initialized: camera={idx}, mirror={mirror}")
        return hub
    except Exception as e:
        logger.error("MAIN", f"Failed to initialize camera hub: {e}")
        return None


# ============================================================================
# Service Start Functions
# ============================================================================

def start_face_auth_service():
    """Start Face ID + Bluetooth service (port 5000)."""
    import python_server
    port = int(os.environ.get("PYTHON_SERVER_PORT", "5000"))
    python_server.start_server("127.0.0.1", port)


def start_gesture_service():
    """Start gesture recognition service (port 5001)."""
    try:
        # Use refactored gesture service with improved accuracy
        from gesture_service_refactored import GestureRecognitionService
        hub = get_camera_hub() if CAMERA_HUB_AVAILABLE else None
        service = GestureRecognitionService(host="127.0.0.1", port=5001, camera_hub=hub)
        service.start_server()
    except ImportError as e:
        logger.error("GESTURE", f"Failed to import gesture_service_refactored: {e}")
        raise


def start_gaze_emotion_service():
    """Start gaze + emotion service (port 5002)."""
    try:
        import gaze_emotion_service as ge
        hub = get_camera_hub() if CAMERA_HUB_AVAILABLE else None
        if hub:
            ge.set_shared_camera_hub(hub)
        ge.run_tcp_server("127.0.0.1", 5002)
    except ImportError as e:
        logger.error("GAZE_EMOTION", f"Failed to import gaze_emotion_service: {e}")
        raise
    finally:
        try:
            import gaze_emotion_service as ge
            ge.clear_shared_camera_hub()
        except:
            pass


def start_yolo_context_service():
    """Start YOLO context service (port 5003)."""
    try:
        import yolo_context_service as yc
        hub = get_camera_hub() if CAMERA_HUB_AVAILABLE else None
        if hub:
            yc.set_shared_camera_hub(hub)
        yc.run_tcp_server("127.0.0.1", 5003)
    except ImportError as e:
        logger.error("YOLO_CONTEXT", f"Failed to import yolo_context_service: {e}")
        raise
    finally:
        try:
            import yolo_context_service as yc
            yc.clear_shared_camera_hub()
        except:
            pass


def start_hand_track_service():
    """Start hand tracking service (port 5004)."""
    try:
        import hand_tracker_service
        port = int(os.environ.get("HAND_TRACK_PORT", "5004"))
        hand_tracker_service.start_server("127.0.0.1", port)
    except ImportError as e:
        logger.error("HAND_TRACK", f"Failed to import hand_tracker_service: {e}")
        raise


# ============================================================================
# Main Unified Server
# ============================================================================

class UnifiedMuseumServer:
    """Main server that orchestrates all museum services."""

    def __init__(self):
        self.services: Dict[str, ServiceWrapper] = {}
        self.camera_hub = None
        self.shutdown_event = threading.Event()

    def initialize_services(self):
        """Initialize all service wrappers."""
        self.services = {
            "FACE_AUTH": ServiceWrapper("FACE_AUTH", start_face_auth_service, 5000),
            "GESTURE": ServiceWrapper("GESTURE", start_gesture_service, 5001),
            "GAZE_EMOTION": ServiceWrapper("GAZE_EMOTION", start_gaze_emotion_service, 5002),
            "YOLO_CONTEXT": ServiceWrapper("YOLO_CONTEXT", start_yolo_context_service, 5003),
            "HAND_TRACK": ServiceWrapper("HAND_TRACK", start_hand_track_service, 5004),
        }

    def start_all_services(self):
        """Start all enabled services."""
        logger.info("MAIN", "Starting all services...")

        # Initialize camera hub first if available
        if CAMERA_HUB_AVAILABLE:
            self.camera_hub = get_camera_hub()

        # Start each service
        for name, service in self.services.items():
            service.start()

        # Print service status
        self.print_service_status()

    def print_service_status(self):
        """Print current status of all services."""
        logger.info("MAIN", "=" * 60)
        logger.info("MAIN", "Service Status:")
        for name, service in self.services.items():
            status = "ENABLED" if service.enabled else "DISABLED"
            port = service.port
            logger.info("MAIN", f"  {name:15} - Port {port:4} - {status}")
        logger.info("MAIN", "=" * 60)

    def monitor_services(self):
        """Monitor service health and log status periodically."""
        while not self.shutdown_event.is_set():
            time.sleep(30)  # Check every 30 seconds (not 10)

            status = health_monitor.get_all_status()
            for service_name, service_status in status.items():
                if not service_status["running"]:
                    if service_status["error_count"] > 0:
                        logger.warn(
                            "MAIN",
                            f"{service_name} is down (errors: {service_status['error_count']}, "
                            f"last error: {service_status['last_error']})"
                        )
                # Removed heartbeat check - services don't implement heartbeat updates
                # The services are running fine, just not sending heartbeat signals

    def shutdown(self):
        """Gracefully shutdown all services."""
        logger.info("MAIN", "Shutting down all services...")
        self.shutdown_event.set()

        # Stop all services
        for name, service in self.services.items():
            service.stop()

        # Shutdown camera hub
        if self.camera_hub:
            try:
                self.camera_hub.shutdown()
                logger.info("MAIN", "Camera hub shut down")
            except Exception as e:
                logger.error("MAIN", f"Error shutting down camera hub: {e}")

        logger.info("MAIN", "All services stopped")

    def run(self):
        """Run the unified server."""
        try:
            self.initialize_services()
            self.start_all_services()

            # Start monitoring thread
            monitor_thread = threading.Thread(
                target=self.monitor_services,
                name="service-monitor",
                daemon=True
            )
            monitor_thread.start()

            logger.info("MAIN", "Unified Museum Server running. Press Ctrl+C to stop.")
            logger.info("MAIN", "C# clients can connect to:")
            logger.info("MAIN", "  - Face Auth:     127.0.0.1:5000")
            logger.info("MAIN", "  - Gesture:       127.0.0.1:5001")
            logger.info("MAIN", "  - Gaze+Emotion:  127.0.0.1:5002")
            logger.info("MAIN", "  - YOLO Context:  127.0.0.1:5003")
            logger.info("MAIN", "  - Hand Track:    127.0.0.1:5004")

            # Keep main thread alive
            while not self.shutdown_event.is_set():
                time.sleep(1.0)

        except KeyboardInterrupt:
            logger.info("MAIN", "Received shutdown signal")
        finally:
            self.shutdown()


def main():
    """Main entry point."""
    # Change to project root for relative imports
    os.chdir(PROJECT_ROOT)

    server = UnifiedMuseumServer()
    server.run()


if __name__ == "__main__":
    main()