using System;
using System.Collections.Generic;

/// <summary>
/// Manages input prioritization between TUIO markers, object tracking, and hand gesture recognition.
/// Priority hierarchy:
/// 1. Object Detection (highest) — when object visible, hand gestures are disabled
/// 2. TUIO Markers — when TUIOs detected, hand gestures are disabled
/// 3. Hand Gestures (lowest) — only active when no objects and no TUIOs
/// 
/// After objects/TUIOs are removed, a 5-second cooldown is enforced before gestures resume.
/// </summary>
public class InputPrioritizer
{
    private bool tuioPresent = false;
    private bool objectPresent = false;
    private DateTime tuioClearedTime = DateTime.MinValue;
    private DateTime objectClearedTime = DateTime.MinValue;
    private const int CooldownMs = 5000; // 5 seconds

    /// <summary>
    /// Returns true if an object is currently visible on screen.
    /// Used by the no-object gesture timer to decide whether to activate hand gestures.
    /// </summary>
    public bool IsObjectPresent => objectPresent;

    /// <summary>
    /// Returns true if hand gestures should be accepted.
    /// False if object is visible, any TUIO is present, OR within cooldown period after clearing.
    /// </summary>
    public bool CanAcceptGestures
    {
        get
        {
            // Priority 1: Objects block gestures
            if (objectPresent)
                return false;

            // Priority 2: TUIOs block gestures
            if (tuioPresent)
                return false;

            // Check object cooldown: if objects were recently cleared, still block gestures
            if (objectClearedTime != DateTime.MinValue)
            {
                int elapsedMs = (int)(DateTime.UtcNow - objectClearedTime).TotalMilliseconds;
                if (elapsedMs < CooldownMs)
                    return false;
            }

            // Check TUIO cooldown: if TUIOs were recently cleared, still block gestures
            if (tuioClearedTime != DateTime.MinValue)
            {
                int elapsedMs = (int)(DateTime.UtcNow - tuioClearedTime).TotalMilliseconds;
                if (elapsedMs < CooldownMs)
                    return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Returns the time remaining (in ms) until gestures can be accepted.
    /// 0 if gestures can be accepted now.
    /// </summary>
    public int GetCooldownRemainingMs()
    {
        // Objects have priority
        if (objectPresent)
            return CooldownMs;

        if (tuioPresent)
            return CooldownMs;

        int maxRemaining = 0;

        // Check object cooldown
        if (objectClearedTime != DateTime.MinValue)
        {
            int elapsedMs = (int)(DateTime.UtcNow - objectClearedTime).TotalMilliseconds;
            int remaining = CooldownMs - elapsedMs;
            if (remaining > 0)
                maxRemaining = Math.Max(maxRemaining, remaining);
        }

        // Check TUIO cooldown
        if (tuioClearedTime != DateTime.MinValue)
        {
            int elapsedMs = (int)(DateTime.UtcNow - tuioClearedTime).TotalMilliseconds;
            int remaining = CooldownMs - elapsedMs;
            if (remaining > 0)
                maxRemaining = Math.Max(maxRemaining, remaining);
        }

        return maxRemaining;
    }

    /// <summary>
    /// Updates the TUIO presence state. Call this when TUIOs are added/removed.
    /// </summary>
    public void SetTuioPresent(bool present)
    {
        bool wasPresent = tuioPresent;
        tuioPresent = present;

        // Transition from present to not present: start cooldown
        if (wasPresent && !present)
        {
            tuioClearedTime = DateTime.UtcNow;
            Console.WriteLine($"[InputPrioritizer] TUIOs cleared. Starting 5s cooldown...");
        }

        // Transition from not present to present: reset cooldown
        if (!wasPresent && present)
        {
            tuioClearedTime = DateTime.MinValue;
            Console.WriteLine($"[InputPrioritizer] TUIO detected. Gesture recognition blocked.");
        }
    }

    /// <summary>
    /// Updates the object detection presence state. Call this when objects are detected/disappear.
    /// Objects have priority over hand gestures.
    /// </summary>
    public void SetObjectPresent(bool present)
    {
        bool wasPresent = objectPresent;
        objectPresent = present;

        // Transition from present to not present: start cooldown
        if (wasPresent && !present)
        {
            objectClearedTime = DateTime.UtcNow;
            Console.WriteLine($"[InputPrioritizer] Object cleared. Starting 5s cooldown...");
        }

        // Transition from not present to present: reset cooldown
        if (!wasPresent && present)
        {
            objectClearedTime = DateTime.MinValue;
            Console.WriteLine($"[InputPrioritizer] Object detected. Hand gestures blocked.");
        }
    }

    /// <summary>
    /// Returns current state for debugging.
    /// </summary>
    public string GetDebugInfo()
    {
        string tuioState = tuioPresent ? "TUIO_PRESENT" : "TUIO_CLEAR";
        string objectState = objectPresent ? "OBJECT_PRESENT" : "OBJECT_CLEAR";
        int cooldownRemaining = GetCooldownRemainingMs();
        return $"[InputPrioritizer] TuioState={tuioState}, ObjectState={objectState}, GesturesAllowed={CanAcceptGestures}, CooldownMs={cooldownRemaining}";
    }

    /// <summary>
    /// Reset to initial state (no TUIOs, no cooldown).
    /// </summary>
    public void Reset()
    {
        tuioPresent = false;
        tuioClearedTime = DateTime.MinValue;
    }
}
