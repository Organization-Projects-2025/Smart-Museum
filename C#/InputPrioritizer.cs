using System;
using System.Collections.Generic;

/// <summary>
/// Manages input prioritization between TUIO markers and hand gesture recognition.
/// When TUIOs are detected, gesture recognition is disabled to avoid conflicts.
/// After all TUIOs are removed, a 5-second cooldown is enforced before gestures resume.
/// </summary>
public class InputPrioritizer
{
    private bool tuioPresent = false;
    private DateTime tuioClearedTime = DateTime.MinValue;
    private const int CooldownMs = 5000; // 5 seconds

    /// <summary>
    /// Returns true if gestures should be accepted.
    /// False if any TUIO is present OR within cooldown period after TUIOs cleared.
    /// </summary>
    public bool CanAcceptGestures
    {
        get
        {
            if (tuioPresent)
                return false;

            // Check cooldown: if TUIOs were recently cleared, still block gestures
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
        if (tuioPresent)
            return CooldownMs; // Treat as full cooldown while TUIOs present

        if (tuioClearedTime != DateTime.MinValue)
        {
            int elapsedMs = (int)(DateTime.UtcNow - tuioClearedTime).TotalMilliseconds;
            int remaining = CooldownMs - elapsedMs;
            return remaining > 0 ? remaining : 0;
        }

        return 0;
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
    /// Returns current state for debugging.
    /// </summary>
    public string GetDebugInfo()
    {
        string state = tuioPresent ? "TUIO_PRESENT" : "TUIO_CLEAR";
        int cooldownRemaining = GetCooldownRemainingMs();
        return $"[InputPrioritizer] State={state}, GesturesAllowed={CanAcceptGestures}, CooldownMs={cooldownRemaining}";
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
