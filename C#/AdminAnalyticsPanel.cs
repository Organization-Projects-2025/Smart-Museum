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


    // ─── Navigation helpers ───────────────────────────────────────────────────

    private void GoToLevel0()
    {
        level = 0;
        userPage = 0;
        userIndexOnPage = 0;
        loadedVisit = null;
        autoReplay = false;
        RefreshUserList();
    }

    private void GoToLevel1(UserSummary user)
    {
        level = 1;
        visitPage = 0;
        visitIndexOnPage = 0;
        string dir = analyticsDirFactory != null ? analyticsDirFactory() : "";
        userVisitPaths = SessionAnalyticsRecorder.ListSessionFiles(dir)
            .Where(p => Path.GetFileName(p).StartsWith("visit_" + user.FaceUserId + "_",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void GoToLevel2(string path)
    {
        try
        {
            loadedVisit = SessionAnalyticsRecorder.Load(path);
            loadedPath = path;
            level = 2;
            selectedSegmentIndex = 0;
            replayCursorMs = 0;
            autoReplay = false;
        }
        catch { /* bad file — stay on level 1 */ }
    }

    private void RefreshUserList()
    {
        string dir = analyticsDirFactory != null ? analyticsDirFactory() : "";
        var files = SessionAnalyticsRecorder.ListSessionFiles(dir);
        // Group by faceUserId, keep latest visit per user
        var dict = new Dictionary<string, UserSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string fn = Path.GetFileNameWithoutExtension(f); // visit_userX_hash_date
            string[] parts = fn.Split('_');
            if (parts.Length < 2) continue;
            // faceUserId is parts[1] (e.g. "user0")
            string uid = parts[1];
            if (!dict.ContainsKey(uid))
            {
                // Try to read display name from file
                string displayName = uid;
                try
                {
                    var doc = SessionAnalyticsRecorder.Load(f);
                    if (doc != null && !string.IsNullOrEmpty(doc.DisplayName))
                        displayName = doc.DisplayName;
                }
                catch { }
                dict[uid] = new UserSummary { FaceUserId = uid, DisplayName = displayName, VisitCount = 0 };
            }
            dict[uid].VisitCount++;
        }
        allUsers = dict.Values.OrderBy(u => u.FaceUserId).ToList();
    }

    private void HandleRotation(float angleRad)
    {
        switch (level)
        {
            case 0:
            {
                int pageCount = allUsers.Count > 0 ? (int)Math.Ceiling(allUsers.Count / (double)UsersPerPage) : 1;
                int onPage = Math.Min(UsersPerPage, allUsers.Count - userPage * UsersPerPage);
                if (onPage <= 0) break;
                float fromTop = NormalizeAngle(angleRad + (float)Math.PI / 2f);
                float step = (float)(Math.PI * 2.0 / onPage);
                int idx = (int)(fromTop / step);
                userIndexOnPage = Math.Max(0, Math.Min(onPage - 1, idx));
                break;
            }
            case 1:
            {
                int onPage = Math.Min(VisitsPerPage, userVisitPaths.Count - visitPage * VisitsPerPage);
                if (onPage <= 0) break;
                float fromTop = NormalizeAngle(angleRad + (float)Math.PI / 2f);
                float step = (float)(Math.PI * 2.0 / onPage);
                int idx = (int)(fromTop / step);
                visitIndexOnPage = Math.Max(0, Math.Min(onPage - 1, idx));
                break;
            }
            case 2:
            {
                if (loadedVisit == null || loadedVisit.Segments == null || loadedVisit.Segments.Count == 0) break;
                int n = loadedVisit.Segments.Count;
                float fromTop = NormalizeAngle(angleRad + (float)Math.PI / 2f);
                float step = (float)(Math.PI * 2.0 / n);
                int idx = (int)(fromTop / step);
                selectedSegmentIndex = Math.Max(0, Math.Min(n - 1, idx));
                replayCursorMs = 0;
                break;
            }
        }
    }

    private void HandleFlickUp(out bool requestClose)
    {
        requestClose = false;
        switch (level)
        {
            case 0:
            {
                int globalIdx = userPage * UsersPerPage + userIndexOnPage;
                if (globalIdx < allUsers.Count)
                    GoToLevel1(allUsers[globalIdx]);
                break;
            }
            case 1:
            {
                int globalIdx = visitPage * VisitsPerPage + visitIndexOnPage;
                if (globalIdx < userVisitPaths.Count)
                    GoToLevel2(userVisitPaths[globalIdx]);
                break;
            }
            case 2:
                autoReplay = !autoReplay;
                break;
        }
    }

    private void HandleFlickDown(out bool requestClose)
    {
        requestClose = false;
        switch (level)
        {
            case 0:
                requestClose = true;
                break;
            case 1:
                GoToLevel0();
                break;
            case 2:
                level = 1;
                loadedVisit = null;
                autoReplay = false;
                break;
        }
    }

    private AnalyticsSegmentDoc GetSelectedSegment()
    {
        if (loadedVisit == null || loadedVisit.Segments == null || loadedVisit.Segments.Count == 0)
            return null;
        return loadedVisit.Segments[Math.Min(selectedSegmentIndex, loadedVisit.Segments.Count - 1)];
    }

    // ─── Draw: Level 0 — User List ────────────────────────────────────────────

    private void DrawUserList(Graphics g, int w, int h, Font fontBody, Font fontSmall, Color accent, Color papyrus)
    {
        DrawCentered(g, "Rotate = select user   Flick UP = view visits   Flick DOWN = close",
            fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 52, w - 40, 22));

        if (allUsers.Count == 0)
        {
            DrawCentered(g, "No analytics sessions found. Run a slideshow first.",
                fontBody, Color.Gray, new RectangleF(40, h / 2f - 30, w - 80, 60));
            return;
        }

        int pageCount = (int)Math.Ceiling(allUsers.Count / (double)UsersPerPage);
        int startIdx  = userPage * UsersPerPage;
        int endIdx    = Math.Min(startIdx + UsersPerPage, allUsers.Count);
        var pageUsers = allUsers.Skip(startIdx).Take(endIdx - startIdx).ToList();

        // Pagination indicator
        if (pageCount > 1)
            DrawCentered(g, string.Format("Page {0}/{1}  (use next/prev page via flick)", userPage + 1, pageCount),
                fontSmall, Color.FromArgb(180, papyrus), new RectangleF(20, 74, w - 40, 20));

        // Draw user cards in a grid
        int cols = 3, rows = 2;
        int cardW = (w - 80) / cols;
        int cardH = 120;
        int startY = 110;

        for (int i = 0; i < pageUsers.Count; i++)
        {
            int col = i % cols;
            int row = i / cols;
            int cx = 40 + col * cardW + cardW / 2;
            int cy = startY + row * (cardH + 20) + cardH / 2;
            bool sel = i == userIndexOnPage;

            var cardRect = new Rectangle(40 + col * cardW, startY + row * (cardH + 20), cardW - 10, cardH);
            using (var br = new SolidBrush(sel ? Color.FromArgb(220, accent) : Color.FromArgb(160, 35, 38, 48)))
                g.FillRectangle(br, cardRect);
            using (var pen = new Pen(sel ? Color.White : Color.FromArgb(80, accent), sel ? 2f : 1f))
                g.DrawRectangle(pen, cardRect);

            var u = pageUsers[i];
            using (var tbr = new SolidBrush(sel ? Color.Black : Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(u.DisplayName, fontBody, tbr,
                    new RectangleF(cardRect.X + 4, cardRect.Y + 10, cardRect.Width - 8, 40), sf);
                g.DrawString(u.FaceUserId, fontSmall, tbr,
                    new RectangleF(cardRect.X + 4, cardRect.Y + 52, cardRect.Width - 8, 24), sf);
                g.DrawString(u.VisitCount + " visit" + (u.VisitCount != 1 ? "s" : ""), fontSmall, tbr,
                    new RectangleF(cardRect.X + 4, cardRect.Y + 76, cardRect.Width - 8, 24), sf);
            }
        }

        // Page navigation hint
        if (pageCount > 1)
        {
            DrawCentered(g, "◄ prev page: rotate past first item   next page: rotate past last item ►",
                fontSmall, Color.FromArgb(150, papyrus), new RectangleF(20, h - 60, w - 40, 22));
        }
    }

    // ─── Draw: Level 1 — Visit List ───────────────────────────────────────────

    private void DrawVisitList(Graphics g, int w, int h, Font fontBody, Font fontSmall, Color accent, Color papyrus)
    {
        string userName = (userPage * UsersPerPage + userIndexOnPage < allUsers.Count)
            ? allUsers[userPage * UsersPerPage + userIndexOnPage].DisplayName
            : "Unknown";

        DrawCentered(g, "Visits for: " + userName,
            fontBody, accent, new RectangleF(20, 52, w - 40, 28));
        DrawCentered(g, "Rotate = select   Flick UP = open   Flick DOWN = back to users",
            fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 78, w - 40, 20));

        if (userVisitPaths.Count == 0)
        {
            DrawCentered(g, "No visits found for this user.",
                fontBody, Color.Gray, new RectangleF(40, h / 2f - 20, w - 80, 40));
            return;
        }

        int pageCount = (int)Math.Ceiling(userVisitPaths.Count / (double)VisitsPerPage);
        int startIdx  = visitPage * VisitsPerPage;
        var pageVisits = userVisitPaths.Skip(startIdx).Take(VisitsPerPage).ToList();

        if (pageCount > 1)
            DrawCentered(g, string.Format("Page {0}/{1}", visitPage + 1, pageCount),
                fontSmall, Color.FromArgb(180, papyrus), new RectangleF(20, 98, w - 40, 18));

        int itemH = 70;
        int startY = 125;

        for (int i = 0; i < pageVisits.Count; i++)
        {
            bool sel = i == visitIndexOnPage;
            var row = new Rectangle(40, startY + i * (itemH + 8), w - 80, itemH);

            using (var br = new SolidBrush(sel ? Color.FromArgb(220, accent) : Color.FromArgb(140, 35, 38, 48)))
                g.FillRectangle(br, row);
            using (var pen = new Pen(sel ? Color.White : Color.FromArgb(60, accent), sel ? 2f : 1f))
                g.DrawRectangle(pen, row);

            string fn = Path.GetFileNameWithoutExtension(pageVisits[i]);
            // Parse date from filename: visit_userX_hash_YYYYMMDD_HHmmss
            string dateStr = "";
            string[] parts = fn.Split('_');
            if (parts.Length >= 5)
                dateStr = parts[3] + " " + parts[4].Insert(2, ":").Insert(5, ":");

            // Try to get segment count
            int segCount = 0;
            try { segCount = SessionAnalyticsRecorder.Load(pageVisits[i])?.Segments?.Count ?? 0; } catch { }

            using (var tbr = new SolidBrush(sel ? Color.Black : Color.White))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(dateStr, fontBody, tbr, new RectangleF(row.X + 12, row.Y + 4, row.Width - 24, 32), sf);
                g.DrawString(segCount + " slide segment" + (segCount != 1 ? "s" : ""), fontSmall, tbr,
                    new RectangleF(row.X + 12, row.Y + 36, row.Width - 24, 24), sf);
            }
        }
    }

    // ─── Draw: Level 2 — Replay ───────────────────────────────────────────────

    private void DrawReplay(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        string title = loadedVisit != null
            ? loadedVisit.DisplayName + "  ·  " + Path.GetFileNameWithoutExtension(loadedPath ?? "")
            : "LIVE";
        DrawCentered(g, title, fontTitle, accent, new RectangleF(20, 52, w - 40, 36));

        string hint = autoReplay ? "▶ AUTO-REPLAY  (flick UP = pause)" : "⏸ PAUSED  (flick UP = play)";
        hint += "   Rotate = select segment   Flick DOWN = back";
        DrawCentered(g, hint, fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 88, w - 40, 20));

        // Gaze area
        int emoW = 210;
        var gazeRect = new Rectangle(40, 116, w - 100 - emoW, h - 220);
        using (var br = new SolidBrush(Color.FromArgb(200, 8, 9, 14)))
            g.FillRectangle(br, gazeRect);
        using (var pen = new Pen(Color.FromArgb(150, accent), 1))
            g.DrawRectangle(pen, gazeRect);

        // Emotion bars on the right
        var emoRect = new Rectangle(gazeRect.Right + 10, gazeRect.Top, emoW, gazeRect.Height);

        var seg = GetSelectedSegment();
        if (seg != null && seg.Samples != null && seg.Samples.Count > 0)
        {
            string segLabel = seg.StoryTitle + "  ·  slide " + seg.SlideIndex;
            DrawCentered(g, segLabel, fontBody, papyrus,
                new RectangleF(gazeRect.X, gazeRect.Top - 26, gazeRect.Width, 22));

            if (!autoReplay)
            {
                long maxT = seg.Samples[seg.Samples.Count - 1].TRelMs;
                replayCursorMs = Math.Min(replayCursorMs, maxT);
            }

            DrawGazeTrail(g, gazeRect, seg.Samples, replayCursorMs);

            var atSample = seg.Samples.LastOrDefault(s => s.TRelMs <= replayCursorMs) ?? seg.Samples[0];
            DrawEmotionBars(g, emoRect, atSample.Dominant, atSample.Emotions, fontSmall, accent);

            // Timeline progress bar
            long totalMs = seg.Samples[seg.Samples.Count - 1].TRelMs;
            if (totalMs > 0)
            {
                var barRect = new Rectangle(gazeRect.X, gazeRect.Bottom + 8, gazeRect.Width, 12);
                using (var bgBr = new SolidBrush(Color.FromArgb(80, 40, 40, 50)))
                    g.FillRectangle(bgBr, barRect);
                int progW = (int)(barRect.Width * Math.Min(1.0, replayCursorMs / (double)totalMs));
                using (var progBr = new SolidBrush(Color.FromArgb(200, accent)))
                    g.FillRectangle(progBr, barRect.X, barRect.Y, progW, barRect.Height);
                DrawCentered(g,
                    string.Format("{0:0.0}s / {1:0.0}s", replayCursorMs / 1000.0, totalMs / 1000.0),
                    fontSmall, papyrus, new RectangleF(barRect.X, barRect.Bottom + 2, barRect.Width, 18));
            }
        }
        else
        {
            DrawCentered(g, "No samples in this segment.", fontBody, Color.Gray,
                new RectangleF(gazeRect.X, gazeRect.Y + gazeRect.Height / 2 - 20, gazeRect.Width, 40));
        }

        // Segment selector wheel at bottom
        if (loadedVisit != null && loadedVisit.Segments != null && loadedVisit.Segments.Count > 1)
        {
            int cx = w / 2;
            int cy = h - 70;
            int r  = 50;
            int n  = loadedVisit.Segments.Count;
            for (int i = 0; i < n; i++)
            {
                float a0 = (float)(-Math.PI / 2 + Math.PI * 2.0 * i / n);
                float a1 = (float)(-Math.PI / 2 + Math.PI * 2.0 * (i + 1) / n);
                bool sel = i == selectedSegmentIndex;
                using (var b = new SolidBrush(sel
                    ? Color.FromArgb(220, accent)
                    : Color.FromArgb(120, 35, 36, 42)))
                using (var gp = new GraphicsPath())
                {
                    gp.AddPie(cx - r, cy - r, r * 2, r * 2,
                        (float)(a0 * 180.0 / Math.PI),
                        (float)((a1 - a0) * 180.0 / Math.PI));
                    g.FillPath(b, gp);
                }
            }
            DrawCentered(g, "Rotate marker to select segment",
                fontSmall, papyrus, new RectangleF(20, h - 110, w - 40, 18));
        }
    }

    // ─── Gaze trail rendering ─────────────────────────────────────────────────

    private static void DrawGazeTrail(Graphics g, Rectangle zone, List<AnalyticsSampleDoc> samples, long cursorMs)
    {
        if (samples == null || samples.Count < 2) return;
        using (var pen = new Pen(Color.FromArgb(180, 100, 200, 255), 2) { LineJoin = LineJoin.Round })
        {
            for (int i = 1; i < samples.Count; i++)
            {
                var a = samples[i - 1];
                var b = samples[i];
                int x1 = zone.Left + (int)(a.Gx * zone.Width);
                int y1 = zone.Top  + (int)(a.Gy * zone.Height);
                int x2 = zone.Left + (int)(b.Gx * zone.Width);
                int y2 = zone.Top  + (int)(b.Gy * zone.Height);
                float t = b.TRelMs <= cursorMs ? 1f : 0.25f;
                pen.Color = Color.FromArgb((int)(50 + 205 * t), 100, 200, 255);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }
        var cur = samples.LastOrDefault(s => s.TRelMs <= cursorMs);
        if (cur != null)
        {
            int cx = zone.Left + (int)(cur.Gx * zone.Width);
            int cy = zone.Top  + (int)(cur.Gy * zone.Height);
            using (var br = new SolidBrush(Color.FromArgb(240, 255, 220, 60)))
                g.FillEllipse(br, cx - 9, cy - 9, 18, 18);
            using (var pen = new Pen(Color.White, 1.5f))
                g.DrawEllipse(pen, cx - 9, cy - 9, 18, 18);
        }
    }

    // ─── Emotion bars ─────────────────────────────────────────────────────────

    private static void DrawEmotionBars(Graphics g, Rectangle zone, string dominant,
        Dictionary<string, double> emotions, Font fontSmall, Color accent)
    {
        if (emotions == null || emotions.Count == 0) return;
        string[] order = { "angry", "disgust", "fear", "happy", "sad", "surprise", "neutral" };
        int n    = order.Length;
        int rowH = zone.Height / n;

        for (int i = 0; i < n; i++)
        {
            string key = order[i];
            double v = 0;
            emotions.TryGetValue(key, out v);
            v = Math.Max(0, Math.Min(1, v));

            var row = new Rectangle(zone.X, zone.Y + i * rowH, zone.Width, rowH - 3);
            using (var bg = new SolidBrush(Color.FromArgb(100, 28, 28, 36)))
                g.FillRectangle(bg, row);

            bool isDom = string.Equals(dominant, key, StringComparison.OrdinalIgnoreCase);
            int bw = (int)(row.Width * v);
            using (var fill = new SolidBrush(isDom
                ? Color.FromArgb(230, accent)
                : Color.FromArgb(150, 70, 110, 150)))
                g.FillRectangle(fill, row.X, row.Y + 3, bw, row.Height - 6);

            using (var tbr = new SolidBrush(isDom ? Color.Black : Color.White))
                g.DrawString(key + "  " + (v * 100).ToString("0") + "%",
                    fontSmall, tbr, row.X + 4, row.Y + 4);
        }

        // Dominant label
        DrawCentered(g, "▲ " + (dominant ?? "—"),
            fontSmall, accent,
            new RectangleF(zone.X, zone.Bottom + 4, zone.Width, 20));
    }

    // ─── Utilities ────────────────────────────────────────────────────────────

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sf = new StringFormat
        {
            Alignment     = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming      = StringTrimming.EllipsisCharacter
        };
        using (var br = new SolidBrush(color))
            g.DrawString(text, font, br, bounds, sf);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle < 0f)                       angle += (float)(Math.PI * 2.0);
        while (angle >= (float)(Math.PI * 2.0))  angle -= (float)(Math.PI * 2.0);
        return angle;
    }
}

// ─── Helper types ─────────────────────────────────────────────────────────────

public class UserSummary
{
    public string FaceUserId  { get; set; }
    public string DisplayName { get; set; }
    public int    VisitCount  { get; set; }
}
