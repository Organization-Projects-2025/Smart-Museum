// Keyboard shortcuts: F1 = full-screen, Escape = exit

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TUIO;

public enum AppState
{
    Idle,           // No markers on the table
    Recognition,    // A figure is detected and the start condition is being validated
    SingleFigure,   // Exactly one known figure is active
    PairNotFacing,  // Two known figures are present but not facing each other
    PairFacing      // Two known figures are facing each other
}

public static class FacingDetector
{
    private const float ThresholdRad = (float)(Math.PI / 4.0);

    public static bool AreFacing(TuioObject a, Figure defA,
        TuioObject b, Figure defB)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float dirAtoB = (float)Math.Atan2(dy, dx);

        float effectiveA = a.Angle + (defA != null ? defA.FacingAngleOffset : 0f);
        float effectiveB = b.Angle + (defB != null ? defB.FacingAngleOffset : 0f);

        float diffA = NormalizeAngle(effectiveA - dirAtoB);
        float diffB = NormalizeAngle(effectiveB - (dirAtoB + (float)Math.PI));

        return Math.Abs(diffA) < ThresholdRad && Math.Abs(diffB) < ThresholdRad;
    }

    public static float FacingDeviation(TuioObject a, Figure defA, TuioObject b)
    {
        float dx = b.X - a.X;
        float dy = b.Y - a.Y;
        float dirAtoB = (float)Math.Atan2(dy, dx);
        float effectiveA = a.Angle + (defA != null ? defA.FacingAngleOffset : 0f);
        return NormalizeAngle(effectiveA - dirAtoB);
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle > (float)Math.PI) angle -= 2f * (float)Math.PI;
        while (angle < -(float)Math.PI) angle += 2f * (float)Math.PI;
        return angle;
    }
}

public class TuioDemo : Form, TuioListener
{
    private TuioClient client;
    private Dictionary<long, TuioObject> objectList = new Dictionary<long, TuioObject>(64);

    private struct StarPoint { public int X, Y, S; }

    private AppState state = AppState.Idle;
    private Figure activeFig;
    private Relationship activeRel;
    private TuioObject objA, objB;

    private SlideShowManager slideShow;
    private ContentSlide currentSlide;
    private bool slideshowLocked = false;
    private bool waitForClearAfterLockedShow = false;
    private int activeFigureSymbolId = -1;
    private bool singleFigureIntroDone = false;
    private int slideElapsedMs = 0;

    private enum SlideShowContext
    {
        None,
        SingleFigureIntro,
        SceneObjectStory,
        Relationship,
        MenuStory
    }
    private SlideShowContext lockedContext = SlideShowContext.None;
    private string activeStoryKey = null;

    private bool isLoggedIn = false;
    private bool authInProgress = false;
    private string authStatus = "Waiting for Face ID";
    private VisitorProfile visitorProfile;

    private Color themePrimary = Color.FromArgb(12, 12, 12);
    private Color themeSecondary = Color.FromArgb(212, 175, 55);
    private Color themeTertiary = Color.FromArgb(201, 166, 107);

    private CircularMenuController circularMenu = new CircularMenuController();
    private Dictionary<string, List<ContentSlide>> storySlidesByKey =
        new Dictionary<string, List<ContentSlide>>();
    private Dictionary<string, string> storyTitleByKey =
        new Dictionary<string, string>();
    private Dictionary<string, string> storyKeyByTitle =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool menuGestureArmed = true;
    private bool hasLastMenuMarkerY = false;
    private float lastMenuMarkerY = 0.5f;
    private float menuGestureAccumY = 0f;
    private const int CircularMenuMarkerSymbolId = 0;
    private const bool MenuUpIsPositiveY = true;
    private const float MenuMoveTriggerDeltaY = 0.035f;
    private const float MenuMoveNeutralBandY = 0.015f;

    private System.Windows.Forms.Timer recognitionTimer;
    private float recognitionProgress = 0f;   // 0..1
    private const int RecognitionMs = 3000; // ms before slideshow starts
    private const float CenterZoneHalfWidth = 0.12f;
    private const float CenterZoneHalfHeight = 0.12f;

    private SceneObject hoverObject;
    private SceneObject activeObjectStory;
    private float objectHoldProgress = 0f; // 0..1
    private const int ObjectHoldMs = 1500;
    private const float ObjectFacingThresholdRad = (float)(Math.PI / 6.0); // 30 degrees

    private int W, H;
    private int winW = 1280, winH = 720;
    private int winLeft, winTop;
    private bool fullscreen;

    private System.Windows.Forms.Timer animTimer;
    private float idlePhase = 0f;
    private float fadeAlpha = 1f;
    private bool fadingIn = false;

    private Dictionary<string, Image> imgCache =
        new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

    private Font fontTitle;
    private Font fontSubtitle;
    private Font fontBody;
    private Font fontSmall;
    private Font fontHint;

    private static Color CGold = Color.FromArgb(212, 175, 55);
    private static Color CGoldLight = Color.FromArgb(255, 220, 100);
    private static Color CGoldDim = Color.FromArgb(90, 70, 20);
    private static Color CPapyrus = Color.FromArgb(240, 220, 165);
    private static Color CBg = Color.FromArgb(10, 8, 25);
    private static Color CGreen = Color.FromArgb(80, 210, 100);

    private StarPoint[] stars;

    public TuioDemo(int port)
    {
        var rng = new Random(1337);
        stars = new StarPoint[100];
        for (int i = 0; i < stars.Length; i++)
        {
            StarPoint sp;
            sp.X = rng.Next(1280);
            sp.Y = rng.Next(720);
            sp.S = rng.Next(1, 3);
            stars[i] = sp;
        }

        fontTitle = new Font("Georgia", 48f, FontStyle.Bold, GraphicsUnit.Pixel);
        fontSubtitle = new Font("Georgia", 28f, FontStyle.Italic, GraphicsUnit.Pixel);
        fontBody = new Font("Georgia", 22f, FontStyle.Regular, GraphicsUnit.Pixel);
        fontSmall = new Font("Georgia", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
        fontHint = new Font("Georgia", 18f, FontStyle.Italic, GraphicsUnit.Pixel);

        W = winW; H = winH;
        this.ClientSize = new Size(W, H);
        this.Text = "Smart Grand Egyptian Museum";
        this.BackColor = CBg;
        this.Cursor = Cursors.Default;
        this.SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.DoubleBuffer |
            ControlStyles.OptimizedDoubleBuffer, true);

        this.KeyDown += OnKeyDown;
        this.Closing += OnClosing;

        slideShow = new SlideShowManager();
        slideShow.SlideChanged += slide =>
        {
            currentSlide = slide;
            fadeAlpha = 0f;
            fadingIn = true;
            slideElapsedMs = 0;
            SafeInvalidate();
        };
        slideShow.SlideShowCompleted += OnSlideShowCompleted;

        recognitionTimer = new System.Windows.Forms.Timer { Interval = 50 };
        recognitionTimer.Tick += OnRecognitionTick;

        animTimer = new System.Windows.Forms.Timer { Interval = 33 };
        animTimer.Tick += OnAnimTick;
        animTimer.Start();

        client = new TuioClient(port);
        client.addTuioListener(this);
        client.connect();

        InitializeStoryLibrary();
        InitializeCircularMenu();
        StartLoginFlow();
    }

