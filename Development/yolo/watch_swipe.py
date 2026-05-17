"""
Watch-centric swipe detection from YOLO bounding-box motion.
Threshold scales with watch size: larger box → less movement required.
Handles brief detection gaps: compares last seen position to reappearance.
"""

from __future__ import annotations

import time

# Normalized displacement when watch area ≈ REF_AREA_FRAC (lower = easier swipes)
BASE_TRIGGER = 0.055
REF_AREA_FRAC = 0.07
MIN_AREA_FRAC = 0.008
MAX_AREA_FRAC = 0.55
TRIGGER_MIN = 0.022
TRIGGER_MAX = 0.16
AXIS_DOMINANCE = 1.05
GAP_MAX_SEC = 0.65
GAP_TRIGGER_FACTOR = 0.82

SWIPE_LABELS = {
    "left": "← SWIPE LEFT",
    "right": "→ SWIPE RIGHT",
    "up": "↑ SWIPE UP",
    "down": "↓ SWIPE DOWN",
}


def trigger_distance(box, frame_w: int, frame_h: int) -> float:
    x1, y1, x2, y2 = box
    area_frac = ((x2 - x1) * (y2 - y1)) / max(frame_w * frame_h, 1)
    area_frac = max(MIN_AREA_FRAC, min(MAX_AREA_FRAC, area_frac))
    scale = (REF_AREA_FRAC / area_frac) ** 0.5
    d = BASE_TRIGGER * scale
    return max(TRIGGER_MIN, min(TRIGGER_MAX, d))


def box_centroid_norm(box, frame_w: int, frame_h: int) -> tuple[float, float]:
    x1, y1, x2, y2 = box
    return (x1 + x2) / 2.0 / frame_w, (y1 + y2) / 2.0 / frame_h


class WatchSwipeDetector:
    """Accumulates centroid motion; fires swipes on continuous move or gap reappear."""

    def __init__(self, cooldown_sec: float = 0.42):
        self.cooldown_sec = cooldown_sec
        self._armed = True
        self._accum_x = 0.0
        self._accum_y = 0.0
        self._last_cx: float | None = None
        self._last_cy: float | None = None
        self._last_box = None
        self._anchor_cx: float | None = None
        self._anchor_cy: float | None = None
        self._cooldown_until = 0.0
        self._lost = False
        self._ghost_cx: float | None = None
        self._ghost_cy: float | None = None
        self._ghost_box = None
        self._ghost_at = 0.0
        self.last_swipe: str | None = None
        self.last_swipe_at = 0.0
        self.last_trigger = BASE_TRIGGER

    def reset(self):
        self._armed = True
        self._accum_x = self._accum_y = 0.0
        self._last_cx = self._last_cy = None
        self._last_box = None
        self._anchor_cx = self._anchor_cy = None
        self._lost = False
        self._clear_ghost()

    def _clear_ghost(self):
        self._ghost_cx = self._ghost_cy = None
        self._ghost_box = None
        self._ghost_at = 0.0

    def _classify_delta(self, dx: float, dy: float, trigger: float) -> str | None:
        if not self._armed:
            return None
        abs_x, abs_y = abs(dx), abs(dy)
        if abs_x >= trigger and abs_x > abs_y * AXIS_DOMINANCE:
            return "right" if dx > 0 else "left"
        if abs_y >= trigger and abs_y > abs_x * AXIS_DOMINANCE:
            return "up" if dy < 0 else "down"
        return None

    def _commit_swipe(self, swipe: str, now: float, cx: float, cy: float, box) -> str:
        self._armed = False
        self._accum_x = self._accum_y = 0.0
        self._cooldown_until = now + self.cooldown_sec
        self.last_swipe = swipe
        self.last_swipe_at = now
        self._last_cx, self._last_cy = cx, cy
        self._last_box = box
        self._anchor_cx, self._anchor_cy = cx, cy
        self._lost = False
        self._clear_ghost()
        return swipe

    def _note_miss(self, now: float) -> None:
        if self._last_cx is not None:
            self._ghost_cx, self._ghost_cy = self._last_cx, self._last_cy
            self._ghost_box = self._last_box
            self._ghost_at = now
        self._lost = True
        self._last_cx = self._last_cy = None
        if self._ghost_at and (now - self._ghost_at) > GAP_MAX_SEC:
            self._clear_ghost()
            self._lost = False

    def update(self, box, frame_w: int, frame_h: int) -> str | None:
        now = time.perf_counter()
        if now < self._cooldown_until:
            if box is None:
                self._note_miss(now)
            return None

        if box is None:
            self._note_miss(now)
            return None

        cx, cy = box_centroid_norm(box, frame_w, frame_h)
        trigger = trigger_distance(box, frame_w, frame_h)
        self.last_trigger = trigger
        neutral = trigger * 0.4

        # Sudden disappearance → reappear: teleport counts as swipe
        if self._lost and self._ghost_cx is not None and (now - self._ghost_at) <= GAP_MAX_SEC:
            dx = cx - self._ghost_cx
            dy = cy - self._ghost_cy
            ref_box = self._ghost_box if self._ghost_box is not None else box
            gap_trigger = trigger_distance(ref_box, frame_w, frame_h) * GAP_TRIGGER_FACTOR
            gap_swipe = self._classify_delta(dx, dy, gap_trigger)
            self._lost = False
            self._clear_ghost()
            if gap_swipe is not None:
                return self._commit_swipe(gap_swipe, now, cx, cy, box)

        self._lost = False

        if self._last_cx is None:
            self._last_cx, self._last_cy = cx, cy
            self._last_box = box
            self._anchor_cx, self._anchor_cy = cx, cy
            return None

        self._accum_x += cx - self._last_cx
        self._accum_y += cy - self._last_cy
        self._last_cx, self._last_cy = cx, cy
        self._last_box = box

        if self._anchor_cx is not None:
            if abs(cx - self._anchor_cx) < neutral and abs(cy - self._anchor_cy) < neutral:
                self._armed = True
                self._accum_x = self._accum_y = 0.0

        swipe = self._classify_delta(self._accum_x, self._accum_y, trigger)
        if swipe is None:
            return None

        return self._commit_swipe(swipe, now, cx, cy, box)

    def swipe_visible(self, display_sec: float = 1.6) -> bool:
        if self.last_swipe is None:
            return False
        return (time.perf_counter() - self.last_swipe_at) < display_sec
