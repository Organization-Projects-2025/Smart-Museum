using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

public class CircularMenuController
{
    public bool IsVisible;
    public bool IsInSecondLevel;
    public bool IsInThirdLevel;

    public string SelectedTop;
    public string SelectedSecond;
    public string SelectedThird;
    public string SelectedFavoriteTitle;

    public readonly List<string> TopItems = new List<string>
    {
        "Favorite",
        "Favorites",
        "Watched",
        "Home",
        "Logout"
    };

    public readonly List<string> Favorites = new List<string>();
    public readonly List<string> Watched = new List<string>();
    public readonly List<string> FavoriteActions = new List<string>
    {
        "Show Slideshow",
        "Unfavorite"
    };
    public bool ShowFavorite = true;

    public int TopIndex;
    public int SecondIndex;
    public int ThirdIndex;

    /// <summary>Degrees — last TUIO marker angle passed to <see cref="UpdateRotation"/> (for on-screen feedback).</summary>
    public float LastTuioAngleDegrees { get; private set; }

    public Action<string, string> OnAction; // action, payload

    public void Show()
    {
        IsVisible = true;
        IsInSecondLevel = false;
        IsInThirdLevel = false;
        TopIndex = 0;
        SecondIndex = 0;
        ThirdIndex = 0;
        SelectedFavoriteTitle = null;
        SyncSelectionTexts();
    }

    public void Hide()
    {
        IsVisible = false;
        IsInSecondLevel = false;
        IsInThirdLevel = false;
        SelectedFavoriteTitle = null;
    }

    public void UpdateRotation(float angleRadians)
    {
        if (!IsVisible) return;

        LastTuioAngleDegrees = angleRadians / (float)Math.PI * 180f;

        List<string> topItems = GetTopLevelItems();
        int count;
        if (IsInThirdLevel) count = FavoriteActions.Count;
        else if (IsInSecondLevel) count = GetSecondLevelItems().Count;
        else count = topItems.Count;
        if (count <= 0) return;

        // Top direction is -90 degrees.
        float fromTop = Normalize(angleRadians + (float)Math.PI / 2f);
        float step = (float)(Math.PI * 2.0 / count);
        int idx = (int)(fromTop / step);
        if (idx >= count) idx = count - 1;
        if (idx < 0) idx = 0;

        if (IsInThirdLevel) ThirdIndex = idx;
        else if (IsInSecondLevel) SecondIndex = idx;
        else TopIndex = idx;

        SyncSelectionTexts();
    }

    public void MoveUpAction()
    {
        if (!IsVisible) return;

        List<string> topItems = GetTopLevelItems();
        if (topItems.Count == 0) return;

        if (TopIndex < 0) TopIndex = 0;
        if (TopIndex >= topItems.Count) TopIndex = topItems.Count - 1;

        string top = topItems[TopIndex];
        if (!IsInSecondLevel)
        {
            if (top == "Favorite")
            {
                if (OnAction != null) OnAction(top, null);
                return;
            }

            if (HasSecondLevel(top))
            {
                IsInSecondLevel = true;
                IsInThirdLevel = false;
                SecondIndex = 0;
                SelectedFavoriteTitle = null;
                SyncSelectionTexts();
                return;
            }

            if (OnAction != null) OnAction(top, null);
            return;
        }

        if (IsInThirdLevel)
        {
            if (!string.Equals(top, "Favorites", StringComparison.OrdinalIgnoreCase)) return;
            if (string.IsNullOrEmpty(SelectedFavoriteTitle)) return;

            if (FavoriteActions.Count == 0) return;
            if (ThirdIndex < 0) ThirdIndex = 0;
            if (ThirdIndex >= FavoriteActions.Count) ThirdIndex = FavoriteActions.Count - 1;

            string action = FavoriteActions[ThirdIndex];
            if (OnAction != null)
            {
                if (action == "Show Slideshow") OnAction("FavoritesShow", SelectedFavoriteTitle);
                else if (action == "Unfavorite") OnAction("FavoritesUnfavorite", SelectedFavoriteTitle);
            }
            return;
        }

        if (IsInSecondLevel)
        {
            var second = GetSecondLevelItems();
            if (second.Count == 0) return;
            string payload = second[SecondIndex];

            if (top == "Favorites")
            {
                IsInThirdLevel = true;
                SelectedFavoriteTitle = payload;
                ThirdIndex = 0;
                SyncSelectionTexts();
                return;
            }

            if (OnAction != null) OnAction(top, payload);
            return;
        }
    }