    private void StartLoginFlow()
    {
        authInProgress = true;
        isLoggedIn = false;
        authStatus = "Face ID is starting...";

        Thread t = new Thread(() =>
        {
            try
            {
                string workspaceRoot = GetWorkspaceRoot();
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;

                string csvPath = Path.Combine(workspaceRoot, "C#", "content", "auth", "users.csv");
                List<VisitorProfile> users = VisitorProfile.LoadFromCsv(csvPath);

                var faceService = new FaceRecognitionService();
                string faceUserId;
                string faceStatus;
                bool faceOk = faceService.Scan(out faceUserId, out faceStatus);
                authStatus = faceStatus;

                if (!faceOk || string.IsNullOrEmpty(faceUserId))
                {
                    authInProgress = false;
                    SafeInvalidate();
                    return;
                }

                VisitorProfile selected = null;
                selected = users.Find(u => string.Equals(u.FaceUserId, faceUserId, StringComparison.OrdinalIgnoreCase));

                if (selected == null)
                {
                    authStatus = "Face ID user '" + faceUserId + "' is not found in CSV.";
                    authInProgress = false;
                    SafeInvalidate();
                    return;
                }

                var btService = new BluetoothService();
                string btStatus;
                bool btOk = btService.Verify(selected.BluetoothMacAddress, out btStatus);
                authStatus = btStatus;

                if (!btOk)
                {
                    authInProgress = false;
                    SafeInvalidate();
                    return;
                }

                visitorProfile = selected;
                authStatus = "Welcome " + visitorProfile.FullName + " (" + visitorProfile.Language + ")";

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        ApplyVisitorTheme();
                        authInProgress = false;
                        isLoggedIn = true;
                        Transition(AppState.Idle, null, null, null, null);
                        Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                authStatus = "Login failed: " + ex.Message;
                authInProgress = false;
                SafeInvalidate();
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlow";
        t.Start();
    }

    private string GetWorkspaceRoot()
    {
        DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        if (d.Parent != null) d = d.Parent;
        if (d.Parent != null) d = d.Parent;
        if (d.Parent != null) d = d.Parent;
        return d.FullName;
    }

    private void ApplyVisitorTheme()
    {
        if (visitorProfile == null) return;

        themePrimary = visitorProfile.PrimaryColor;
        themeSecondary = visitorProfile.SecondaryColor;
        themeTertiary = visitorProfile.TertiaryColor;

        if (fontTitle != null) fontTitle.Dispose();
        if (fontSubtitle != null) fontSubtitle.Dispose();
        if (fontBody != null) fontBody.Dispose();
        if (fontSmall != null) fontSmall.Dispose();
        if (fontHint != null) fontHint.Dispose();

        fontTitle = new Font("Georgia", visitorProfile.TitleSizePx, FontStyle.Bold, GraphicsUnit.Pixel);
        fontSubtitle = new Font("Georgia", visitorProfile.SubtitleSizePx, FontStyle.Italic, GraphicsUnit.Pixel);
        fontBody = new Font("Georgia", visitorProfile.BodySizePx, FontStyle.Regular, GraphicsUnit.Pixel);
        fontSmall = new Font("Georgia", visitorProfile.SmallSizePx, FontStyle.Regular, GraphicsUnit.Pixel);
        fontHint = new Font("Georgia", visitorProfile.BodySizePx, FontStyle.Italic, GraphicsUnit.Pixel);
    }

    private void InitializeStoryLibrary()
    {
        foreach (Figure fig in MuseumData.Figures.Values)
        {
            string key = "figure:" + fig.SymbolId;
            string title = "Figure: " + fig.Name;
            RegisterStory(key, title, fig.SoloSlides);
        }

        for (int i = 0; i < MuseumData.Relationships.Count; i++)
        {
            Relationship rel = MuseumData.Relationships[i];
            string key = "relationship:" + rel.SymbolIdA + "_" + rel.SymbolIdB;
            string title = "Connection: " + rel.ConnectionTitle;
            RegisterStory(key, title, rel.Slides);
        }
    }

    private void InitializeCircularMenu()
    {
        circularMenu.OnAction = HandleMenuAction;

        AddFavoriteIfExists("figure:1");
        AddFavoriteIfExists("figure:2");
        AddFavoriteIfExists("relationship:1_2");
    }

    private void RegisterStory(string key, string title, List<ContentSlide> slides)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(title) || slides == null || slides.Count == 0)
            return;

        storySlidesByKey[key] = slides;
        storyTitleByKey[key] = title;
        storyKeyByTitle[title] = key;
    }

    private void AddFavoriteIfExists(string key)
    {
        if (!storyTitleByKey.ContainsKey(key)) return;
        string title = storyTitleByKey[key];
        if (!circularMenu.Favorites.Contains(title))
            circularMenu.Favorites.Add(title);
    }

    private void AddWatchedIfExists(string key)
    {
        if (!storyTitleByKey.ContainsKey(key)) return;
        string title = storyTitleByKey[key];
        if (!circularMenu.Watched.Contains(title))
            circularMenu.Watched.Add(title);
    }

    private void HandleMenuAction(string action, string payload)
    {
        if (action == "Favorite")
        {
            string key = GetCurrentFigureStoryKey();
            if (string.IsNullOrEmpty(key))
            {
                authStatus = "No active figure to favorite right now.";
                return;
            }

            if (storyTitleByKey.ContainsKey(key))
            {
                string title = storyTitleByKey[key];
                if (!circularMenu.Favorites.Contains(title))
                {
                    circularMenu.Favorites.Add(title);
                    authStatus = "Added to favorites: " + title;
                }
                else
                {
                    authStatus = "Already in favorites: " + title;
                }
            }
            return;
        }

        if (action == "Home")
        {
            circularMenu.Hide();
            StopAndUnlockSlides();
            Transition(AppState.Idle, null, null, null, null);
            return;
        }

        if (action == "Logout")
        {
            circularMenu.Hide();
            StopAndUnlockSlides();
            visitorProfile = null;
            authStatus = "Logged out.";
            StartLoginFlow();
            return;
        }

        if (action == "FavoritesShow" && !string.IsNullOrEmpty(payload))
        {
            string key;
            if (storyKeyByTitle.TryGetValue(payload, out key))
            {
                List<ContentSlide> slides;
                if (storySlidesByKey.TryGetValue(key, out slides))
                {
                    circularMenu.Hide();
                    StartLockedSlideShow(slides, SlideShowContext.MenuStory, key);
                }
            }
            return;
        }

        if (action == "FavoritesUnfavorite" && !string.IsNullOrEmpty(payload))
        {
            circularMenu.Favorites.Remove(payload);
            authStatus = "Removed from favorites: " + payload;
            circularMenu.MoveDownAction();
            return;
        }

        if ((action == "Favorites" || action == "Watched") && !string.IsNullOrEmpty(payload))
        {
            string key;
            if (storyKeyByTitle.TryGetValue(payload, out key))
            {
                List<ContentSlide> slides;
                if (storySlidesByKey.TryGetValue(key, out slides))
                {
                    circularMenu.Hide();
                    StartLockedSlideShow(slides, SlideShowContext.MenuStory, key);
                }
            }
        }
    }

    private string GetCurrentFigureStoryKey()
    {
        if (activeFig != null)
            return "figure:" + activeFig.SymbolId;

        if (!string.IsNullOrEmpty(activeStoryKey) && activeStoryKey.StartsWith("figure:", StringComparison.OrdinalIgnoreCase))
            return activeStoryKey;

        return null;
    }

    private void StopAndUnlockSlides()
    {
        slideShow.Stop();
        slideshowLocked = false;
        waitForClearAfterLockedShow = false;
        lockedContext = SlideShowContext.None;
        activeStoryKey = null;
        currentSlide = null;
        slideElapsedMs = 0;
        activeObjectStory = null;
        objectHoldProgress = 0f;
    }

    // TuioListener

    public void addTuioObject(TuioObject o)
    {
        lock (objectList) objectList[o.SessionID] = o;
    }

    public void updateTuioObject(TuioObject o)
    {
        lock (objectList)
            if (objectList.ContainsKey(o.SessionID)) objectList[o.SessionID] = o;
    }

    public void removeTuioObject(TuioObject o)
    {
        lock (objectList) objectList.Remove(o.SessionID);
    }

    public void addTuioCursor(TuioCursor c) { }
    public void updateTuioCursor(TuioCursor c) { }
    public void removeTuioCursor(TuioCursor c) { }
    public void addTuioBlob(TuioBlob b) { }
    public void updateTuioBlob(TuioBlob b) { }
    public void removeTuioBlob(TuioBlob b) { }

    /// <summary>Called once per TUIO frame; updates state on the UI thread.</summary>
    public void refresh(TuioTime frameTime)
    {
        if (IsHandleCreated)
            this.BeginInvoke(new Action(EvaluateState));
    }

    // State machine

    private void EvaluateState()
    {
        if (!isLoggedIn || authInProgress) return;
        if (circularMenu.IsVisible) return;

        List<TuioObject> onTable;
        lock (objectList) onTable = new List<TuioObject>(objectList.Values);

        // Keep only recognised figures
        onTable = onTable.FindAll(o => o.SymbolID != CircularMenuMarkerSymbolId && MuseumData.Figures.ContainsKey(o.SymbolID));

        // Keep current slideshow stable even when camera briefly loses tracking.
        if (slideshowLocked)
        {
            return;
        }

        // After a locked slideshow ends, require the table to clear once to avoid
        // instant re-trigger with the same markers still present.
        if (waitForClearAfterLockedShow)
        {
            if (onTable.Count > 0)
            {
                return;
            }

            waitForClearAfterLockedShow = false;
            Transition(AppState.Idle, null, null, null, null);
            return;
        }

        if (onTable.Count == 0)
        {
            recognitionTimer.Stop();
            recognitionProgress = 0f;
            Transition(AppState.Idle, null, null, null, null);
            return;
        }

        // If we are currently in Recognition, don't re-trigger it — let the
        // countdown finish naturally. Only update the live object references.
        if (state == AppState.Recognition)
        {
            lock (objectList)
            {
                if (onTable.Count >= 2)
                {
                    objA = onTable[0];
                    objB = onTable[1];
                }
                else
                {
                    objA = onTable[0];
                    objB = null;
                }
            }
            Invalidate();
            return;
        }

        // Determine what the final state should be
        AppState targetState;
        Figure pendingFigureLocal = null;
        Relationship pendingRelationshipLocal = null;
        TuioObject pendingA = null, pendingB = null;

        if (onTable.Count == 1)
        {
            targetState = AppState.SingleFigure;
            pendingFigureLocal = MuseumData.Figures[onTable[0].SymbolID];
            pendingA = onTable[0];

            if (activeFigureSymbolId != pendingFigureLocal.SymbolId)
            {
                activeFigureSymbolId = pendingFigureLocal.SymbolId;
                singleFigureIntroDone = false;
                activeObjectStory = null;
            }
        }
        else
        {
            activeFigureSymbolId = -1;
            singleFigureIntroDone = false;
            activeObjectStory = null;

            TuioObject a = onTable[0], b = onTable[1];
            Figure defA = MuseumData.Figures[a.SymbolID];
            Figure defB = MuseumData.Figures[b.SymbolID];
            pendingRelationshipLocal = FindRelationship(a.SymbolID, b.SymbolID);
            pendingA = a; pendingB = b;

            if (pendingRelationshipLocal != null && FacingDetector.AreFacing(a, defA, b, defB))
                targetState = AppState.PairFacing;
            else
                targetState = AppState.PairNotFacing;
        }

        // If markers were already active and state is stable, transition directly
        // (avoids re-triggering recognition on every update while figures are on table)
        bool wasIdle = (state == AppState.Idle);
        if (!wasIdle)
        {
            if (targetState == AppState.SingleFigure)
                Transition(targetState, pendingFigureLocal, null, pendingA, pendingB);
            else
                Transition(targetState, null, pendingRelationshipLocal, pendingA, pendingB);
            return;
        }

        // Coming from Idle: start the recognition countdown
        pendingState = targetState;
        pendingFig = pendingFigureLocal;
        pendingRel = pendingRelationshipLocal;
        objA = pendingA;
        objB = pendingB;
        recognitionProgress = 0f;
        state = AppState.Recognition;
        recognitionTimer.Start();
        Invalidate();
    }

    // Stored pending state used by recognition countdown
    private AppState pendingState;
    private Figure pendingFig;
    private Relationship pendingRel;

    private void OnRecognitionTick(object sender, EventArgs e)
    {
        // Update pending state from live markers.
        List<TuioObject> onTable;
        lock (objectList) onTable = new List<TuioObject>(objectList.Values);
        onTable = onTable.FindAll(o => o.SymbolID != CircularMenuMarkerSymbolId && MuseumData.Figures.ContainsKey(o.SymbolID));

        if (onTable.Count == 0)
        {
            recognitionTimer.Stop();
            recognitionProgress = 0f;
            Transition(AppState.Idle, null, null, null, null);
            return;
        }

        if (pendingState == AppState.SingleFigure)
        {
            if (onTable.Count != 1)
            {
                recognitionTimer.Stop();
                recognitionProgress = 0f;
                Transition(AppState.Idle, null, null, null, null);
                return;
            }

            objA = onTable[0];
            objB = null;

            // Timer only runs while the figure remains in the centre zone.
            if (!IsInCenterZone(objA))
            {
                recognitionProgress = 0f;
                Invalidate();
                return;
            }
        }
        else
        {
            objA = onTable[0];
            objB = onTable.Count >= 2 ? onTable[1] : null;
        }

        recognitionProgress += 50f / RecognitionMs;
        if (recognitionProgress >= 1f)
        {
            recognitionProgress = 1f;
            recognitionTimer.Stop();
            if (pendingState == AppState.SingleFigure)
                Transition(pendingState, pendingFig, null, objA, objB);
            else
                Transition(pendingState, null, pendingRel, objA, objB);
            return;
        }

        Invalidate();
    }

    private void Transition(AppState ns, Figure fig, Relationship rel,
        TuioObject a, TuioObject b)
    {
        bool stateChanged = ns != state;
        bool figureChanged = fig != activeFig;
        bool relChanged = rel != activeRel;

        state = ns;
        activeFig = fig;
        activeRel = rel;
        objA = a;
        objB = b;

        switch (ns)
        {
            case AppState.Idle:
                if (stateChanged)
                {
                    slideShow.Stop();
                    currentSlide = null;
                    hoverObject = null;
                    activeObjectStory = null;
                    objectHoldProgress = 0f;
                }
                break;
            case AppState.Recognition:
                break;
            case AppState.SingleFigure:
                if (stateChanged || figureChanged)
                {
                    if (fig != null)
                        AddWatchedIfExists("figure:" + fig.SymbolId);

                    hoverObject = null;
                    activeObjectStory = null;
                    objectHoldProgress = 0f;

                    if (fig.SceneObjects != null && fig.SceneObjects.Count > 0)
                    {
                        if (!singleFigureIntroDone)
                            StartLockedSlideShow(fig.SoloSlides, SlideShowContext.SingleFigureIntro, "figure:" + fig.SymbolId);
                        else
                        {
                            slideShow.Stop();
                            currentSlide = null;
                        }
                    }
                    else
                    {
                        StartLockedSlideShow(fig.SoloSlides, SlideShowContext.SingleFigureIntro, "figure:" + fig.SymbolId);
                    }
                }
                break;
            case AppState.PairNotFacing:
                if (stateChanged)
                {
                    slideShow.Stop();
                    currentSlide = null;
                    hoverObject = null;
                    activeObjectStory = null;
                    objectHoldProgress = 0f;
                }
                break;
            case AppState.PairFacing:
                if ((stateChanged || relChanged) && rel != null)
                {
                    AddWatchedIfExists("relationship:" + rel.SymbolIdA + "_" + rel.SymbolIdB);
                    StartLockedSlideShow(rel.Slides, SlideShowContext.Relationship,
                        "relationship:" + rel.SymbolIdA + "_" + rel.SymbolIdB);
                }
                break;
        }
        Invalidate();
    }

    private static Relationship FindRelationship(int idA, int idB)
    {
        foreach (var r in MuseumData.Relationships)
            if ((r.SymbolIdA == idA && r.SymbolIdB == idB) ||
                (r.SymbolIdA == idB && r.SymbolIdB == idA))
                return r;
        return null;
    }

    private void StartLockedSlideShow(List<ContentSlide> slides, SlideShowContext context, string storyKey = null)
    {
        if (slides == null || slides.Count == 0) return;

        slideshowLocked = true;
        waitForClearAfterLockedShow = false;
        lockedContext = context;
        activeStoryKey = storyKey;
        slideElapsedMs = 0;
        slideShow.StartSlideShow(slides, true);
    }

    private void OnSlideShowCompleted()
    {
        slideshowLocked = false;
        slideElapsedMs = 0;
        currentSlide = null;
        hoverObject = null;
        objectHoldProgress = 0f;

        if (lockedContext == SlideShowContext.SingleFigureIntro)
        {
            singleFigureIntroDone = true;
            activeObjectStory = null;
            lockedContext = SlideShowContext.None;
            // Stay in SingleFigure so static object scene is shown next.
            Invalidate();
            return;
        }

        if (lockedContext == SlideShowContext.SceneObjectStory)
        {
            activeObjectStory = null;
            lockedContext = SlideShowContext.None;
            // Return to object selection scene in same figure mode.
            Invalidate();
            return;
        }

        if (lockedContext == SlideShowContext.MenuStory)
        {
            lockedContext = SlideShowContext.None;
            activeStoryKey = null;
            Transition(AppState.Idle, null, null, null, null);
            Invalidate();
            return;
        }

        // Relationship: keep previous behavior (finish then require clear once).
        waitForClearAfterLockedShow = true;
        lockedContext = SlideShowContext.None;
        activeStoryKey = null;
        activeObjectStory = null;
        Transition(AppState.Idle, null, null, null, null);
    }

    // Animation timer

    private void OnAnimTick(object sender, EventArgs e)
    {
        idlePhase = (idlePhase + 1.5f) % 360f;
        UpdateCircularMenuInput();

        if (slideShow != null && slideShow.IsRunning && currentSlide != null)
            slideElapsedMs += animTimer.Interval;

        if (state == AppState.SingleFigure)
            UpdateSingleFigureObjectSelection();

        if (fadingIn)
        {
            fadeAlpha = Math.Min(1f, fadeAlpha + 0.08f);
            if (fadeAlpha >= 1f) fadingIn = false;
        }

        if (state == AppState.Idle || state == AppState.Recognition ||
            state == AppState.SingleFigure || state == AppState.PairNotFacing || fadingIn || circularMenu.IsVisible || !isLoggedIn)
            Invalidate();
    }

    private void UpdateSingleFigureObjectSelection()
    {
        if (circularMenu.IsVisible) return;

        if (activeFig == null || objA == null) return;
        if (activeFig.SceneObjects == null || activeFig.SceneObjects.Count == 0) return;

        // Only allow object selection after intro story has completed.
        if (!singleFigureIntroDone)
        {
            hoverObject = null;
            objectHoldProgress = 0f;
            return;
        }

        // Ignore orientation-based triggers while any locked slideshow is playing.
        if (slideshowLocked)
        {
            hoverObject = null;
            objectHoldProgress = 0f;
            return;
        }

        SceneObject bestObj = null;
        float bestDiff = float.MaxValue;

        for (int i = 0; i < activeFig.SceneObjects.Count; i++)
        {
            SceneObject so = activeFig.SceneObjects[i];
            float dir = (float)Math.Atan2(so.Y - objA.Y, so.X - objA.X);
            float eff = objA.Angle + (activeFig != null ? activeFig.FacingAngleOffset : 0f);
            float diff = AbsAngleDiff(eff, dir);

            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestObj = so;
            }
        }

        if (bestObj != null && bestDiff <= ObjectFacingThresholdRad)
        {
            if (hoverObject != bestObj)
            {
                hoverObject = bestObj;
                objectHoldProgress = 0f;
            }
            else
            {
                objectHoldProgress = Math.Min(1f, objectHoldProgress + (animTimer.Interval / (float)ObjectHoldMs));
                if (objectHoldProgress >= 1f && activeObjectStory != bestObj)
                {
                    activeObjectStory = bestObj;
                    if (activeFig != null && bestObj != null)
                    {
                        string objectKey = "object:" + activeFig.SymbolId + ":" + bestObj.Name;
                        string objectTitle = "Object: " + activeFig.Name + " - " + bestObj.Name;
                        RegisterStory(objectKey, objectTitle, bestObj.StorySlides);
                        AddWatchedIfExists(objectKey);
                    }
                    if (bestObj.StorySlides != null && bestObj.StorySlides.Count > 0)
                        StartLockedSlideShow(bestObj.StorySlides, SlideShowContext.SceneObjectStory,
                            "object:" + activeFig.SymbolId + ":" + bestObj.Name);
                }
            }
        }
        else
        {
            hoverObject = null;
            objectHoldProgress = Math.Max(0f, objectHoldProgress - 0.1f);
        }
    }

