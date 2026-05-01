#!/usr/bin/env bash
set -euo pipefail

# Run from repo root.
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$ROOT_DIR"

export PYTHONUNBUFFERED=1

# -----------------------------
# Camera selection (optional)
# -----------------------------
# 0 is the default webcam. Override with:
#   MUSEUM_CAMERA=1 ./run_all_servers.sh
export MUSEUM_CAMERA="${MUSEUM_CAMERA:-0}"

# -----------------------------
# YOLO mode (optional)
# -----------------------------
# 1 = mock tracks (no ultralytics required)
# 0 = real YOLO (requires: pip install ultralytics)
export YOLO_CONTEXT_MOCK="${YOLO_CONTEXT_MOCK:-1}"

# -----------------------------
# Server mode (optional)
# -----------------------------
# Set to 1 to use unified server (recommended)
# export USE_UNIFIED_SERVER=1
USE_UNIFIED="${USE_UNIFIED_SERVER:-0}"

# -----------------------------
# Ports
# -----------------------------
# unified_museum_server.py exposes:
#   5000 face/bluetooth
#   5001 gesture
#   5002 gaze/emotion
#   5003 yolo context
#   5004 hand tracking
#
# museum_vision_server.py exposes:
#   5000 face/bluetooth
#   5001 gesture
#   5002 gaze/emotion
#   5003 yolo context
#
# hand_tracker_service.py uses:
#   5004 hand tracking (3D object control)
export HAND_TRACK_PORT="${HAND_TRACK_PORT:-5004}"

echo "Starting Smart-Museum Python servers…"
echo "  - camera index: $MUSEUM_CAMERA"
echo "  - YOLO_CONTEXT_MOCK: $YOLO_CONTEXT_MOCK"
echo "  - HAND_TRACK_PORT: $HAND_TRACK_PORT"
echo "  - Server mode: $(if [ "$USE_UNIFIED" = "1" ]; then echo "UNIFIED (single process)"; else echo "LEGACY (multiple processes)"; fi)"
echo ""

if [ "$USE_UNIFIED" = "1" ]; then
    # NEW: Unified server (single process with fault isolation)
    echo "Using Unified Server (recommended)"
    echo "All services run in one process with fault isolation"
    echo ""

    python python/server/unified_museum_server.py &
    UNIFIED_PID=$!

    echo "Running:"
    echo "  unified_museum_server.py pid=$UNIFIED_PID (ports 5000-5004)"
    echo ""
    echo "Now run the C# app from Visual Studio."
    echo "Press Ctrl+C to stop the unified server."
    echo ""

    cleanup() {
      echo ""
      echo "Stopping unified server…"
      kill "$UNIFIED_PID" 2>/dev/null || true
      wait "$UNIFIED_PID" 2>/dev/null || true
    }
    trap cleanup INT TERM EXIT

    wait
else
    # LEGACY: Multiple separate processes
    echo "Using Legacy Multi-Process Mode"
    echo "Consider setting USE_UNIFIED_SERVER=1 for better fault isolation"
    echo ""

    python python/server/museum_vision_server.py &
    VISION_PID=$!

    python python/server/hand_tracker_service.py &
    HAND_PID=$!

    echo "Running:"
    echo "  museum_vision_server.py pid=$VISION_PID (5000/5001/5002/5003)"
    echo "  hand_tracker_service.py pid=$HAND_PID (HAND_TRACK_PORT=$HAND_TRACK_PORT)"
    echo ""
    echo "Now run the C# app from Visual Studio."
    echo "Press Ctrl+C to stop all servers."

    cleanup() {
      echo ""
      echo "Stopping servers…"
      kill "$HAND_PID" 2>/dev/null || true
      kill "$VISION_PID" 2>/dev/null || true
      wait "$HAND_PID" 2>/dev/null || true
      wait "$VISION_PID" 2>/dev/null || true
    }
    trap cleanup INT TERM EXIT

    wait
fi

