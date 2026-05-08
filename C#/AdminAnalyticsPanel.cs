using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

/// <summary>
/// Full-screen 3-level analytics dashboard navigated via TUIO marker (rotate + flick).
///
/// Level 0 — USER LIST (vertical rows, paginated, web-style):
///   Rotate marker CW  = scroll DOWN (next row).
///   Rotate marker CCW = scroll UP (prev row).
///   Flick RIGHT = select highlighted user → Level 1.
///   Flick DOWN  = close panel.
///
/// Level 1 — VISIT LIST (vertical rows, paginated):
///   Rotate = scroll rows.
///   Flick RIGHT = open highlighted visit → Level 2 (replay).
///   Flick LEFT  = back to Level 0.
///
/// Level 2 — REPLAY (full-screen slide + gaze dot + emotion overlay):
///   Rotate = scrub timeline / select segment.
///   Flick UP   = toggle auto-replay.
///   Flick LEFT = back to Level 1.
/// </summary>
public class AdminAnalyticsPanel
{
    // ─── dependencies ────────────────────────────────────────────────────────
    private readonly SessionAnalyticsRecorder recorder;
    private readonly Func<string> analyticsDirFactory;

    // ─── navigation state ────────────────────────────────────────────────────
    private int level = 0;   // 0 = user list, 1 = visit list, 2 = replay

    // Level 0 – user list
    private const int RowsPerPage = 8;
    private List<UserSummary> allUsers = new List<UserSummary>();
    private int userSelectedIndex = 0;   // absolute index into allUsers

    // Level 1 – visit list
    private const int VisitsPerPage = 8;
    private List<string> userVisitPaths = new List<string>();
    private int visitSelectedIndex = 0;  // absolute index into userVisitPaths

    // Level 2 – replay
    private VisitAnalyticsDocument loadedVisit;
    private string loadedPath;
    private int selectedSegmentIndex = 0;
    private long replayCursorMs = 0;
    private bool autoReplay = false;

    // ─── TUIO gesture state ──────────────────────────────────────────────────
    private bool gestureArmed = true;
    private bool hasLastPos = false;
    private float lastX = 0.5f;
    private float lastY = 0.5f;
    private float accumX = 0f;
    private float accumY = 0f;
    private const float TriggerD    = 0.035f;
    private const float NeutralBand = 0.015f;