    private void UpdateCircularMenuInput()
    {
        if (!isLoggedIn || authInProgress) return;

        TuioObject marker = GetSingleMenuMarker();

        // Open the menu immediately when marker ID 0 appears.
        if (!circularMenu.IsVisible && marker != null)
        {
            circularMenu.Show();
            menuGestureArmed = true;
            menuGestureAccumY = 0f;
            hasLastMenuMarkerY = true;
            lastMenuMarkerY = marker.Y;
        }

        // If menu was opened by marker control, hide when marker is removed.
        if (circularMenu.IsVisible && marker == null)
        {
            circularMenu.Hide();
            hasLastMenuMarkerY = false;
            menuGestureArmed = true;
            menuGestureAccumY = 0f;
            return;
        }

        if (!circularMenu.IsVisible) return;

        // Hide/show Favorite entry dynamically based on whether a figure context exists.
        circularMenu.ShowFavorite = !string.IsNullOrEmpty(GetCurrentFigureStoryKey());

        if (marker != null)
        {
            float a = marker.Angle;
            circularMenu.UpdateRotation(a);

            if (!hasLastMenuMarkerY)
            {
                hasLastMenuMarkerY = true;
                lastMenuMarkerY = marker.Y;
                menuGestureAccumY = 0f;
            }
            else
            {
                float frameDy = marker.Y - lastMenuMarkerY;
                menuGestureAccumY += frameDy;

                // Rearm gestures when marker returns near neutral vertical band.
                if (Math.Abs(marker.Y - 0.5f) <= MenuMoveNeutralBandY)
                {
                    menuGestureArmed = true;
                    menuGestureAccumY = 0f;
                }

                // upDelta is positive when marker moves UP (Y decreases towards top of screen).
                // downDelta is positive when marker moves DOWN (Y increases towards bottom of screen).
                float upDelta = MenuUpIsPositiveY ? (-menuGestureAccumY) : menuGestureAccumY;
                float downDelta = -upDelta;

                if (menuGestureArmed && upDelta >= MenuMoveTriggerDeltaY)
                {
                    circularMenu.MoveUpAction();
                    menuGestureArmed = false;
                    menuGestureAccumY = 0f;
                }
                else if (menuGestureArmed && downDelta >= MenuMoveTriggerDeltaY)
                {
                    circularMenu.MoveDownAction();
                    menuGestureArmed = false;
                    menuGestureAccumY = 0f;
                }

                lastMenuMarkerY = marker.Y;
            }
        }
        else
        {
            menuGestureArmed = true;
            menuGestureAccumY = 0f;
            hasLastMenuMarkerY = false;
        }
    }