    public void MoveDownAction()
    {
        if (!IsVisible) return;

        // Down gesture means "go back one level".
        if (IsInThirdLevel)
        {
            IsInThirdLevel = false;
            SyncSelectionTexts();
            return;
        }

        if (IsInSecondLevel)
        {
            IsInSecondLevel = false;
            SelectedFavoriteTitle = null;
            SyncSelectionTexts();
        }
    }

    public void Draw(Graphics g, int w, int h, Color secondary, Color tertiary, Font titleFont, Font smallFont)
    {
        if (!IsVisible) return;

        int cx = w / 2;
        int cy = h / 2;
        int innerHoleRadius = 70;
        int topInner = 80;
        int topOuter = 178;
        int secondInner = 188;
        int secondOuter = 276;

        Color panelDark = Color.FromArgb(228, 28, 31, 35);
        Color panelDarkAlt = Color.FromArgb(230, 35, 38, 42);
        Color edge = Color.FromArgb(210, secondary);
        Color activeFill = Color.FromArgb(220, tertiary);
        Color chip = Color.FromArgb(235, secondary);
        Color chipText = Color.White;

        List<string> topItems = GetTopLevelItems();
        DrawDonutSegments(
            g,
            cx,
            cy,
            topInner,
            topOuter,
            topItems,
            TopIndex,
            !IsInSecondLevel,
            panelDark,
            panelDarkAlt,
            edge,
            activeFill,
            chip,
            chipText,
            smallFont,
            GetTopIconText);

        // Show second level ring as preview (unhighlighted) if hovering over item with submenu,
        // or highlighted if actually in second level
        bool hasSecondLevelAtCurrent = HasSecondLevel(topItems.Count > TopIndex && TopIndex >= 0 ? topItems[TopIndex] : "");
        bool showSecondRing = (IsInSecondLevel && !IsInThirdLevel) || (!IsInSecondLevel && !IsInThirdLevel && hasSecondLevelAtCurrent);
        
        if (showSecondRing)
        {
            List<string> second = GetSecondLevelItems();
            int previewSecondIndex = IsInSecondLevel ? SecondIndex : 0; // Show first item in preview
            DrawDonutSegments(
                g,
                cx,
                cy,
                secondInner,
                secondOuter,
                second,
                previewSecondIndex,
                IsInSecondLevel, // Only highlight if actually in second level (not preview)
                Color.FromArgb(220, 32, 35, 39),
                Color.FromArgb(220, 39, 43, 47),
                Color.FromArgb(180, tertiary),
                Color.FromArgb(230, secondary),
                Color.FromArgb(235, tertiary),
                Color.White,
                smallFont,
                GetSecondIconText);
        }

            if (IsInThirdLevel)
            {
                DrawDonutSegments(
                g,
                cx,
                cy,
                secondInner,
                secondOuter,
                FavoriteActions,
                ThirdIndex,
                true,
                Color.FromArgb(220, 35, 34, 41),
                Color.FromArgb(220, 44, 43, 51),
                Color.FromArgb(180, tertiary),
                Color.FromArgb(230, secondary),
                Color.FromArgb(235, tertiary),
                Color.White,
                smallFont,
                GetSecondIconText);
            }

        using (var centerBrush = new SolidBrush(Color.FromArgb(225, 236, 236, 236)))
            g.FillEllipse(centerBrush, cx - innerHoleRadius, cy - innerHoleRadius, innerHoleRadius * 2, innerHoleRadius * 2);

        using (var centerEdge = new Pen(Color.FromArgb(180, 95, 95, 95), 2f))
            g.DrawEllipse(centerEdge, cx - innerHoleRadius, cy - innerHoleRadius, innerHoleRadius * 2, innerHoleRadius * 2);

        string centerText = IsInThirdLevel
            ? (SelectedThird ?? string.Empty)
            : (IsInSecondLevel ? (SelectedSecond ?? string.Empty) : (SelectedTop ?? string.Empty));
        DrawCentered(g, centerText, titleFont, secondary, new RectangleF(cx - 150, cy - 26, 300, 52));

        int segCount = IsInThirdLevel
            ? FavoriteActions.Count
            : (IsInSecondLevel ? GetSecondLevelItems().Count : topItems.Count);
        float segDeg = segCount > 0 ? 360f / segCount : 0f;
        string rotHud = segCount > 1
            ? string.Format("TUIO angle {0:0}° (~{1:0}° per segment)", LastTuioAngleDegrees, segDeg)
            : string.Format("TUIO angle {0:0}°", LastTuioAngleDegrees);
        DrawCentered(g, rotHud, smallFont, Color.FromArgb(200, 220, 220, 220),
            new RectangleF(20, h - 36, w - 40, 22));
    }

