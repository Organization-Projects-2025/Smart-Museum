"""
demographics_service.py — DeepFace-based demographic analysis.

Analyses a face image and returns estimated age, gender, and race.
Used by the auth service during new-user registration.
"""

import os
import traceback
from typing import Optional, Tuple

# Lazy-load DeepFace to avoid heavy import at server startup
_deepface = None


def _load_deepface():
    global _deepface
    if _deepface is None:
        from deepface import DeepFace
        _deepface = DeepFace
    return _deepface


def warmup():
    """Pre-download DeepFace models at startup so sign-up is instant.
    Call this once from main.py during server boot."""
    try:
        print("[Demographics] Warming up DeepFace models (first-time download may take a minute)...")
        df = _load_deepface()
        # Run a dummy analysis on a tiny image to trigger model downloads.
        # DeepFace caches them in ~/.deepface/weights/
        import numpy as np
        dummy = np.zeros((100, 100, 3), dtype=np.uint8)
        dummy[30:70, 30:70] = 200  # light square to give it something
        try:
            df.analyze(dummy, actions=["age", "gender", "race"],
                       enforce_detection=False, silent=True)
        except Exception:
            pass  # analysis may fail on dummy image, but models are now cached
        print("[Demographics] Models ready.")
    except Exception as e:
        print(f"[Demographics] Warmup failed (will retry on first use): {e}")


def analyze(image_path: str) -> Tuple[int, str, str]:
    """
    Run DeepFace analysis on a saved face image.

    Returns:
        (age, gender, race) where:
            age    – integer estimate
            gender – "male" or "female"
            race   – one of: asian, indian, black, white,
                     middle eastern, latino hispanic
    Raises:
        RuntimeError if analysis fails.
    """
    if not os.path.isfile(image_path):
        raise RuntimeError(f"Image not found: {image_path}")

    try:
        df = _load_deepface()
        results = df.analyze(
            img_path=image_path,
            actions=["age", "gender", "race"],
            enforce_detection=False,
            silent=True,
        )

        # DeepFace returns a list; take the first result
        result = results[0] if isinstance(results, list) else results

        age = int(result.get("age", 25))
        dominant_gender = (result.get("dominant_gender") or "Man").strip()
        dominant_race = (result.get("dominant_race") or "white").strip()

        # Normalise gender to lowercase male/female
        gender = _normalise_gender(dominant_gender)

        # Normalise race to match CSV conventions
        race = _normalise_race(dominant_race)

        print(f"[Demographics] {os.path.basename(image_path)}: "
              f"age={age}, gender={gender}, race={race}")
        return age, gender, race

    except Exception as e:
        traceback.print_exc()
        raise RuntimeError(f"DeepFace analysis failed: {e}")


def _normalise_gender(raw: str) -> str:
    """Map DeepFace gender labels to CSV values."""
    g = raw.lower().strip()
    if g in ("woman", "female"):
        return "female"
    return "male"


def _normalise_race(raw: str) -> str:
    """Map DeepFace race labels to CSV values used by the C# theme system."""
    r = raw.lower().strip()
    # DeepFace returns: asian, indian, black, white,
    #                   middle eastern, latino hispanic
    mapping = {
        "asian":           "asian",
        "indian":          "indian",
        "black":           "black",
        "white":           "white",
        "middle eastern":  "middle_eastern",
        "latino hispanic": "latino",
    }
    return mapping.get(r, "white")