    private TuioObject GetSingleMenuMarker()
    {
        List<TuioObject> onTable;
        lock (objectList) onTable = new List<TuioObject>(objectList.Values);
        onTable = onTable.FindAll(o => o.SymbolID == CircularMenuMarkerSymbolId);
        if (onTable.Count == 1) return onTable[0];
        return null;
    }

    private static float AbsAngleDiff(float a, float b)
    {
        float d = a - b;
        while (d > (float)Math.PI) d -= 2f * (float)Math.PI;
        while (d < -(float)Math.PI) d += 2f * (float)Math.PI;
        return Math.Abs(d);
    }

    // Rendering: top level

    protected override void OnPaintBackground(PaintEventArgs e) { /* suppress */ }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        Color bgColor = visitorProfile != null ? themePrimary : CBg;
        using (var bg = new SolidBrush(bgColor))
            g.FillRectangle(bg, 0, 0, W, H);

        if (!isLoggedIn || authInProgress)
        {
            DrawLoginScreen(g);
            return;
        }

        DrawStarField(g);

        switch (state)
        {
            case AppState.Idle: DrawIdle(g); break;
            case AppState.Recognition: DrawRecognition(g); break;
            case AppState.SingleFigure: DrawSingleFigure(g); break;
            case AppState.PairNotFacing: DrawPairNotFacing(g); break;
            case AppState.PairFacing: DrawPairFacing(g); break;
        }