    private static void DrawDonutSegments(
        Graphics g,
        int cx,
        int cy,
        int innerRadius,
        int outerRadius,
        List<string> labels,
        int selectedIndex,
        bool selectedRingActive,
        Color panelA,
        Color panelB,
        Color edge,
        Color activeFill,
        Color chipColor,
        Color chipText,
        Font labelFont,
        Func<int, string, string> iconTextFactory)
    {
        if (labels == null || labels.Count == 0) return;

        float segmentSweep = 360f / labels.Count;
        float gap = Math.Min(3.5f, segmentSweep * 0.22f);

        for (int i = 0; i < labels.Count; i++)
        {
            float start = -90f + i * segmentSweep + gap / 2f;
            float sweep = segmentSweep - gap;

            bool selected = selectedRingActive && i == selectedIndex;
            Color fill = selected ? activeFill : (i % 2 == 0 ? panelA : panelB);

            using (GraphicsPath path = BuildDonutSegmentPath(cx, cy, innerRadius, outerRadius, start, sweep))
            using (var br = new SolidBrush(fill))
            using (var p = new Pen(edge, selected ? 2.8f : 1.2f))
            {
                g.FillPath(br, path);
                g.DrawPath(p, path);
            }

            float centerDeg = start + sweep / 2f;
            float centerRad = centerDeg * (float)Math.PI / 180f;
            float textRadius = (innerRadius + outerRadius) * 0.5f;
            int tx = (int)(cx + Math.Cos(centerRad) * textRadius);
            int ty = (int)(cy + Math.Sin(centerRad) * textRadius);

            int iconSize = selected ? 36 : 31;
            int iconY = ty - 18;
            using (var chipBrush = new SolidBrush(chipColor))
                g.FillEllipse(chipBrush, tx - iconSize / 2, iconY - iconSize / 2, iconSize, iconSize);

            using (var chipPen = new Pen(Color.FromArgb(180, 255, 255, 255), 1.2f))
                g.DrawEllipse(chipPen, tx - iconSize / 2, iconY - iconSize / 2, iconSize, iconSize);

            string iconText = iconTextFactory != null ? iconTextFactory(i, labels[i]) : "";
            using (var iconFont = new Font("Georgia", Math.Max(10f, labelFont.Size - 1f), FontStyle.Bold, GraphicsUnit.Pixel))
                DrawCentered(g, iconText, iconFont, chipText, new RectangleF(tx - 24, iconY - 11, 48, 22));

            DrawCentered(g, TrimLabel(labels[i]), labelFont, Color.White, new RectangleF(tx - 52, ty + 2, 104, 24));
        }
    }