    // Rotation → row scrolling
    private float lastAngleRad = 0f;
    private bool  hasLastAngle = false;
    private const float AngleScrollStep = 0.20f; // radians per row step

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
        hasLastPos = false;
        hasLastAngle = false;
        accumX = accumY = 0f;
        gestureArmed = true;
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
        long dur = seg.Samples[seg.Samples.Count - 1].TRelMs;
        replayCursorMs += deltaMs * 2;
        if (replayCursorMs > dur)
        {
            // Advance to next segment automatically
            int nextSeg = selectedSegmentIndex + 1;
            if (loadedVisit.Segments != null && nextSeg < loadedVisit.Segments.Count)
            {
                selectedSegmentIndex = nextSeg;
                replayCursorMs = 0;
            }
            else
            {
                // Reached end of all segments — stop playback
                replayCursorMs = dur;
                autoReplay = false;
            }
        }
    }

    // ─── OnMarker (primary entry point from TuioDemo) ────────────────────────
    public void OnMarker(bool hasMarker, float angleRad, float xNorm, float yNorm, out bool requestCloseAppPanel)
    {
        requestCloseAppPanel = false;
        if (!IsActive) return;

        if (!hasMarker)
        {
            lastTuioAngleValid = false;
            gestureArmed = true;
            accumX = accumY = 0f;
            hasLastPos = false;
            hasLastAngle = false;
            return;
        }

        lastTuioAngleValid = true;
        lastTuioAngleDeg = angleRad / (float)Math.PI * 180f;

        // ── Rotation → scroll rows (levels 0 and 1) or scrub replay (level 2) ─
        if (level < 2)
        {
            if (hasLastAngle)
            {
                float delta = NormalizeAngle(angleRad - lastAngleRad);
                if (delta > AngleScrollStep)
                {
                    ScrollDown();
                    lastAngleRad = angleRad;
                }
                else if (delta < -AngleScrollStep)
                {
                    ScrollUp();
                    lastAngleRad = angleRad;
                }
            }
            else
            {
                hasLastAngle = true;
                lastAngleRad = angleRad;
            }
        }
        else
        {
            HandleRotationReplay(angleRad);
        }

        // ── XY flick detection ────────────────────────────────────────────────
        if (!hasLastPos)
        {
            hasLastPos = true;
            lastX = xNorm;
            lastY = yNorm;
            accumX = accumY = 0f;
            return;
        }

        accumX += xNorm - lastX;
        accumY += yNorm - lastY;

        if (Math.Abs(xNorm - 0.5f) <= NeutralBand && Math.Abs(yNorm - 0.5f) <= NeutralBand)
        {
            gestureArmed = true;
            accumX = accumY = 0f;
        }

        if (gestureArmed)
        {
            float absX = Math.Abs(accumX);
            float absY = Math.Abs(accumY);

            if (absX >= TriggerD && absX > absY)
            {
                gestureArmed = false;
                bool right = accumX > 0;
                accumX = accumY = 0f;
                if (right) HandleFlickRight(out requestCloseAppPanel);
                else       HandleFlickLeft(out requestCloseAppPanel);
            }
            else if (absY >= TriggerD && absY > absX)
            {
                gestureArmed = false;
                bool up = accumY < 0;
                accumX = accumY = 0f;
                if (up) HandleFlickUp(out requestCloseAppPanel);
                else    HandleFlickDown(out requestCloseAppPanel);
            }
        }

        lastX = xNorm;
        lastY = yNorm;
    }

    // Backward-compat overload for callers that don't pass xNorm
    public void OnMarker(bool hasMarker, float angleRad, float yNorm, out bool requestCloseAppPanel)
    {
        OnMarker(hasMarker, angleRad, 0.5f, yNorm, out requestCloseAppPanel);
    }

    // ─── Draw ────────────────────────────────────────────────────────────────
    public void Draw(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        if (!IsActive) return;

        using (var bg = new SolidBrush(Color.FromArgb(245, 14, 16, 22)))
            g.FillRectangle(bg, 0, 0, w, h);

        switch (level)
        {
            case 0: DrawUserList(g, w, h, fontTitle, fontBody, fontSmall, accent, papyrus); break;
            case 1: DrawVisitList(g, w, h, fontTitle, fontBody, fontSmall, accent, papyrus); break;
            case 2: DrawReplay(g, w, h, fontTitle, fontBody, fontSmall, accent, papyrus, live); break;
        }

        // Border
        using (var p = new Pen(Color.FromArgb(120, accent), 2))
            g.DrawRectangle(p, 10, 10, w - 20, h - 20);

        // Marker hint at very bottom
        string rotLine = lastTuioAngleValid
            ? string.Format("Marker {0}  angle {1:0}°  |  rotate=scroll  flick right=select  flick left=back",
                TuioControlMarker.MenuAuthSymbolId, lastTuioAngleDeg)
            : string.Format("Place marker {0} on the table to navigate", TuioControlMarker.MenuAuthSymbolId);
        DrawCentered(g, rotLine, fontSmall, Color.FromArgb(140, 200, 200, 210),
            new RectangleF(20, h - 22, w - 40, 18));
    }

    // ─── Navigation helpers ───────────────────────────────────────────────────

    private void GoToLevel0()
    {
        level = 0;
        userSelectedIndex = 0;
        loadedVisit = null;
        autoReplay = false;
        RefreshUserList();
    }

    private void GoToLevel1(UserSummary user)
    {
        level = 1;
        visitSelectedIndex = 0;
        string dir = analyticsDirFactory != null ? analyticsDirFactory() : "";
        userVisitPaths = SessionAnalyticsRecorder.ListSessionFiles(dir)
            .Where(p => Path.GetFileName(p).StartsWith(
                "visit_" + user.FaceUserId + "_", StringComparison.OrdinalIgnoreCase))
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
        var dict = new Dictionary<string, UserSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string fn = Path.GetFileNameWithoutExtension(f);
            string[] parts = fn.Split('_');
            if (parts.Length < 2) continue;
            string uid = parts[1];
            if (!dict.ContainsKey(uid))
            {
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
        userSelectedIndex = 0;
    }

    private void ScrollUp()
    {
        if (level == 0)
        {
            if (allUsers.Count == 0) return;
            userSelectedIndex = (userSelectedIndex - 1 + allUsers.Count) % allUsers.Count;
        }
        else if (level == 1)
        {
            if (userVisitPaths.Count == 0) return;
            visitSelectedIndex = (visitSelectedIndex - 1 + userVisitPaths.Count) % userVisitPaths.Count;
        }
    }

    private void ScrollDown()
    {
        if (level == 0)
        {
            if (allUsers.Count == 0) return;
            userSelectedIndex = (userSelectedIndex + 1) % allUsers.Count;
        }
        else if (level == 1)
        {
            if (userVisitPaths.Count == 0) return;
            visitSelectedIndex = (visitSelectedIndex + 1) % userVisitPaths.Count;
        }
    }

    private void HandleRotationReplay(float angleRad)
    {
        if (loadedVisit == null || loadedVisit.Segments == null || loadedVisit.Segments.Count == 0) return;
        if (!hasLastAngle) { hasLastAngle = true; lastAngleRad = angleRad; return; }
        float delta = NormalizeAngle(angleRad - lastAngleRad);
        if (delta > AngleScrollStep)
        {
            selectedSegmentIndex = Math.Min(selectedSegmentIndex + 1, loadedVisit.Segments.Count - 1);
            replayCursorMs = 0;
            lastAngleRad = angleRad;
        }
        else if (delta < -AngleScrollStep)
        {
            selectedSegmentIndex = Math.Max(selectedSegmentIndex - 1, 0);
            replayCursorMs = 0;
            lastAngleRad = angleRad;
        }
    }

    private void HandleFlickRight(out bool requestClose)
    {
        requestClose = false;
        switch (level)
        {
            case 0:
                if (userSelectedIndex < allUsers.Count)
                    GoToLevel1(allUsers[userSelectedIndex]);
                break;
            case 1:
                if (visitSelectedIndex < userVisitPaths.Count)
                    GoToLevel2(userVisitPaths[visitSelectedIndex]);
                break;
        }
    }

    private void HandleFlickLeft(out bool requestClose)
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

    private void HandleFlickUp(out bool requestClose)
    {
        requestClose = false;
        if (level == 2) autoReplay = !autoReplay;
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

    private static float NormalizeAngle(float a)
    {
        while (a >  (float)Math.PI) a -= 2f * (float)Math.PI;
        while (a < -(float)Math.PI) a += 2f * (float)Math.PI;
        return a;
    }

    // ─── Draw: Level 0 — User List (vertical web-style rows) ─────────────────

    private void DrawUserList(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus)
    {
        // Header
        DrawCentered(g, "ADMIN ANALYTICS  —  USER LIST",
            fontTitle, accent, new RectangleF(20, 14, w - 40, 48));
        DrawCentered(g,
            "Rotate = scroll   Flick RIGHT = view visits   Flick DOWN = close",
            fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 62, w - 40, 20));

        if (allUsers.Count == 0)
        {
            DrawCentered(g, "No analytics sessions found. Run a slideshow first.",
                fontBody, Color.Gray, new RectangleF(40, h / 2f - 20, w - 80, 40));
            return;
        }

        // Pagination
        int pageCount = (int)Math.Ceiling(allUsers.Count / (double)RowsPerPage);
        int page      = userSelectedIndex / RowsPerPage;
        int startIdx  = page * RowsPerPage;
        int endIdx    = Math.Min(startIdx + RowsPerPage, allUsers.Count);

        if (pageCount > 1)
            DrawCentered(g, string.Format("Page {0} / {1}", page + 1, pageCount),
                fontSmall, Color.FromArgb(160, papyrus), new RectangleF(20, 82, w - 40, 18));

        // Row list
        int rowH   = 54;
        int startY = 108;
        int padX   = 40;

        for (int i = startIdx; i < endIdx; i++)
        {
            bool sel = i == userSelectedIndex;
            var u    = allUsers[i];
            int  ry  = startY + (i - startIdx) * (rowH + 6);
            var  row = new Rectangle(padX, ry, w - padX * 2, rowH);

            // Row background
            using (var br = new SolidBrush(sel
                ? Color.FromArgb(230, accent)
                : Color.FromArgb(130, 30, 34, 44)))
                g.FillRectangle(br, row);

            // Row border
            using (var pen = new Pen(sel
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(60, accent), sel ? 2f : 1f))
                g.DrawRectangle(pen, row);

            // Selection arrow
            if (sel)
            {
                using (var arrowBr = new SolidBrush(Color.Black))
                {
                    var pts = new PointF[]
                    {
                        new PointF(row.X + 10, ry + rowH / 2f - 7),
                        new PointF(row.X + 10, ry + rowH / 2f + 7),
                        new PointF(row.X + 22, ry + rowH / 2f)
                    };
                    g.FillPolygon(arrowBr, pts);
                }
            }

            // Text
            var textColor = sel ? Color.Black : Color.White;
            using (var tbr = new SolidBrush(textColor))
            {
                var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                var sfR = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center };
                var nameRect  = new RectangleF(row.X + 30, row.Y, row.Width * 0.55f, row.Height);
                var idRect    = new RectangleF(row.X + 30, row.Y + row.Height * 0.5f, row.Width * 0.55f, row.Height * 0.5f);
                var countRect = new RectangleF(row.X, row.Y, row.Width - 12, row.Height);

                g.DrawString(u.DisplayName, fontBody, tbr, nameRect, sfL);
                using (var dimBr = new SolidBrush(sel ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(160, papyrus)))
                    g.DrawString(u.FaceUserId, fontSmall, dimBr, idRect, sfL);
                g.DrawString(u.VisitCount + (u.VisitCount == 1 ? " visit" : " visits"), fontSmall, tbr, countRect, sfR);
            }
        }

        // Scroll arrows
        if (page > 0)
            DrawCentered(g, "▲ more above", fontSmall, Color.FromArgb(160, papyrus),
                new RectangleF(20, startY - 18, w - 40, 16));
        if (endIdx < allUsers.Count)
            DrawCentered(g, "▼ more below", fontSmall, Color.FromArgb(160, papyrus),
                new RectangleF(20, startY + (endIdx - startIdx) * (rowH + 6) + 2, w - 40, 16));
    }

    // ─── Draw: Level 1 — Visit List (vertical rows) ───────────────────────────

    private void DrawVisitList(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus)
    {
        string userName = (userSelectedIndex < allUsers.Count)
            ? allUsers[userSelectedIndex].DisplayName
            : "Unknown";

        DrawCentered(g, "VISITS  —  " + userName.ToUpper(),
            fontTitle, accent, new RectangleF(20, 14, w - 40, 48));
        DrawCentered(g,
            "Rotate = scroll   Flick RIGHT = open replay   Flick LEFT = back to users",
            fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 62, w - 40, 20));

        if (userVisitPaths.Count == 0)
        {
            DrawCentered(g, "No visits found for this user.",
                fontBody, Color.Gray, new RectangleF(40, h / 2f - 20, w - 80, 40));
            return;
        }

        int pageCount = (int)Math.Ceiling(userVisitPaths.Count / (double)VisitsPerPage);
        int page      = visitSelectedIndex / VisitsPerPage;
        int startIdx  = page * VisitsPerPage;
        int endIdx    = Math.Min(startIdx + VisitsPerPage, userVisitPaths.Count);

        if (pageCount > 1)
            DrawCentered(g, string.Format("Page {0} / {1}", page + 1, pageCount),
                fontSmall, Color.FromArgb(160, papyrus), new RectangleF(20, 82, w - 40, 18));

        int rowH   = 60;
        int startY = 108;
        int padX   = 40;

        for (int i = startIdx; i < endIdx; i++)
        {
            bool sel = i == visitSelectedIndex;
            int  ry  = startY + (i - startIdx) * (rowH + 6);
            var  row = new Rectangle(padX, ry, w - padX * 2, rowH);

            using (var br = new SolidBrush(sel
                ? Color.FromArgb(230, accent)
                : Color.FromArgb(130, 30, 34, 44)))
                g.FillRectangle(br, row);
            using (var pen = new Pen(sel
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(60, accent), sel ? 2f : 1f))
                g.DrawRectangle(pen, row);

            if (sel)
            {
                using (var arrowBr = new SolidBrush(Color.Black))
                {
                    var pts = new PointF[]
                    {
                        new PointF(row.X + 10, ry + rowH / 2f - 7),
                        new PointF(row.X + 10, ry + rowH / 2f + 7),
                        new PointF(row.X + 22, ry + rowH / 2f)
                    };
                    g.FillPolygon(arrowBr, pts);
                }
            }

            // Parse filename: visit_userX_hash_YYYYMMDD_HHmmss
            string fn    = Path.GetFileNameWithoutExtension(userVisitPaths[i]);
            string[] pts2 = fn.Split('_');
            string dateStr = "";
            if (pts2.Length >= 5)
            {
                string d = pts2[3]; // YYYYMMDD
                string t = pts2[4]; // HHmmss
                if (d.Length == 8 && t.Length == 6)
                    dateStr = d.Substring(0, 4) + "-" + d.Substring(4, 2) + "-" + d.Substring(6, 2)
                            + "  " + t.Substring(0, 2) + ":" + t.Substring(2, 2) + ":" + t.Substring(4, 2);
            }

            int segCount = 0;
            try { segCount = SessionAnalyticsRecorder.Load(userVisitPaths[i])?.Segments?.Count ?? 0; } catch { }

            var textColor = sel ? Color.Black : Color.White;
            using (var tbr = new SolidBrush(textColor))
            {
                var sfL = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                var sfR = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center };
                g.DrawString(dateStr, fontBody, tbr,
                    new RectangleF(row.X + 30, row.Y, row.Width * 0.7f, row.Height * 0.55f), sfL);
                using (var dimBr = new SolidBrush(sel ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(160, papyrus)))
                    g.DrawString(segCount + (segCount == 1 ? " slide segment" : " slide segments"),
                        fontSmall, dimBr,
                        new RectangleF(row.X + 30, row.Y + row.Height * 0.5f, row.Width * 0.7f, row.Height * 0.5f), sfL);
                g.DrawString("#" + (i + 1), fontSmall, tbr,
                    new RectangleF(row.X, row.Y, row.Width - 12, row.Height), sfR);
            }
        }

        if (page > 0)
            DrawCentered(g, "▲ more above", fontSmall, Color.FromArgb(160, papyrus),
                new RectangleF(20, startY - 18, w - 40, 16));
        if (endIdx < userVisitPaths.Count)
            DrawCentered(g, "▼ more below", fontSmall, Color.FromArgb(160, papyrus),
                new RectangleF(20, startY + (endIdx - startIdx) * (rowH + 6) + 2, w - 40, 16));
    }

    // ─── Draw: Level 2 — Replay ───────────────────────────────────────────────

    private void DrawReplay(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        var seg = GetSelectedSegment();
        int totalSegs = loadedVisit?.Segments?.Count ?? 0;

        // ── Header bar ────────────────────────────────────────────────────────
        string userName = loadedVisit?.DisplayName ?? "Unknown";
        string segLabel = totalSegs > 0
            ? string.Format("Slide {0}/{1}", selectedSegmentIndex + 1, totalSegs)
            : "";
        string storyLabel = seg != null ? seg.StoryTitle : "";
        string headerLine = userName + "   ·   " + segLabel
            + (string.IsNullOrEmpty(storyLabel) ? "" : "   ·   " + storyLabel);
        DrawCentered(g, headerLine, fontBody, accent, new RectangleF(20, 10, w - 40, 36));

        // ── Controls hint ─────────────────────────────────────────────────────
        string playState = autoReplay ? "▶ PLAYING" : "⏸ PAUSED";
        string hint = playState + "   |   Flick UP = play/pause   Rotate = jump segment   Flick LEFT = back";
        DrawCentered(g, hint, fontSmall, Color.FromArgb(190, papyrus), new RectangleF(20, 46, w - 40, 20));

        var slideArea = new Rectangle(0, 68, w, h - 100);

        if (seg == null)
        {
            using (var bg = new SolidBrush(Color.FromArgb(255, 10, 8, 25)))
                g.FillRectangle(bg, slideArea);
            DrawCentered(g, "No segments in this visit.", fontBody, Color.Gray,
                new RectangleF(0, h / 2f - 20, w, 40));
            return;
        }

        // 1. Slide background (text or image — exactly as user saw it)
        DrawSegmentSlide(g, seg, slideArea, accent);

        if (seg.Samples != null && seg.Samples.Count > 0)
        {
            if (!autoReplay)
            {
                long maxT = seg.Samples[seg.Samples.Count - 1].TRelMs;
                replayCursorMs = Math.Min(replayCursorMs, maxT);
            }

            var atSample = seg.Samples.LastOrDefault(s => s.TRelMs <= replayCursorMs) ?? seg.Samples[0];

            // 2. Gaze trail
            DrawGazeTrail(g, slideArea, seg.Samples, replayCursorMs);

            // 3. Gaze dot (identical to live overlay)
            double nx = Math.Max(0.0, Math.Min(1.0, atSample.Gx));
            double ny = Math.Max(0.0, Math.Min(1.0, atSample.Gy));
            int px = slideArea.X + (int)(nx * slideArea.Width);
            int py = slideArea.Y + (int)(ny * slideArea.Height);
            const int outerR = 14;
            using (var ring = new Pen(Color.FromArgb(240, 255, 255, 255), 3f))
                g.DrawEllipse(ring, px - outerR, py - outerR, outerR * 2, outerR * 2);
            using (var fill = new SolidBrush(Color.FromArgb(230, 255, 60, 60)))
                g.FillEllipse(fill, px - 5, py - 5, 10, 10);
            using (var cross = new Pen(Color.FromArgb(200, 255, 255, 255), 1.5f))
            {
                g.DrawLine(cross, px - 22, py, px + 22, py);
                g.DrawLine(cross, px, py - 22, px, py + 22);
            }

            // 4. Dominant emotion label — top-left of slide, pill style
            string emotionLine = "😐 Dominant: " + FormatEmotion(atSample.Dominant);
            using (var labelFont = new Font("Georgia", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF textSz = g.MeasureString(emotionLine, labelFont);
                var pill = new RectangleF(slideArea.X + 10, slideArea.Y + 10,
                    textSz.Width + 24, textSz.Height + 10);
                using (var pillBg = new SolidBrush(Color.FromArgb(210, 12, 14, 20)))
                    g.FillRectangle(pillBg, pill);
                using (var pillPen = new Pen(Color.FromArgb(120, accent), 1.5f))
                    g.DrawRectangle(pillPen, pill.X, pill.Y, pill.Width, pill.Height);
                using (var textBr = new SolidBrush(Color.FromArgb(255, 255, 248, 200)))
                    g.DrawString(emotionLine, labelFont, textBr,
                        new PointF(pill.X + 12, pill.Y + 5));
            }

            // 5. Emotion probability bars — right side, tall panel
            int barPanelW = 220;
            int barPanelH = Math.Min(280, slideArea.Height - 60);
            var emoRect = new Rectangle(slideArea.Right - barPanelW - 8,
                slideArea.Top + 8, barPanelW, barPanelH);
            DrawEmotionBars(g, emoRect, atSample.Dominant, atSample.Emotions, fontSmall, accent);

            // 6. Timeline progress bar + time label
            long totalMs = seg.Samples[seg.Samples.Count - 1].TRelMs;
            int barY = h - 36;
            var barRect = new Rectangle(60, barY, w - 120, 10);
            using (var bgBr = new SolidBrush(Color.FromArgb(120, 40, 40, 50)))
                g.FillRectangle(bgBr, barRect);
            if (totalMs > 0)
            {
                int progW = (int)(barRect.Width * Math.Min(1.0, replayCursorMs / (double)totalMs));
                using (var progBr = new SolidBrush(Color.FromArgb(220, accent)))
                    g.FillRectangle(progBr, barRect.X, barRect.Y, progW, barRect.Height);
            }
            // Time labels
            using (var timeBr = new SolidBrush(Color.FromArgb(200, papyrus)))
            {
                var sfL = new StringFormat { Alignment = StringAlignment.Far,  LineAlignment = StringAlignment.Center };
                var sfR = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                g.DrawString(string.Format("{0:0.0}s", replayCursorMs / 1000.0),
                    fontSmall, timeBr, new RectangleF(0, barY - 2, 56, 14), sfL);
                g.DrawString(string.Format("{0:0.0}s", totalMs / 1000.0),
                    fontSmall, timeBr, new RectangleF(barRect.Right + 4, barY - 2, 56, 14), sfR);
            }
        }
        else
        {
            DrawCentered(g, "No gaze samples recorded for this segment.",
                fontBody, Color.FromArgb(180, papyrus),
                new RectangleF(slideArea.X, slideArea.Y + slideArea.Height / 2f - 20, slideArea.Width, 40));
        }

        // 7. Segment selector strip at bottom
        DrawSegmentStrip(g, w, h, accent, papyrus, fontSmall);
    }

    // ─── Segment strip (bottom of replay) ────────────────────────────────────

    private void DrawSegmentStrip(Graphics g, int w, int h, Color accent, Color papyrus, Font fontSmall)
    {
        if (loadedVisit == null || loadedVisit.Segments == null || loadedVisit.Segments.Count <= 1) return;

        int n       = loadedVisit.Segments.Count;
        int stripH  = 22;
        int stripY  = h - stripH - 2;
        int slotW   = Math.Min(120, (w - 20) / n);
        int totalW  = slotW * n;
        int startX  = (w - totalW) / 2;

        for (int i = 0; i < n; i++)
        {
            bool sel = i == selectedSegmentIndex;
            var slot = new Rectangle(startX + i * slotW, stripY, slotW - 2, stripH);

            using (var br = new SolidBrush(sel
                ? Color.FromArgb(200, accent)
                : Color.FromArgb(80, 40, 44, 54)))
                g.FillRectangle(br, slot);

            string label = (i + 1).ToString();
            using (var tb = new SolidBrush(sel ? Color.Black : Color.FromArgb(160, papyrus)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, fontSmall, tb, new RectangleF(slot.X, slot.Y, slot.Width, slot.Height), sf);
            }
        }
    }

    // ─── Slide content renderer ───────────────────────────────────────────────

    private void DrawSegmentSlide(Graphics g, AnalyticsSegmentDoc seg, Rectangle area, Color accent)
    {
        using (var bg = new SolidBrush(Color.FromArgb(255, 10, 8, 25)))
            g.FillRectangle(bg, area);

        if (string.IsNullOrEmpty(seg.ContentSummary))
        {
            DrawCentered(g, seg.StoryTitle + "  ·  slide " + seg.SlideIndex,
                new Font("Georgia", 22f, FontStyle.Regular, GraphicsUnit.Pixel),
                Color.FromArgb(200, 240, 220, 165),
                new RectangleF(area.X, area.Y + area.Height / 2f - 20, area.Width, 40));
            return;
        }

        int colon = seg.ContentSummary.IndexOf(':');
        string typeStr = colon > 0 ? seg.ContentSummary.Substring(0, colon) : seg.ContentSummary;
        string content = colon > 0 ? seg.ContentSummary.Substring(colon + 1) : "";

        if (typeStr == "Image" && !string.IsNullOrEmpty(content))
            DrawReplayImageSlide(g, content, area);
        else if (typeStr == "Text" && !string.IsNullOrEmpty(content))
            DrawReplayTextSlide(g, content, area, accent);
        else
            DrawCentered(g, seg.StoryTitle + "  ·  slide " + seg.SlideIndex,
                new Font("Georgia", 22f, FontStyle.Regular, GraphicsUnit.Pixel),
                Color.FromArgb(200, 240, 220, 165),
                new RectangleF(area.X, area.Y + area.Height / 2f - 20, area.Width, 40));
    }

    private static readonly Dictionary<string, Image> _imgCache =
        new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

    private static Image TryLoadImage(string path)
    {
        if (_imgCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            string full = path;
            if (!Path.IsPathRooted(full))
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 6; i++)
                {
                    string candidate = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(candidate)) { full = candidate; break; }
                    dir = Path.GetDirectoryName(dir) ?? dir;
                }
            }
            if (File.Exists(full))
            {
                var img = Image.FromFile(full);
                _imgCache[path] = img;
                return img;
            }
        }
        catch { }
        return null;
    }

    private static Rectangle FitRect(int imgW, int imgH, Rectangle area)
    {
        float scale = Math.Min((float)area.Width / imgW, (float)area.Height / imgH);
        int dw = (int)(imgW * scale);
        int dh = (int)(imgH * scale);
        return new Rectangle(area.X + (area.Width - dw) / 2, area.Y + (area.Height - dh) / 2, dw, dh);
    }

    private static void DrawReplayImageSlide(Graphics g, string path, Rectangle area)
    {
        Image img = TryLoadImage(path);
        if (img != null)
        {
            Rectangle dest = FitRect(img.Width, img.Height, area);
            g.DrawImage(img, dest);
        }
        else
        {
            using (var pen = new Pen(Color.FromArgb(90, 70, 20), 2))
                g.DrawRectangle(pen, area);
            DrawCentered(g, "[ Image: " + Path.GetFileName(path) + " ]",
                new Font("Georgia", 18f, FontStyle.Italic, GraphicsUnit.Pixel),
                Color.FromArgb(160, 240, 220, 165),
                new RectangleF(area.X, area.Y + area.Height / 2f - 18, area.Width, 36));
        }
    }

    private static void DrawReplayTextSlide(Graphics g, string text, Rectangle area, Color accent)
    {
        int padX  = Math.Min(60, area.Width / 8);
        var panel = new Rectangle(
            area.X + padX, area.Y + area.Height / 8,
            area.Width - padX * 2, area.Height * 6 / 8);

        using (var panelBr = new SolidBrush(Color.FromArgb(200, 20, 16, 35)))
            g.FillRectangle(panelBr, panel);
        using (var panelPen = new Pen(Color.FromArgb(100, accent), 1.5f))
            g.DrawRectangle(panelPen, panel);

        using (var textBr = new SolidBrush(Color.FromArgb(240, 240, 220, 165)))
        using (var tf = new Font("Georgia", 20f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming      = StringTrimming.Word
            };
            g.DrawString(text, tf, textBr,
                new RectangleF(panel.X + 20, panel.Y + 20, panel.Width - 40, panel.Height - 40), sf);
        }
    }

    // ─── Gaze trail ───────────────────────────────────────────────────────────

    private static void DrawGazeTrail(Graphics g, Rectangle area,
        List<AnalyticsSampleDoc> samples, long cursorMs)
    {
        const int TrailMs   = 2000;
        const int MaxRadius = 6;

        long trailStart = cursorMs - TrailMs;
        var  inWindow   = samples.Where(s => s.TRelMs >= trailStart && s.TRelMs <= cursorMs).ToList();
        if (inWindow.Count < 2) return;

        for (int i = 1; i < inWindow.Count; i++)
        {
            var prev = inWindow[i - 1];
            var curr = inWindow[i];
            float age  = 1f - (float)(cursorMs - curr.TRelMs) / TrailMs;
            int   alpha = (int)(age * 180);
            if (alpha <= 0) continue;

            int x1 = area.X + (int)(Math.Max(0, Math.Min(1, prev.Gx)) * area.Width);
            int y1 = area.Y + (int)(Math.Max(0, Math.Min(1, prev.Gy)) * area.Height);
            int x2 = area.X + (int)(Math.Max(0, Math.Min(1, curr.Gx)) * area.Width);
            int y2 = area.Y + (int)(Math.Max(0, Math.Min(1, curr.Gy)) * area.Height);

            using (var pen = new Pen(Color.FromArgb(alpha, 255, 200, 60), 2f))
                g.DrawLine(pen, x1, y1, x2, y2);

            int r = (int)(age * MaxRadius);
            if (r > 0)
                using (var dotBr = new SolidBrush(Color.FromArgb(alpha / 2, 255, 200, 60)))
                    g.FillEllipse(dotBr, x2 - r, y2 - r, r * 2, r * 2);
        }
    }

    // ─── Emotion bars ─────────────────────────────────────────────────────────

    private static void DrawEmotionBars(Graphics g, Rectangle area,
        string dominant, Dictionary<string, double> emotions, Font fontSmall, Color accent)
    {
        if (emotions == null || emotions.Count == 0) return;

        // Semi-transparent panel background
        using (var bgBr = new SolidBrush(Color.FromArgb(200, 10, 12, 20)))
            g.FillRectangle(bgBr, area);
        using (var borderPen = new Pen(Color.FromArgb(80, accent), 1f))
            g.DrawRectangle(borderPen, area);

        var sorted = emotions.OrderByDescending(kv => kv.Value).ToList();

        // Layout: label col (70px) | bar | pct (38px)
        const int labelW = 70;
        const int pctW   = 38;
        const int padX   = 8;
        const int padY   = 8;
        int barAreaW = area.Width - labelW - pctW - padX * 2;
        int barH     = Math.Max(10, (area.Height - padY * 2 - 4) / Math.Max(1, sorted.Count) - 5);

        int y = area.Y + padY;

        using (var labelFont = new Font("Georgia", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var pctFont   = new Font("Georgia", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            foreach (var kv in sorted)
            {
                if (y + barH > area.Bottom - padY) break;

                bool isDom = string.Equals(kv.Key, dominant, StringComparison.OrdinalIgnoreCase);
                int  barW  = Math.Max(2, (int)(kv.Value * barAreaW));

                // Label (right-aligned in label column)
                using (var labelBr = new SolidBrush(isDom ? Color.White : Color.FromArgb(200, 200, 200, 200)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString(FormatEmotion(kv.Key), labelFont, labelBr,
                        new RectangleF(area.X + padX, y, labelW - 4, barH), sf);
                }

                // Bar
                int barX = area.X + padX + labelW;
                using (var barBr = new SolidBrush(isDom
                    ? Color.FromArgb(230, accent)
                    : Color.FromArgb(110, 100, 140, 180)))
                    g.FillRectangle(barBr, barX, y + 2, barW, barH - 4);

                // Dominant glow outline
                if (isDom)
                    using (var glowPen = new Pen(Color.FromArgb(180, accent), 1.5f))
                        g.DrawRectangle(glowPen, barX, y + 2, barAreaW, barH - 4);

                // Percentage
                using (var pctBr = new SolidBrush(isDom ? Color.White : Color.FromArgb(180, 200, 200, 200)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
                    g.DrawString(string.Format("{0:0}%", kv.Value * 100), pctFont, pctBr,
                        new RectangleF(barX + barAreaW + 4, y, pctW, barH), sf);
                }

                y += barH + 5;
            }
        }
    }

    // ─── Utility ─────────────────────────────────────────────────────────────

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF rect)
    {
        using (var br = new SolidBrush(color))
        {
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming      = StringTrimming.EllipsisCharacter
            };
            g.DrawString(text, font, br, rect, sf);
        }
    }

    private static string FormatEmotion(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "—";
        return char.ToUpper(raw[0]) + raw.Substring(1).ToLower();
    }
}

// ─── Supporting types ─────────────────────────────────────────────────────────

public class UserSummary
{
    public string FaceUserId;
    public string DisplayName;
    public int    VisitCount;
}
