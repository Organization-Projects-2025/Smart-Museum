using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;

/// <summary>
/// Full-screen analytics dashboard navigated via TUIO marker (same patterns as circular menu).
/// </summary>
public class AdminAnalyticsPanel
{
    private readonly SessionAnalyticsRecorder recorder;
    private readonly Func<string> analyticsDirFactory;

    private List<string> sessionPaths = new List<string>();
    private VisitAnalyticsDocument loadedVisit;
    private string loadedPath;
    private bool detailMode;
    private int selectedListIndex;
    private int selectedSegmentIndex;
    private long replayCursorMs;
    private bool autoReplay;
    private bool menuGestureArmed = true;
    private bool hasLastY;
    private float lastY;
    private float accumY;
    private const float TriggerDy = 0.035f;
    private const float NeutralBand = 0.015f;
    private const bool MenuUpIsPositiveY = true;
    private float lastTuioAngleDeg;
    private bool lastTuioAngleValid;

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
        detailMode = false;
        selectedListIndex = 0;
        selectedSegmentIndex = 0;
        replayCursorMs = 0;
        autoReplay = false;
        RefreshFileList();
        loadedVisit = null;
        loadedPath = null;
    }

    public void Exit()
    {
        IsActive = false;
        detailMode = false;
        loadedVisit = null;
        autoReplay = false;
    }

    private void RefreshFileList()
    {
        string dir = analyticsDirFactory != null ? analyticsDirFactory() : null;
        sessionPaths = SessionAnalyticsRecorder.ListSessionFiles(dir ?? "");
        if (recorder != null && recorder.HasActiveVisit)
            sessionPaths.Insert(0, "__LIVE__");
    }

    public void Tick(int deltaMs)
    {
        if (!IsActive || !detailMode || !autoReplay || loadedVisit == null) return;
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
            if (!detailMode)
                requestCloseAppPanel = true;
            return;
        }

        lastTuioAngleValid = true;
        lastTuioAngleDeg = angleRad / (float)Math.PI * 180f;

        if (!detailMode)
        {
            int n = Math.Max(1, sessionPaths.Count);
            float fromTop = NormalizeAngle(angleRad + (float)Math.PI / 2f);
            float step = (float)(Math.PI * 2.0 / n);
            int idx = (int)(fromTop / step);
            if (idx >= n) idx = n - 1;
            if (idx < 0) idx = 0;
            selectedListIndex = idx;
        }
        else
        {
            if (loadedVisit != null && loadedVisit.Segments != null && loadedVisit.Segments.Count > 0)
            {
                int n = loadedVisit.Segments.Count;
                float fromTop = NormalizeAngle(angleRad + (float)Math.PI / 2f);
                float step = (float)(Math.PI * 2.0 / n);
                int idx = (int)(fromTop / step);
                if (idx >= n) idx = n - 1;
                if (idx < 0) idx = 0;
                selectedSegmentIndex = idx;
            }
        }

        if (!hasLastY)
        {
            hasLastY = true;
            lastY = yNorm;
            accumY = 0f;
            return;
        }

        float frameDy = yNorm - lastY;
        accumY += frameDy;
        if (Math.Abs(yNorm - 0.5f) <= NeutralBand)
        {
            menuGestureArmed = true;
            accumY = 0f;
        }

        float upDelta = MenuUpIsPositiveY ? (-accumY) : accumY;
        float downDelta = -upDelta;

        if (menuGestureArmed && upDelta >= TriggerDy)
        {
            menuGestureArmed = false;
            accumY = 0f;
            if (!detailMode)
                OpenSelectedSession();
            else
                autoReplay = !autoReplay;
        }
        else if (menuGestureArmed && downDelta >= TriggerDy)
        {
            menuGestureArmed = false;
            accumY = 0f;
            if (detailMode)
            {
                detailMode = false;
                loadedVisit = null;
                loadedPath = null;
                RefreshFileList();
            }
        }

        lastY = yNorm;
    }

    private void OpenSelectedSession()
    {
        if (sessionPaths.Count == 0) return;
        string path = sessionPaths[Math.Min(selectedListIndex, sessionPaths.Count - 1)];
        if (path == "__LIVE__")
        {
            loadedVisit = null;
            loadedPath = null;
            detailMode = true;
            selectedSegmentIndex = 0;
            replayCursorMs = 0;
            return;
        }

        try
        {
            loadedVisit = SessionAnalyticsRecorder.Load(path);
            loadedPath = path;
            detailMode = true;
            selectedSegmentIndex = 0;
            replayCursorMs = 0;
        }
        catch
        {
            loadedVisit = null;
        }
    }

    private AnalyticsSegmentDoc GetSelectedSegment()
    {
        if (loadedVisit == null || loadedVisit.Segments == null || loadedVisit.Segments.Count == 0)
            return null;
        int i = Math.Min(selectedSegmentIndex, loadedVisit.Segments.Count - 1);
        return loadedVisit.Segments[i];
    }

    public void Draw(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        if (!IsActive) return;

        using (var bg = new SolidBrush(Color.FromArgb(245, 14, 16, 22)))
            g.FillRectangle(bg, 0, 0, w, h);

        DrawCentered(g, "ADMIN ANALYTICS (control marker symbol " + TuioControlMarker.MenuAuthSymbolId + "; rotate = select, flick up/down = enter / back)",
            fontTitle, accent, new RectangleF(20, 16, w - 40, 44));

        if (!detailMode)
            DrawSessionList(g, w, h, fontBody, fontSmall, accent, papyrus);
        else
            DrawDetail(g, w, h, fontTitle, fontBody, fontSmall, accent, papyrus, live);

        using (var p = new Pen(Color.FromArgb(120, accent), 2))
            g.DrawRectangle(p, 10, 10, w - 20, h - 20);

        string rotLine = lastTuioAngleValid
            ? string.Format("TUIO marker angle: {0:0}°", lastTuioAngleDeg)
            : "TUIO marker angle: — (place symbol " + TuioControlMarker.MenuAuthSymbolId + " on the table; ID 0 reserved)";
        DrawCentered(g, rotLine, fontSmall, Color.FromArgb(200, 200, 200, 210),
            new RectangleF(20, h - 32, w - 40, 22));
    }

    private void DrawSessionList(Graphics g, int w, int h, Font fontBody, Font fontSmall, Color accent, Color papyrus)
    {
        DrawCentered(g, "Recorded sessions (newest first)", fontBody, papyrus,
            new RectangleF(20, 70, w - 40, 28));

        if (sessionPaths.Count == 0)
        {
            DrawCentered(g, "No sessions yet. Slideshow gaze is recorded while the Python gaze service runs.",
                fontBody, Color.White, new RectangleF(40, h / 2f - 40, w - 80, 80));
            return;
        }

        int cx = w / 2;
        int cy = h / 2 + 20;
        int radius = Math.Min(w, h) / 3;
        for (int i = 0; i < sessionPaths.Count; i++)
        {
            float a0 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * i) / sessionPaths.Count);
            float a1 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * (i + 1)) / sessionPaths.Count);
            bool sel = i == selectedListIndex;
            using (var br = new SolidBrush(sel ? Color.FromArgb(220, accent) : Color.FromArgb(160, 40, 42, 50)))
            {
                using (var path = new GraphicsPath())
                {
                    path.AddPie(cx - radius, cy - radius, radius * 2, radius * 2,
                        (float)(a0 * 180.0 / Math.PI), (float)((a1 - a0) * 180.0 / Math.PI));
                    g.FillPath(br, path);
                }
            }
            float am = (a0 + a1) * 0.5f;
            string label = sessionPaths[i] == "__LIVE__"
                ? "LIVE"
                : Path.GetFileName(sessionPaths[i]);
            if (label.Length > 14) label = label.Substring(0, 12) + "..";
            var ft = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float tx = cx + (float)Math.Cos(am) * (radius * 0.65f);
            float ty = cy + (float)Math.Sin(am) * (radius * 0.65f);
            using (var tbr = new SolidBrush(sel ? Color.Black : Color.White))
                g.DrawString(label, fontSmall, tbr, tx, ty, ft);
        }
    }

    private void DrawDetail(Graphics g, int w, int h, Font fontTitle, Font fontBody, Font fontSmall,
        Color accent, Color papyrus, LiveSessionSnapshot live)
    {
        bool isLive = loadedVisit == null;
        string title = isLive ? ("LIVE — " + (live != null ? live.DisplayName : "...")) : (loadedVisit.DisplayName + " · " + Path.GetFileName(loadedPath));
        DrawCentered(g, title, fontTitle, accent, new RectangleF(20, 62, w - 40, 40));

        DrawCentered(g, "Flick DOWN = back to list   Flick UP = toggle auto-replay (detail)",
            fontSmall, Color.FromArgb(200, papyrus), new RectangleF(20, 100, w - 40, 22));

        var gazeRect = new Rectangle(60, 140, w - 120, h - 280);
        using (var br = new SolidBrush(Color.FromArgb(200, 8, 9, 14)))
            g.FillRectangle(br, gazeRect);
        using (var pen = new Pen(Color.FromArgb(150, accent), 1))
            g.DrawRectangle(pen, gazeRect);

        List<AnalyticsSampleDoc> samplesDoc = null;
        List<AnalyticsSample> samplesLive = null;
        string segLabel = "";

        if (isLive)
        {
            if (live != null && live.RecentSamples.Count > 0)
            {
                samplesLive = live.RecentSamples;
                segLabel = live.StoryTitle + " · slide " + live.SlideIndex;
            }
        }
        else if (loadedVisit != null && loadedVisit.Segments != null && loadedVisit.Segments.Count > 0)
        {
            var seg = GetSelectedSegment();
            if (seg != null)
            {
                samplesDoc = seg.Samples;
                segLabel = seg.StoryTitle + " · slide " + seg.SlideIndex;
            }
        }

        DrawCentered(g, segLabel, fontBody, papyrus, new RectangleF(60, gazeRect.Top - 32, w - 120, 26));

        if (samplesDoc != null && samplesDoc.Count > 0)
        {
            if (!autoReplay)
            {
                long maxT = samplesDoc[samplesDoc.Count - 1].TRelMs;
                replayCursorMs = Math.Min(replayCursorMs, maxT);
            }
            DrawGazeTrail(g, gazeRect, samplesDoc, replayCursorMs);
            var at = samplesDoc.LastOrDefault(s => s.TRelMs <= replayCursorMs) ?? samplesDoc[0];
            DrawEmotionBars(g, new Rectangle(gazeRect.Right - 200, gazeRect.Top, 190, gazeRect.Height), at.Dominant, at.Emotions, fontSmall, accent);
        }
        else if (samplesLive != null && samplesLive.Count > 0)
        {
            DrawGazeTrailLive(g, gazeRect, samplesLive);
            var at = samplesLive[samplesLive.Count - 1];
            DrawEmotionBars(g, new Rectangle(gazeRect.Right - 200, gazeRect.Top, 190, gazeRect.Height), at.Dominant, at.Emotions, fontSmall, accent);
        }
        else
        {
            DrawCentered(g, "No samples in this segment yet.", fontBody, Color.Gray,
                new RectangleF(gazeRect.X, gazeRect.Y + gazeRect.Height / 2, gazeRect.Width, 40));
        }

        if (!isLive && loadedVisit != null && loadedVisit.Segments != null && loadedVisit.Segments.Count > 1)
        {
            int cx = w / 2;
            int cy = h - 100;
            int r = 70;
            int n = loadedVisit.Segments.Count;
            for (int i = 0; i < n; i++)
            {
                float a0 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * i) / n);
                float a1 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * (i + 1)) / n);
                bool sel = i == selectedSegmentIndex;
                using (var b = new SolidBrush(sel ? Color.FromArgb(200, 80, 140, 200) : Color.FromArgb(120, 35, 36, 42)))
                using (var path = new GraphicsPath())
                {
                    path.AddPie(cx - r, cy - r, r * 2, r * 2,
                        (float)(a0 * 180.0 / Math.PI), (float)((a1 - a0) * 180.0 / Math.PI));
                    g.FillPath(b, path);
                }
            }
            DrawCentered(g, "Rotate marker: slide segment", fontSmall, papyrus,
                new RectangleF(20, h - 48, w - 40, 22));
        }
    }

    private static void DrawGazeTrail(Graphics g, Rectangle zone, List<AnalyticsSampleDoc> samples, long cursorMs)
    {
        if (samples.Count < 2) return;
        using (var pen = new Pen(Color.FromArgb(180, 100, 200, 255), 2) { LineJoin = LineJoin.Round })
        {
            for (int i = 1; i < samples.Count; i++)
            {
                var a = samples[i - 1];
                var b = samples[i];
                int x1 = zone.Left + (int)(a.Gx * zone.Width);
                int y1 = zone.Top + (int)(a.Gy * zone.Height);
                int x2 = zone.Left + (int)(b.Gx * zone.Width);
                int y2 = zone.Top + (int)(b.Gy * zone.Height);
                float t = b.TRelMs <= cursorMs ? 1f : 0.35f;
                pen.Color = Color.FromArgb((int)(60 + 195 * t), 100, 200, 255);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }
        var cur = samples.LastOrDefault(s => s.TRelMs <= cursorMs);
        if (cur != null)
        {
            int cx = zone.Left + (int)(cur.Gx * zone.Width);
            int cy = zone.Top + (int)(cur.Gy * zone.Height);
            using (var br = new SolidBrush(Color.FromArgb(240, 255, 220, 80)))
                g.FillEllipse(br, cx - 8, cy - 8, 16, 16);
        }
    }

    private static void DrawGazeTrailLive(Graphics g, Rectangle zone, List<AnalyticsSample> samples)
    {
        if (samples.Count < 2) return;
        using (var pen = new Pen(Color.FromArgb(200, 120, 255, 160), 2))
        {
            for (int i = 1; i < samples.Count; i++)
            {
                var a = samples[i - 1];
                var b = samples[i];
                int x1 = zone.Left + (int)(a.Gx * zone.Width);
                int y1 = zone.Top + (int)(a.Gy * zone.Height);
                int x2 = zone.Left + (int)(b.Gx * zone.Width);
                int y2 = zone.Top + (int)(b.Gy * zone.Height);
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }
        var cur = samples[samples.Count - 1];
        int cx = zone.Left + (int)(cur.Gx * zone.Width);
        int cy = zone.Top + (int)(cur.Gy * zone.Height);
        using (var br = new SolidBrush(Color.FromArgb(240, 255, 200, 60)))
            g.FillEllipse(br, cx - 8, cy - 8, 16, 16);
    }

    private static void DrawEmotionBars(Graphics g, Rectangle zone, string dominant,
        Dictionary<string, double> emotions, Font fontSmall, Color accent)
    {
        if (emotions == null || emotions.Count == 0) return;
        string[] order = { "angry", "disgust", "fear", "happy", "sad", "surprise", "neutral" };
        int n = order.Length;
        int rowH = zone.Height / n;
        for (int i = 0; i < n; i++)
        {
            string key = order[i];
            double v = 0;
            emotions.TryGetValue(key, out v);
            var row = new Rectangle(zone.X, zone.Y + i * rowH, zone.Width, rowH - 2);
            using (var bg = new SolidBrush(Color.FromArgb(100, 30, 30, 36)))
                g.FillRectangle(bg, row);
            int bw = (int)(row.Width * Math.Max(0, Math.Min(1, v)));
            using (var fill = new SolidBrush(string.Equals(dominant, key, StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(220, accent) : Color.FromArgb(160, 80, 120, 160)))
                g.FillRectangle(fill, row.X, row.Y + 4, bw, row.Height - 8);
            using (var tbr = new SolidBrush(Color.White))
                g.DrawString(key + " " + (v * 100).ToString("0") + "%", fontSmall, tbr, row.X + 4, row.Y + 4);
        }
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        using (var br = new SolidBrush(color))
            g.DrawString(text, font, br, bounds, sf);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle < 0f) angle += (float)(Math.PI * 2.0);
        while (angle >= (float)(Math.PI * 2.0)) angle -= (float)(Math.PI * 2.0);
        return angle;
    }
}