    private static GraphicsPath BuildDonutSegmentPath(int cx, int cy, int innerRadius, int outerRadius, float startDeg, float sweepDeg)
    {
        var path = new GraphicsPath();

        Rectangle outer = new Rectangle(cx - outerRadius, cy - outerRadius, outerRadius * 2, outerRadius * 2);
        Rectangle inner = new Rectangle(cx - innerRadius, cy - innerRadius, innerRadius * 2, innerRadius * 2);

        path.AddArc(outer, startDeg, sweepDeg);
        path.AddArc(inner, startDeg + sweepDeg, -sweepDeg);
        path.CloseFigure();

        return path;
    }

    private static string TrimLabel(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= 12 ? text : text.Substring(0, 12);
    }

    private static string GetTopIconText(int index, string label)
    {
        if (label == "Favorite") return "+";
        if (label == "Favorites") return "F";
        if (label == "Watched") return "W";
        if (label == "Home") return "H";
        if (label == "Logout") return "X";
        if (label == "Analytics") return "A";
        return !string.IsNullOrEmpty(label) ? label.Substring(0, 1).ToUpperInvariant() : "?";
    }

    private static string GetSecondIconText(int index, string label)
    {
        if (string.IsNullOrEmpty(label)) return "?";
        var words = label.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count == 1)
        {
            string w = words[0];
            return w.Length >= 2 ? w.Substring(0, 2).ToUpperInvariant() : w.ToUpperInvariant();
        }
        return (words[0][0].ToString() + words[1][0].ToString()).ToUpperInvariant();
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        using (var br = new SolidBrush(color))
            g.DrawString(text, font, br, bounds, sf);
    }

    private void SyncSelectionTexts()
    {
        List<string> topItems = GetTopLevelItems();
        if (topItems.Count > 0)
        {
            if (TopIndex < 0) TopIndex = 0;
            if (TopIndex >= topItems.Count) TopIndex = topItems.Count - 1;
            SelectedTop = topItems[TopIndex];
        }
        else
        {
            TopIndex = 0;
            SelectedTop = null;
        }

        SelectedThird = null;

        var second = GetSecondLevelItems();
        if (second.Count > 0)
        {
            if (SecondIndex < 0) SecondIndex = 0;
            if (SecondIndex >= second.Count) SecondIndex = second.Count - 1;
            SelectedSecond = second[SecondIndex];
        }
        else
        {
            SelectedSecond = null;
        }

        if (IsInThirdLevel && FavoriteActions.Count > 0)
        {
            if (ThirdIndex < 0) ThirdIndex = 0;
            if (ThirdIndex >= FavoriteActions.Count) ThirdIndex = FavoriteActions.Count - 1;
            SelectedThird = FavoriteActions[ThirdIndex];
        }
    }

    private List<string> GetSecondLevelItems()
    {
        List<string> topItems = GetTopLevelItems();
        if (topItems.Count == 0) return new List<string>();

        if (TopIndex < 0) TopIndex = 0;
        if (TopIndex >= topItems.Count) TopIndex = topItems.Count - 1;

        string top = topItems[TopIndex];
        if (top == "Favorites") return Favorites;
        if (top == "Watched") return Watched;
        return new List<string>();
    }

    private static bool HasSecondLevel(string top)
    {
        return top == "Favorites" || top == "Watched";
    }

    private List<string> GetTopLevelItems()
    {
        if (ShowFavorite) return TopItems;
        return TopItems.Where(item => item != "Favorite").ToList();
    }

    private static float Normalize(float angle)
    {
        while (angle < 0f) angle += (float)(Math.PI * 2.0);
        while (angle >= (float)(Math.PI * 2.0)) angle -= (float)(Math.PI * 2.0);
        return angle;
    }
}
