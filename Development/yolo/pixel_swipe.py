"""
Pixel-based object swipe detection (from museum object-track script).
Compares displacement over a sliding window; scales threshold when object is smaller in frame.
"""

from __future__ import annotations

import time

SWIPE_LABELS = {
    "left": "← SWIPE LEFT",
    "right": "→ SWIPE RIGHT",
    "up": "↑ SWIPE UP",
    "down": "↓ SWIPE DOWN",
}

# C# gesture names on port 5005
C_SHARP_GESTURES = {
    "left": "objectswipeleft",
    "right": "objectswiperight",
    "up": "objectswipeup",
    "down": "objectswipedown",
}


class PixelSwipeTracker:
    SWIPE_WINDOW = 15
    BASE_THRESHOLD_PX = 55
    SWIPE_COOLDOWN_FRAMES = 12
    GAP_MAX_SEC = 0.55

    def __init__(self, min_conf: float = 0.1):
        self.min_conf = min_conf
        self._track: list[tuple[float, float]] = []
        self._cooldown = 0
        self._ghost: tuple[float, float] | None = None
        self._ghost_at = 0.0
        self.last_swipe: str | None = None
        self.last_swipe_at = 0.0
        self.last_threshold_px = self.BASE_THRESHOLD_PX

    def reset(self):
        self._track.clear()
        self._ghost = None

    def _threshold_px(self, box) -> float:
        x1, y1, x2, y2 = box
        size = max(x2 - x1, y2 - y1, 20)
        # smaller object → slightly more pixels required
        scale = max(0.55, min(1.4, 120.0 / size))
        self.last_threshold_px = self.BASE_THRESHOLD_PX * scale
        return self.last_threshold_px

    def _centroid(self, box) -> tuple[float, float]:
        x1, y1, x2, y2 = box
        return (x1 + x2) / 2.0, (y1 + y2) / 2.0

    def _classify(self, dx: float, dy: float, thresh: float) -> str | None:
        if abs(dx) > abs(dy) and abs(dx) > thresh:
            return "right" if dx > 0 else "left"
        if abs(dy) > abs(dx) and abs(dy) > thresh:
            return "down" if dy > 0 else "up"
        return None

    def _fire(self, swipe: str) -> str:
        self.last_swipe = swipe
        self.last_swipe_at = time.perf_counter()
        self._track.clear()
        self._cooldown = self.SWIPE_COOLDOWN_FRAMES
        self._ghost = None
        return swipe

    def update(self, box, conf: float) -> str | None:
        now = time.perf_counter()

        if self._cooldown > 0:
            self._cooldown -= 1

        if box is None:
            if self._track:
                self._ghost = self._track[-1]
                self._ghost_at = now
            return None

        if conf < self.min_conf:
            return None

        cx, cy = self._centroid(box)
        thresh = self._threshold_px(box)

        if self._ghost is not None and (now - self._ghost_at) <= self.GAP_MAX_SEC:
            dx = cx - self._ghost[0]
            dy = cy - self._ghost[1]
            self._ghost = None
            if self._cooldown == 0:
                gap_swipe = self._classify(dx, dy, thresh * 0.85)
                if gap_swipe:
                    return self._fire(gap_swipe)

        self._track.append((cx, cy))
        if len(self._track) > 60:
            self._track.pop(0)

        if len(self._track) < self.SWIPE_WINDOW or self._cooldown > 0:
            return None

        dx = self._track[-1][0] - self._track[-self.SWIPE_WINDOW][0]
        dy = self._track[-1][1] - self._track[-self.SWIPE_WINDOW][1]
        swipe = self._classify(dx, dy, thresh)
        if swipe:
            return self._fire(swipe)
        return None

    def swipe_visible(self, display_sec: float = 1.6) -> bool:
        return self.last_swipe is not None and (time.perf_counter() - self.last_swipe_at) < display_sec