        if (circularMenu.IsVisible)
            circularMenu.Draw(g, W, H, themeSecondary, themeTertiary, fontSubtitle, fontSmall);
    }

    private void DrawLoginScreen(Graphics g)
    {
        using (var veil = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
            g.FillRectangle(veil, 0, 0, W, H);

        DrawCentered(g, "LOGIN WITH FACE ID", fontTitle, themeSecondary,
            new RectangleF(0, H / 2f - 150, W, 66));

        DrawCentered(g, "Two Factor Authentication: Face ID + Bluetooth",
            fontSubtitle, Color.FromArgb(220, CPapyrus),
            new RectangleF(40, H / 2f - 78, W - 80, 40));

        DrawCentered(g, authStatus, fontBody, Color.White,
            new RectangleF(40, H / 2f - 8, W - 80, 44));

        DrawCentered(g, "Press L to retry login if needed", fontSmall, Color.FromArgb(190, CPapyrus),
            new RectangleF(40, H / 2f + 48, W - 80, 32));

        DrawOuterBorder(g);
    }

    // Rendering: idle

    private void DrawIdle(Graphics g)
    {
        DrawAnimatedSunRing(g, W / 2, H / 2, idlePhase);
        DrawEyeOfRa(g, W / 2, H / 2);

        DrawCentered(g, "SMART GRAND EGYPTIAN MUSEUM",
            fontTitle, CGold,
            new RectangleF(0, H / 2 - 168, W, 60));

        DrawCentered(g, "Interactive Tangible Table Experience",
            fontSubtitle, CPapyrus,
            new RectangleF(0, H / 2 - 98, W, 38));

        DrawGoldDivider(g, W / 2 - 260, H / 2 - 46, 520);

        DrawCentered(g,
            "Place a figure on the table to begin your journey through ancient Egypt",
            fontHint, Color.FromArgb(210, CPapyrus),
            new RectangleF(60, H / 2 + 20, W - 120, 38));

        DrawCentered(g,
            "Place two figures facing each other to discover their historical connection",
            fontHint, Color.FromArgb(145, CPapyrus),
            new RectangleF(60, H / 2 + 68, W - 120, 36));

        DrawOuterBorder(g);
    }

    // Rendering: recognition

    private void DrawRecognition(Graphics g)
    {
        // Dim background overlay
        using (var bg = new SolidBrush(Color.FromArgb(200, CBg)))
            g.FillRectangle(bg, 0, 0, W, H);

        bool singleFigureMode = objB == null;
        bool isSingleInCenter = singleFigureMode && objA != null && IsInCenterZone(objA);

        if (singleFigureMode)
            DrawCenterTarget(g, isSingleInCenter);

        // Draw markers on the table surface
        if (objA != null) DrawMarkerOnSurface(g, objA);
        if (objB != null) DrawMarkerOnSurface(g, objB);

        // Title
        string title = (objB == null)
            ? GetName(objA) + " detected"
            : GetName(objA) + "  &  " + GetName(objB) + " detected";
        DrawCentered(g, title.ToUpper(), fontTitle, CGold,
            new RectangleF(0, 24, W, 64));

        // Hint line
        string hint = (objB == null)
            ? (isSingleInCenter
                ? "Hold the figure in the center for 3 seconds to start"
                : "Move the figure to the center and hold for 3 seconds")
            : "Rotate the figures to face each other to discover their connection";
        DrawCentered(g, hint, fontHint, Color.FromArgb(210, CPapyrus),
            new RectangleF(60, 96, W - 120, 36));

        // Circular countdown bar at centre-bottom
        DrawCountdownArc(g, W / 2, H - 72, 38, recognitionProgress);

        DrawOuterBorder(g);
    }

    /// <summary>Maps TUIO normalised coords (0-1) to screen pixels and draws
    /// a circle + facing arrow for a single marker.</summary>
    private void DrawMarkerOnSurface(Graphics g, TuioObject obj)
    {
        int sx, sy;
        MapSurfacePoint(obj.X, obj.Y, out sx, out sy);

        Figure def = MuseumData.Figures.ContainsKey(obj.SymbolID)
            ? MuseumData.Figures[obj.SymbolID] : null;
        Color accent = def != null ? def.AccentColor : CGold;
        string name = def != null ? def.Name : ("ID " + obj.SymbolID);
        float effectiveAngle = obj.Angle + (def != null ? def.FacingAngleOffset : 0f);

        const int R = 40;
        Image markerImg = (def != null && !string.IsNullOrEmpty(def.MarkerImagePath))
            ? TryLoadImage(def.MarkerImagePath)
            : null;

        // Keep approximately same area as the old placeholder circle.
        if (markerImg != null)
        {
            GraphicsState gs = g.Save();
            g.TranslateTransform(sx, sy);
            g.RotateTransform((float)(effectiveAngle * 180.0 / Math.PI));
            g.DrawImage(markerImg, new Rectangle(-R, -R, R * 2, R * 2));
            g.Restore(gs);
        }
        else
        {
            // Fallback for figures that do not yet have marker images.
            using (var glow = new Pen(Color.FromArgb(55, accent), 12))
                g.DrawEllipse(glow, sx - R - 6, sy - R - 6, (R + 6) * 2, (R + 6) * 2);

            using (var p = new Pen(accent, 3))
                g.DrawEllipse(p, sx - R, sy - R, R * 2, R * 2);

            using (var fill = new SolidBrush(Color.FromArgb(80, accent)))
                g.FillEllipse(fill, sx - R, sy - R, R * 2, R * 2);

            DrawCentered(g, obj.SymbolID.ToString(), fontSmall, Color.White,
                new RectangleF(sx - R, sy - R, R * 2, R * 2));
        }

        // Facing direction arrow (thick, clearly visible)
        int arrowLen = R + 30;
        int ax = (int)(sx + Math.Cos(effectiveAngle) * arrowLen);
        int ay = (int)(sy + Math.Sin(effectiveAngle) * arrowLen);
        using (var arrowPen = new Pen(accent, 4) { EndCap = LineCap.ArrowAnchor })
            g.DrawLine(arrowPen, sx, sy, ax, ay);

        // Name label below
        DrawCentered(g, name, fontSubtitle, accent,
            new RectangleF(sx - 140, sy + R + 8, 280, 34));

        // Period label
        if (def != null)
            DrawCentered(g, def.Period, fontSmall, Color.FromArgb(190, CPapyrus),
                new RectangleF(sx - 140, sy + R + 42, 280, 22));
    }

    private void MapSurfacePoint(float nx, float ny, out int sx, out int sy)
    {
        int margin = 80;
        sx = margin + (int)(nx * (W - margin * 2));
        sy = margin + (int)(ny * (H - margin * 2));
    }

    private void DrawCountdownArc(Graphics g, int cx, int cy, int r, float progress)
    {
        // Background track
        using (var track = new Pen(Color.FromArgb(55, CGold), 6))
            g.DrawEllipse(track, cx - r, cy - r, r * 2, r * 2);

        // Progress arc (sweeps clockwise from top)
        if (progress > 0f)
        {
            using (var arc = new Pen(CGoldLight, 6)
            { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, cx - r, cy - r, r * 2, r * 2,
                          -90f, progress * 360f);
        }

        // Percentage text
        int pct = (int)(progress * 100);
        DrawCentered(g, pct + "%", fontSmall, CGoldLight,
            new RectangleF(cx - r, cy - r, r * 2, r * 2));
    }

    private bool IsInCenterZone(TuioObject obj)
    {
        if (obj == null) return false;
        return Math.Abs(obj.X - 0.5f) <= CenterZoneHalfWidth &&
               Math.Abs(obj.Y - 0.5f) <= CenterZoneHalfHeight;
    }

    private void DrawCenterTarget(Graphics g, bool active)
    {
        int cx = W / 2;
        int cy = H / 2;
        int r = 78;
        Color target = active ? Color.FromArgb(220, 80, 220, 120) : Color.FromArgb(170, 220, 190, 90);

        using (var p1 = new Pen(Color.FromArgb(120, target), 2f))
            g.DrawEllipse(p1, cx - r - 18, cy - r - 18, (r + 18) * 2, (r + 18) * 2);

        using (var p2 = new Pen(target, 3f))
            g.DrawEllipse(p2, cx - r, cy - r, r * 2, r * 2);

        using (var h = new Pen(Color.FromArgb(150, target), 2f))
        {
            g.DrawLine(h, cx - r - 28, cy, cx + r + 28, cy);
            g.DrawLine(h, cx, cy - r - 28, cx, cy + r + 28);
        }
    }

    private void DrawHeaderProgressBar(Graphics g, int x, int y, int width, int height, float progress, Color accent)
    {
        if (width <= 0 || height <= 0) return;
        progress = Math.Max(0f, Math.Min(1f, progress));

        using (Brush bg = new SolidBrush(Color.FromArgb(45, Color.White)))
            g.FillRectangle(bg, x, y, width, height);

        int fillW = (int)(width * progress);
        if (fillW > 0)
        {
            using (Brush fg = new SolidBrush(accent))
                g.FillRectangle(fg, x, y, fillW, height);
        }

        using (Pen border = new Pen(Color.FromArgb(130, Color.White), 1))
            g.DrawRectangle(border, x, y, width, height);
    }

    // Rendering: single figure

    private void DrawSingleFigure(Graphics g)
    {
        if (activeFig == null) return;
        Color accent = activeFig.AccentColor;

        int headerH = 95;
        DrawTopGradientBar(g, headerH,
            Color.FromArgb(180, ScaleBrightness(accent, 0.4f)));

        DrawCentered(g, activeFig.Name.ToUpper(), fontTitle, accent,
            new RectangleF(0, 8, W, 58));

        DrawCentered(g, activeFig.Period + "   \u00B7   " + activeFig.ShortDescription,
            fontSmall, Color.FromArgb(200, CPapyrus),
            new RectangleF(0, 68, W, 24));

        DrawHeaderSep(g, headerH + 6, accent);

        var contentArea = new Rectangle(50, headerH + 22, W - 100, H - headerH - 70);

        bool hasSceneObjects = activeFig.SceneObjects != null && activeFig.SceneObjects.Count > 0;
        bool showIntroOnly = hasSceneObjects && !singleFigureIntroDone;

        if (showIntroOnly)
        {
            if (currentSlide != null)
                DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
        }
        else if (hasSceneObjects)
        {
            bool isObjectStoryPlaying = lockedContext == SlideShowContext.SceneObjectStory && currentSlide != null;
            if (isObjectStoryPlaying)
            {
                // Match intro/relationship style: story slide only, no scene visible behind.
                DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
            }
            else
            {
                DrawSingleFigureObjectScene(g, contentArea, activeFig);
            }
        }
        else if (currentSlide != null)
        {
            DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
        }

        DrawProgressDots(g, slideShow.CurrentIndex, slideShow.TotalSlides,
                         W / 2, H - 26, accent);
        DrawOuterBorder(g);
    }

    private void DrawSingleFigureObjectScene(Graphics g, Rectangle area, Figure fig)
    {
        using (var veil = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            g.FillRectangle(veil, area);

        for (int i = 0; i < fig.SceneObjects.Count; i++)
            DrawSceneObject(g, fig.SceneObjects[i], fig.AccentColor);

        if (objA != null)
            DrawMarkerOnSurface(g, objA);

        string hint = "Rotate " + fig.Name + " toward an object and hold for 1.5 seconds";
        DrawCentered(g, hint, fontHint, Color.White,
            new RectangleF(area.X + 20, area.Bottom - 44, area.Width - 40, 28));
    }

    private void DrawSceneObject(Graphics g, SceneObject so, Color accent)
    {
        int sx, sy;
        MapSurfacePoint(so.X, so.Y, out sx, out sy);

        const int boxW = 150;
        const int boxH = 120;
        Rectangle box = new Rectangle(sx - boxW / 2, sy - boxH / 2, boxW, boxH);

        Image img = TryLoadImage(so.ImagePath);
        if (img != null)
        {
            Rectangle fit = FitRect(img.Width, img.Height, box);
            g.DrawImage(img, fit);
        }
        else
        {
            using (var miss = new SolidBrush(Color.FromArgb(120, 20, 20, 20)))
                g.FillRectangle(miss, box);
            DrawCentered(g, "[PNG]", fontSmall, Color.White,
                new RectangleF(box.X, box.Y + box.Height / 2f - 11, box.Width, 22));
        }

        bool isHover = (hoverObject == so);
        bool isActive = (activeObjectStory == so);
        Color frame = isActive ? CGreen : (isHover ? CGoldLight : accent);

        using (var pen = new Pen(frame, isHover || isActive ? 3 : 2))
            g.DrawRectangle(pen, box);

        DrawCentered(g, so.Name, fontSmall, Color.White,
            new RectangleF(box.X - 25, box.Bottom + 8, box.Width + 50, 22));

        if (isHover)
            DrawCountdownArc(g, sx, box.Bottom + 42, 14, objectHoldProgress);
    }

    private void DrawObjectSceneStoryOverlay(Graphics g, ContentSlide slide, Rectangle area, Color accent, float alpha)
    {
        int a = Clamp255(alpha * 255f);
        if (a == 0) return;

        Rectangle panel = new Rectangle(
            area.X + area.Width / 6,
            area.Y + area.Height / 7,
            area.Width * 4 / 6,
            area.Height * 5 / 7);

        using (var veil = new SolidBrush(Color.FromArgb(a * 180 / 255, 6, 6, 14)))
            g.FillRectangle(veil, panel);

        Color frame = (activeObjectStory != null) ? GetAccent(objA) : accent;
        using (var pen = new Pen(Color.FromArgb(a, frame), 3))
            g.DrawRectangle(pen, panel);

        DrawCornerAccents(g, panel, Color.FromArgb(a, frame), 26);

        Rectangle inner = new Rectangle(panel.X + 20, panel.Y + 22, panel.Width - 40, panel.Height - 60);

        if (slide.Type == ContentType.Image)
            DrawImageSlide(g, slide.Content, inner, a);
        else if (slide.Type == ContentType.Text)
            DrawTextSlide(g, slide.Content, inner, frame, a);
        else
            DrawVideoSlide(g, slide.Content, inner, frame, a);

        string title = (activeObjectStory != null) ? activeObjectStory.Name : "Object Story";
        DrawCentered(g, title, fontSmall, Color.White,
            new RectangleF(panel.X + 10, panel.Bottom - 30, panel.Width - 20, 22));
    }

    // Rendering: pair not facing

    private void DrawPairNotFacing(Graphics g)
    {
        if (objA == null || objB == null) return;

        string nameA = GetName(objA);
        string nameB = GetName(objB);
        Color accA = GetAccent(objA);
        Color accB = GetAccent(objB);

        DrawCentered(g, "TWO FIGURES DETECTED", fontTitle, CGold,
            new RectangleF(0, 28, W, 60));
        DrawCentered(g, nameA + "  &  " + nameB, fontSubtitle, CPapyrus,
            new RectangleF(0, 96, W, 38));
        DrawGoldDivider(g, W / 2 - 260, 144, 520);

        // Facing compasses
        int cy = H / 2 + 10;
        Figure defA = MuseumData.Figures.ContainsKey(objA.SymbolID)
            ? MuseumData.Figures[objA.SymbolID] : null;
        Figure defB = MuseumData.Figures.ContainsKey(objB.SymbolID)
            ? MuseumData.Figures[objB.SymbolID] : null;

        float devA = FacingDetector.FacingDeviation(objA, defA, objB);
        float devB = FacingDetector.FacingDeviation(objB, defB, objA);

        DrawFacingCompass(g, W / 2 - 185, cy,
            objA.Angle + (defA != null ? defA.FacingAngleOffset : 0f),
            devA, nameA, accA);
        DrawFacingCompass(g, W / 2 + 185, cy,
            objB.Angle + (defB != null ? defB.FacingAngleOffset : 0f),
            devB, nameB, accB);

        using (var pen = new Pen(Color.FromArgb(70, CGold), 1)
        { DashStyle = DashStyle.Dash })
            g.DrawLine(pen, W / 2 - 110, cy, W / 2 + 110, cy);

        string hint = activeRel != null
            ? "Rotate the figures so they face each other to uncover their historical connection!"
            : "These two figures share no direct historical connection in our records.";
        int hintAlpha = activeRel != null ? 210 : 140;
        DrawCentered(g, hint, fontHint, Color.FromArgb(hintAlpha, CPapyrus),
            new RectangleF(60, H - 96, W - 120, 56));

        if (activeRel != null)
            DrawCentered(g, "[ " + activeRel.ConnectionTitle + " ]",
                fontSmall, Color.FromArgb(150, CGold),
                new RectangleF(60, H - 48, W - 120, 28));

        DrawOuterBorder(g);
    }

    // Rendering: pair facing

    private void DrawPairFacing(Graphics g)
    {
        if (activeRel == null)
        {
            DrawCentered(g, GetName(objA) + "  &  " + GetName(objB),
                fontTitle, CGold, new RectangleF(0, 50, W, 60));
            DrawCentered(g,
                "These figures face each other, but no historical connection has been recorded.",
                fontBody, CPapyrus,
                new RectangleF(60, H / 2 - 30, W - 120, 60));
            DrawOuterBorder(g);
            return;
        }

        int headerH = 105;
        DrawTopGradientBar(g, headerH, Color.FromArgb(155, 80, 20, 0));

        // Shimmer sweep
        float shimX = (idlePhase / 360f) * (W + 200) - 100;
        using (var sh = new LinearGradientBrush(
            new PointF(shimX - 40, 0), new PointF(shimX + 40, 0),
            Color.Transparent, Color.FromArgb(50, Color.White)))
            g.FillRectangle(sh, 0, 0, W, headerH);

        DrawCentered(g, "\u2736   CONNECTION DISCOVERED   \u2736",
            fontTitle, CGoldLight,
            new RectangleF(0, 8, W, 58));
        DrawCentered(g, activeRel.ConnectionTitle,
            fontSubtitle, CPapyrus,
            new RectangleF(0, 68, W, 36));
        DrawHeaderSep(g, headerH + 6, CGold);

        var contentArea = new Rectangle(50, headerH + 22, W - 100, H - headerH - 70);
        if (currentSlide != null)
            DrawSlide(g, currentSlide, contentArea, CGold, fadeAlpha);

        DrawProgressDots(g, slideShow.CurrentIndex, slideShow.TotalSlides,
                         W / 2, H - 26, CGoldLight);
        DrawOuterBorder(g);
    }

    // Rendering: slide content

    private void DrawSlide(Graphics g, ContentSlide slide, Rectangle area,
        Color accent, float alpha)
    {
        int a = Clamp255(alpha * 255f);
        if (a == 0) return;

        switch (slide.Type)
        {
            case ContentType.Image: DrawImageSlide(g, slide.Content, area, a); break;
            case ContentType.Text: DrawTextSlide(g, slide.Content, area, accent, a); break;
            case ContentType.Video: DrawVideoSlide(g, slide.Content, area, accent, a); break;
        }
    }

    private void DrawImageSlide(Graphics g, string path, Rectangle area, int alpha)
    {
        Image img = TryLoadImage(path);
        if (img != null)
        {
            Rectangle dest = FitRect(img.Width, img.Height, area);
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = alpha / 255f;
                ia.SetColorMatrix(cm);
                g.DrawImage(img, dest, 0, 0, img.Width, img.Height,
                            GraphicsUnit.Pixel, ia);
            }
        }
        else
        {
            using (var pen = new Pen(Color.FromArgb(alpha, CGoldDim), 2))
                g.DrawRectangle(pen, area);
            DrawCentered(g,
                "[ Image: " + Path.GetFileName(path) + " ]",
                fontHint, Color.FromArgb(alpha, Color.FromArgb(160, CPapyrus)),
                new RectangleF(area.X, area.Y + area.Height / 2f - 18, area.Width, 36));
        }
    }

    private void DrawTextSlide(Graphics g, string text, Rectangle area,
        Color accent, int alpha)
    {
        int padX = Math.Min(60, area.Width / 8);
        var panel = new Rectangle(
            area.X + padX,
            area.Y + area.Height / 8,
            area.Width - padX * 2,
            area.Height * 6 / 8);

        // Solid dark background for maximum contrast
        using (var bg = new SolidBrush(Color.FromArgb(alpha * 230 / 255, 4, 4, 12)))
            g.FillRectangle(bg, panel);

        // Accent border — 2px, fully opaque at full alpha
        using (var pen = new Pen(Color.FromArgb(alpha, accent), 2))
            g.DrawRectangle(pen, panel);

        DrawCornerAccents(g, panel, Color.FromArgb(alpha, accent), 24);

        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.Word,
            FormatFlags = StringFormatFlags.LineLimit
        };

        int innerPad = 36;
        var textRect = new RectangleF(
            panel.X + innerPad, panel.Y + innerPad,
            panel.Width - innerPad * 2, panel.Height - innerPad * 2);

        // Shadow pass (offset 2px) for extra pop
        using (var shadow = new SolidBrush(Color.FromArgb(alpha * 180 / 255, 0, 0, 0)))
            g.DrawString(text, fontBody, shadow,
                new RectangleF(textRect.X + 2, textRect.Y + 2,
                               textRect.Width, textRect.Height), sf);

        // Main text — full white for maximum legibility
        using (var br = new SolidBrush(Color.FromArgb(alpha, Color.White)))
            g.DrawString(text, fontBody, br, textRect, sf);
    }

    private void DrawVideoSlide(Graphics g, string path, Rectangle area,
        Color accent, int alpha)
    {
        int padX = area.Width / 6;
        var panel = new Rectangle(
            area.X + padX, area.Y + area.Height / 5,
            area.Width - padX * 2, area.Height * 3 / 5);

        using (var bg = new SolidBrush(Color.FromArgb(alpha * 155 / 255, 0, 0, 0)))
            g.FillRectangle(bg, panel);
        using (var pen = new Pen(Color.FromArgb(alpha, accent), 2))
            g.DrawRectangle(pen, panel);

        int bx = panel.X + panel.Width / 2;
        int by = panel.Y + panel.Height / 2;
        DrawPlayTriangle(g, bx, by - 20, 36, Color.FromArgb(alpha, accent));
        DrawCentered(g, Path.GetFileNameWithoutExtension(path),
            fontSmall, Color.FromArgb(alpha, CPapyrus),
            new RectangleF(panel.X, by + 28, panel.Width, 24));
    }

    // Rendering: decorative helpers

    private void DrawAnimatedSunRing(Graphics g, int cx, int cy, float phase)
    {
        float pulse = 1f + 0.04f * (float)Math.Sin(phase * Math.PI / 180.0);
        int r1 = (int)(165 * pulse), r2 = (int)(205 * pulse), r3 = (int)(235 * pulse);

        using (var p = new Pen(Color.FromArgb(48, CGold), 2))
            g.DrawEllipse(p, cx - r1, cy - r1, r1 * 2, r1 * 2);
        using (var p = new Pen(Color.FromArgb(28, CGold), 1))
            g.DrawEllipse(p, cx - r2, cy - r2, r2 * 2, r2 * 2);
        using (var p = new Pen(Color.FromArgb(16, CGold), 1))
            g.DrawEllipse(p, cx - r3, cy - r3, r3 * 2, r3 * 2);

        for (int i = 0; i < 12; i++)
        {
            float a = (phase + i * 30f) * (float)(Math.PI / 180.0);
            int x1 = (int)(cx + Math.Cos(a) * (r1 + 6));
            int y1 = (int)(cy + Math.Sin(a) * (r1 + 6));
            int x2 = (int)(cx + Math.Cos(a) * r2);
            int y2 = (int)(cy + Math.Sin(a) * r2);
            using (var p = new Pen(Color.FromArgb(22, CGold), 1))
                g.DrawLine(p, x1, y1, x2, y2);
        }
    }

    private void DrawEyeOfRa(Graphics g, int cx, int cy)
    {
        using (var p = new Pen(Color.FromArgb(50, CGold), 2))
            g.DrawEllipse(p, cx - 26, cy - 14, 52, 28);
        using (var br = new SolidBrush(Color.FromArgb(32, CGold)))
            g.FillEllipse(br, cx - 9, cy - 9, 18, 18);
    }

    private void DrawFacingCompass(Graphics g, int cx, int cy,
        float angle, float deviation,
        string name, Color accent)
    {
        const int R = 68;
        float quality = Math.Max(0f, 1f - Math.Abs(deviation) / ((float)Math.PI / 4f));
        Color ringColor = InterpolateColor(CGold, CGreen, quality);

        using (var p = new Pen(ringColor, 3))
            g.DrawEllipse(p, cx - R, cy - R, R * 2, R * 2);

        // Target arc
        float idealAngle = angle - deviation;
        float startDeg = (float)(idealAngle * 180f / Math.PI) - 15f;
        using (var p = new Pen(Color.FromArgb(100, CGreen), 3)
        { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(p, cx - (R + 10), cy - (R + 10), (R + 10) * 2, (R + 10) * 2,
                      startDeg, 30f);

        // Current direction arrow
        int ax = (int)(cx + Math.Cos(angle) * (R - 10));
        int ay = (int)(cy + Math.Sin(angle) * (R - 10));
        using (var p = new Pen(Color.FromArgb(220, accent), 3) { EndCap = LineCap.ArrowAnchor })
            g.DrawLine(p, cx, cy, ax, ay);

        DrawCentered(g, name, fontSmall, accent,
            new RectangleF(cx - 135, cy + R + 8, 270, 24));

        string deg = ((int)(deviation * 180 / Math.PI)).ToString("+#;-#;0") + "\u00B0";
        DrawCentered(g, deg, fontSmall,
            Color.FromArgb(Clamp255(quality * 255f + 60), CGreen),
            new RectangleF(cx - 45, cy + R + 32, 90, 22));
    }

    private void DrawStarField(Graphics g)
    {
        using (var dim = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
        using (var bright = new SolidBrush(Color.FromArgb(105, 255, 255, 215)))
        {
            foreach (StarPoint star in stars)
            {
                int sx = star.X * W / 1280;
                int sy = star.Y * H / 720;
                SolidBrush br = (star.S > 1) ? bright : dim;
                g.FillEllipse(br, sx, sy, star.S, star.S);
            }
        }
    }

    private void DrawTopGradientBar(Graphics g, int height, Color top)
    {
        using (var br = new LinearGradientBrush(
            new Rectangle(0, 0, W, height), top, Color.Transparent,
            LinearGradientMode.Vertical))
            g.FillRectangle(br, 0, 0, W, height);
    }

    private void DrawHeaderSep(Graphics g, int y, Color color)
    {
        using (var p = new Pen(Color.FromArgb(175, color), 1))
            g.DrawLine(p, 50, y, W - 50, y);
        DrawDiamond(g, W / 2, y, 5, color);
    }

    private void DrawGoldDivider(Graphics g, int x, int y, int length)
    {
        using (var p = new Pen(CGold, 1))
            g.DrawLine(p, x, y, x + length, y);
        DrawDiamond(g, x + length / 2, y, 5, CGold);
    }

    private void DrawDiamond(Graphics g, int cx, int cy, int r, Color color)
    {
        Point[] pts = {
            new Point(cx,     cy - r),
            new Point(cx + r, cy),
            new Point(cx,     cy + r),
            new Point(cx - r, cy),
        };
        using (var br = new SolidBrush(color)) g.FillPolygon(br, pts);
    }

    private void DrawOuterBorder(Graphics g)
    {
        int m = 16;
        using (var p = new Pen(CGoldDim, 1))
        {
            g.DrawRectangle(p, m, m, W - m * 2, H - m * 2);
            g.DrawRectangle(p, m + 5, m + 5, W - m * 2 - 10, H - m * 2 - 10);
        }
        DrawCornerAccents(g, new Rectangle(m, m, W - m * 2, H - m * 2), CGoldDim, 28);
    }

    private void DrawCornerAccents(Graphics g, Rectangle r, Color color, int size)
    {
        using (var p = new Pen(color, 2))
        {
            g.DrawLine(p, r.Left, r.Top + size, r.Left, r.Top);
            g.DrawLine(p, r.Left, r.Top, r.Left + size, r.Top);
            g.DrawLine(p, r.Right - size, r.Top, r.Right, r.Top);
            g.DrawLine(p, r.Right, r.Top, r.Right, r.Top + size);
            g.DrawLine(p, r.Left, r.Bottom - size, r.Left, r.Bottom);
            g.DrawLine(p, r.Left, r.Bottom, r.Left + size, r.Bottom);
            g.DrawLine(p, r.Right - size, r.Bottom, r.Right, r.Bottom);
            g.DrawLine(p, r.Right, r.Bottom - size, r.Right, r.Bottom);
        }
    }

    private void DrawProgressDots(Graphics g, int current, int total,
        int cx, int cy, Color color)
    {
        if (total <= 0) return;
        const int dotR = 5, spacing = 18;
        int startX = cx - (total - 1) * spacing / 2;
        for (int i = 0; i < total; i++)
        {
            using (var br = new SolidBrush(i == current
                ? color : Color.FromArgb(55, color)))
                g.FillEllipse(br, startX + i * spacing - dotR,
                              cy - dotR, dotR * 2, dotR * 2);
        }
    }

    private void DrawPlayTriangle(Graphics g, int cx, int cy, int r, Color color)
    {
        Point[] pts = {
            new Point(cx - r / 2, cy - r),
            new Point(cx + r,     cy),
            new Point(cx - r / 2, cy + r),
        };
        using (var p = new Pen(color, 3)) g.DrawPolygon(p, pts);
    }

    // Image loading with cache

    private Image TryLoadImage(string relativePath)
    {
        Image cached;
        if (imgCache.TryGetValue(relativePath, out cached)) return cached;

        string full = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
        Image img = null;
        if (File.Exists(full))
        {
            try { img = Image.FromFile(full); }
            catch { }
        }
        imgCache[relativePath] = img;
        return img;
    }

    // Utility helpers

    private void DrawCentered(Graphics g, string text, Font font, Color color,
        RectangleF bounds)
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

    private static Rectangle FitRect(int srcW, int srcH, Rectangle area)
    {
        float a = (float)srcW / srcH, aa = (float)area.Width / area.Height;
        int w, h;
        if (a > aa) { w = area.Width; h = (int)(area.Width / a); }
        else { h = area.Height; w = (int)(area.Height * a); }
        return new Rectangle(area.X + (area.Width - w) / 2,
                             area.Y + (area.Height - h) / 2, w, h);
    }

    private static Color ScaleBrightness(Color c, float s)
    {
        return Color.FromArgb((int)(c.R * s), (int)(c.G * s), (int)(c.B * s));
    }

    private static Color InterpolateColor(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static int Clamp255(float v)
    {
        if (v < 0) return 0;
        if (v > 255) return 255;
        return (int)v;
    }

    private string GetName(TuioObject o)
    {
        if (o != null && o.SymbolID == CircularMenuMarkerSymbolId)
            return "Menu Marker";

        if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
            return MuseumData.Figures[o.SymbolID].Name;
        return "Unknown";
    }

    private Color GetAccent(TuioObject o)
    {
        if (o != null && o.SymbolID == CircularMenuMarkerSymbolId)
            return Color.FromArgb(120, 180, 240);

        if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
            return MuseumData.Figures[o.SymbolID].AccentColor;
        return CGold;
    }

    private void SafeInvalidate()
    {
        if (IsHandleCreated) this.BeginInvoke(new Action(this.Invalidate));
    }

    // Resize

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            W = this.ClientSize.Width;
            H = this.ClientSize.Height;
        }
    }

    // Keyboard and form events

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F1)
        {
            if (!fullscreen)
            {
                winW = W; winH = H;
                winLeft = this.Left; winTop = this.Top;
                W = Screen.PrimaryScreen.Bounds.Width;
                H = Screen.PrimaryScreen.Bounds.Height;
                this.FormBorderStyle = FormBorderStyle.None;
                this.Left = 0; this.Top = 0;
                this.Width = W; this.Height = H;
                fullscreen = true;
            }
            else
            {
                W = winW; H = winH;
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.Left = winLeft; this.Top = winTop;
                this.Width = W + 16; this.Height = H + 39;
                fullscreen = false;
            }
        }
        else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Q)
        {
            this.Close();
        }
        else if (e.KeyCode == Keys.L)
        {
            if (!authInProgress)
                StartLoginFlow();
        }
        else if (e.KeyCode == Keys.M)
        {
            if (isLoggedIn)
            {
                if (circularMenu.IsVisible) circularMenu.Hide();
                else circularMenu.Show();
            }
        }
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        animTimer.Stop();
        recognitionTimer.Stop();
        slideShow.Stop();
        if (client != null)
        {
            client.removeTuioListener(this);
            client.disconnect();
        }
        Environment.Exit(0);
    }

    // Entry point

    [STAThread]
    static void Main(string[] args)
    {
        int port = 3333;
        int p;
        if (args.Length >= 1 && int.TryParse(args[0], out p)) port = p;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TuioDemo(port));
    }
}


