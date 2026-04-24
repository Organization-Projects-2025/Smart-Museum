"""
Integration test for yolo_context_service.py (mock mode, ephemeral port).
Run the suite several times from the shell to stress the handshake + frame loop.

Example (PowerShell, repo root):
  for ($i = 1; $i -le 3; $i++) { python python/server/test_yolo_context_protocol.py }
"""

import json
import os
import socket
import subprocess
import sys
import time


def _run_once(port: int):
    env = os.environ.copy()
    env["YOLO_CONTEXT_MOCK"] = "1"
    env["YOLO_CONTEXT_PORT"] = str(port)
    env["YOLO_CONTEXT_HOST"] = "127.0.0.1"

    proc = subprocess.Popen(
        [sys.executable, os.path.join(os.path.dirname(__file__), "yolo_context_service.py")],
        env=env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    time.sleep(0.35)
    try:
        s = socket.create_connection(("127.0.0.1", port), timeout=3)
        try:
            s.settimeout(8.0)
            rfile = s.makefile("r", encoding="utf-8", newline="\n")
            wfile = s.makefile("w", encoding="utf-8", newline="\n")
            wfile.write("PING\n")
            wfile.flush()
            line = rfile.readline()
            assert line, "empty PING response"
            o = json.loads(line.strip())
            assert o.get("status") == "ok", o

            wfile.write("STREAM\n")
            wfile.flush()
            ack = rfile.readline()
            assert ack, "empty STREAM ack"
            o = json.loads(ack.strip())
            assert o.get("status") == "ok", o

            frames = []
            for _ in range(12):
                line = rfile.readline()
                if not line:
                    break
                fr = json.loads(line.strip())
                assert fr.get("ok") is True, fr
                assert "t_ms" in fr and "tracks" in fr
                assert isinstance(fr["tracks"], list)
                frames.append(fr)

            assert len(frames) >= 6, "expected several mock frames"

            wfile.write("PAUSE\n")
            wfile.flush()
            pause_ack = rfile.readline()
            assert pause_ack, "empty PAUSE ack"
            assert json.loads(pause_ack.strip()).get("status") == "ok"
            rfile.close()
            wfile.close()
        finally:
            try:
                s.close()
            except Exception:
                pass
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=3)
        except subprocess.TimeoutExpired:
            proc.kill()


def main():
    base = 18000 + (int(time.time()) % 1000)
    for i in range(3):
        _run_once(base + i)
        print("yolo_context protocol test pass", i + 1, "/ 3")
    print("all ok")


if __name__ == "__main__":
    main()
