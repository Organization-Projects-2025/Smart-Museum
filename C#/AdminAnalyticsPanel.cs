using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

/// <summary>
/// Full-screen 3-level analytics dashboard navigated via TUIO marker (rotate + vertical flick).
///
/// Level 0 — USER LIST:
///   Shows all unique users parsed from visit_*.json files. Paginated, 6 users per page.
///   Rotate = scroll through users on current page (wraps to next/prev page at boundaries).
///   Flick UP  = select user → go to Level 1.
///   Flick DOWN = close panel (requestCloseAppPanel = true).
///
/// Level 1 — SLIDESHOW LIST:
///   Shows all visit files for the selected user, newest first. Paginated, 5 per page.
///   Rotate = scroll. Flick UP = open visit → go to Level 2. Flick DOWN = back to Level 0.
///
/// Level 2 — REPLAY:
///   Shows gaze trail + emotion bars for the selected visit.
///   Rotate = scrub timeline through segments. Flick UP = toggle auto-replay. Flick DOWN = back to Level 1.
/// </summary>
public class AdminAnalyticsPanel
{
    // ─── dependencies ────────────────────────────────────────────────────────
    private readonly SessionAnalyticsRecorder recorder;
    private readonly Func<string> analyticsDirFactory;

    // ─── navigation state ────────────────────────────────────────────────────
    private int level = 0;   // 0 = user list, 1 = slideshow list, 2 = replay

    // Level 0 – user list
    private const int UsersPerPage = 6;
    private List<UserSummary> allUsers = new List<UserSummary>();
    private int userPage = 0;
    private int userIndexOnPage = 0;   // 0..UsersPerPage-1

    // Level 1 – slideshow list
    private const int VisitsPerPage = 5;
    private List<string> userVisitPaths = new List<string>();
    private int visitPage = 0;
    private int visitIndexOnPage = 0;  // 0..VisitsPerPage-1

    // Level 2 – replay
    private VisitAnalyticsDocument loadedVisit;
    private string loadedPath;
    private int selectedSegmentIndex = 0;
    private long replayCursorMs = 0;
    private bool autoReplay = false;

    // ─── TUIO gesture state ──────────────────────────────────────────────────
    private bool menuGestureArmed = true;
    private bool hasLastY = false;
    private float lastY = 0f;
    private float accumY = 0f;
    private const float TriggerDy   = 0.035f;
    private const float NeutralBand = 0.015f;
    // yNorm increases downward on screen; a flick UP means yNorm decreases → delta is negative
    private const bool MenuUpIsPositiveY = true;   // kept for parity with original

    private float lastTuioAngleDeg = 0f;
    private bool  lastTuioAngleValid = false;

    // ─── public API ──────────────────────────────────────────────────────────
    public bool IsActive { get; private set; }

    public AdminAnalyticsPanel(SessionAnalyticsRecorder recorder, Func<string> analyticsDirFactory)
    {
        this.recorder = recorder;
        this.analyticsDirFactory = analyticsDirFactory;
    }

    public void Enter()
    {
        IsActive = true;
        lastTuioAngleValid = false;
        hasLastY = false;
        accumY = 0f;
        menuGestureArmed = true;
        GoToLevel0();
    }

    public void Exit()
    {
        IsActive = false;
        loadedVisit = null;
        autoReplay = false;
    }

    public void Tick(int deltaMs)
    {
        if (!IsActive || level != 2 || !autoReplay || loadedVisit == null) return;
        var seg = GetSelectedSegment();
        if (seg == null || seg.Samples == null || seg.Samples.Count == 0) return;
        long dur = seg.Samples[seg.Samples.Count - 1].TRelMs + 1;
        replayCursorMs += deltaMs * 2;
        if (replayCursorMs > dur) replayCursorMs = 0;
    }

    public void OnMarker(bool hasMarker, float angleRad, float yNorm, out bool requestCloseAppPanel)
    {
        requestCloseAppPanel = false;
        if (!IsActive) return;

        if (!hasMarker)
        {
            lastTuioAngleValid = false;
            menuGestureArmed = true;
            accumY = 0f;
            hasLastY = false;
            return;
        }

        lastTuioAngleValid = true;
        lastTuioAngleDeg = angleRad / (float)Math.PI * 180f;

        // ── rotation → item selection ────────────────────────────────────────
        HandleRotation(angleRad);

        // ── vertical flick detection ─────────────────────────────────────────
        if (!hasLastY)
        {
            hasLastY = true;
            lastY = yNorm;
            accumY = 0f;
            return;
        }

        accumY += yNorm - lastY;

        if (Math.Abs(yNorm - 0.5f) <= NeutralBand)
        {
            menuGestureArmed = true;
            accumY = 0f;
        }

        // upDelta > 0 means the marker moved upward (yNorm decreased)
        float upDelta   = MenuUpIsPositiveY ? (-accumY) : accumY;
        float downDelta = -upDelta;

        if (menuGestureArmed && upDelta >= TriggerDy)
        {
            menuGestureArmed = false;
            accumY = 0f;
            HandleFlickUp(out requestCloseAppPanel);
        }
        else if (menuGestureArmed && downDelta >= TriggerDy)
        {
            menuGestureArmed = false;
            accumY = 0f;
            HandleFlickDown(out requestCloseAppPanel);
        }

        lastY = yNorm;
    }

    // ─── Draw ────────────────────────────────────────────────────────────────
    public void Draw(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        if (!IsActive) return;

        // dark background
        using (var bg = new SolidBrush(Color.FromArgb(245, 14, 16, 22)))
            g.FillRectangle(bg, 0, 0, w, h);

        // title bar
        string levelLabel = level == 0 ? "USER LIST" : level == 1 ? "VISIT LIST" : "REPLAY";
        DrawCentered(g,
            "ADMIN ANALYTICS  [" + levelLabel + "]  — marker symbol " + TuioControlMarker.MenuAuthSymbolId,
            fontTitle, accent, new RectangleF(20, 10, w - 40, 44));

        // level-specific content
        switch (level)
        {
            case 0: DrawUserList(g, w, h, fontBody, fontSmall, accent, papyrus); break;
            case 1: DrawVisitList(g, w, h, fontBody, fontSmall, accent, papyrus); break;
            case 2: DrawReplay(g, w, h, fontTitle, fontBody, fontSmall, accent, papyrus, live); break;
        }

        // border
        using (var p = new Pen(Color.FromArgb(120, accent), 2))
            g.DrawRectangle(p, 10, 10, w - 20, h - 20);

        // angle readout
        string rotLine = lastTuioAngleValid
            ? string.Format("TUIO marker angle: {0:0}°", lastTuioAngleDeg)
            : "TUIO marker angle: — (place symbol " + TuioControlMarker.MenuAuthSymbolId + " on the table)";
        DrawCentered(g, rotLine, fontSmall, Color.FromArgb(200, 200, 200, 210),
            new RectangleF(20, h - 28, w - 40, 22));
    }

