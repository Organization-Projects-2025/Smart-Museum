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
using System.Threading.Tasks;
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
    private HashSet<int> recognizedFigureIds = new HashSet<int>();
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

    private enum LoginAuthPhase
    {
        /// <summary>Initial / restart screen: shows "look at the camera" while RegisterFaceScan auto-runs.</summary>
        MainPicker,
        LoginScanning,
        RegisterScanning,
        /// <summary>Returning user: show CSV profile on C# surface, then Bluetooth.</summary>
        ProfileWelcome,
        RegisterDuplicateChoice,
        RegisterBluetoothScanning,
        RegisterBluetoothConfirm,
        /// <summary>Face was not detected. Ring offers Try again / Exit.</summary>
        NoFaceRecovery,
        /// <summary>Webcam / server error (not “no face in oval”). Same ring as NoFaceRecovery.</summary>
        AuthCameraIssue,
        /// <summary>Login or duplicate-login: Bluetooth failed; user can retry or restart face scan.</summary>
        LoginBluetoothRecovery,
        /// <summary>Registration: Bluetooth device scan failed; user can retry or restart face scan.</summary>
        RegisterBluetoothRecovery,
        RegisterEnterFirstName,
        RegisterEnterLastName,
        RegisterEnterAge,
        RegisterGenderPick,
        RegisterRolePick,
        RegisterSaving
    }

    private LoginAuthPhase loginPhase = LoginAuthPhase.MainPicker;
    private List<string> authRingItems = new List<string>();
    private int authRingSelectedIndex;
    private bool authRingGestureArmed = true;
    private bool authRingHasLastY;
    private float authRingLastY;
    private float authRingAccumY;
    /// <summary>Latest menu-marker rotation in degrees (TUIO), for on-screen debugging.</summary>
    private float authTuioMarkerAngleDeg;
    private bool authTuioMarkerTracked;

    private string pendingDuplicateUserId;
    private string pendingNewFaceUserId;
    private string pendingBtDeviceName;
    private string pendingBtMac;
    private string pendingRegisterRole = "visitor";

    /// <summary>Face ID succeeded but Bluetooth verify failed — retry uses this profile.</summary>
    private VisitorProfile pendingLoginBluetoothUser;
    private bool pendingLoginBluetoothFromDuplicate;

    /// <summary>Loaded after face lobby match — shown on Tuio auth screen (museum profile panel).</summary>
    private VisitorProfile authLobbyProfilePreview;

    /// <summary>After a successful face match, advance to Bluetooth without TUIO when this time elapses.</summary>
    private DateTime? profileWelcomeAutoContinueUtc;

    private int loginBluetoothFailureCount;
    private DateTime loginBtCooldownUntilUtc;
    private bool loginBluetoothRecoveryEscalated;

    private string nameEntryBuffer = "";
    private string regPendingFirstName;
    private string regPendingLastName;
    private int regPendingAge;
    private string regPendingGender;

    private const int RegMaxNameChars = 40;
    private const int RegMinAge = 1;
    private const int RegMaxAge = 120;

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
    private bool menuOpenedByGesture = false; // Track if menu was opened by hand gesture vs marker
    /// <summary>Flick baseline in normalized TUIO space (0–1). Confirm = displacement from here.</summary>
    private float menuFlickArmX = 0.5f, menuFlickArmY = 0.5f;
    private DateTime lastMenuFlickActionUtc = DateTime.MinValue;
    private bool menuFlickNeedsResync = true;
    private const int MenuFlickCooldownMs = 300;
    /// <summary>Min |Δy| from arm to confirm (back uses +Δy).</summary>
    private const float MenuFlickPullY = 0.038f;
    /// <summary>Diagonal flick: total drag from arm must exceed this and include vertical bias.</summary>
    private const float MenuFlickDiagonalDist = 0.065f;
    private const float MenuFlickDiagonalBiasY = 0.016f;
    /// <summary>Set true if your TUIO Y axis is inverted vs screen top/bottom.</summary>
    private const bool CircularMenuTuioInvertY = false;

    // Login / auth ring: vertical flick on same TUIO marker (unchanged)
    private const bool MenuUpIsPositiveY = true;
    private const float MenuMoveTriggerDeltaY = 0.035f;
    private const float MenuMoveNeutralBandY = 0.015f;

    // Gesture recognition for circular menu
    private GestureClient gestureClient;
    private System.Windows.Forms.Timer gestureCheckTimer;
    private bool isGestureActive = false;

    // Input prioritization: blocks gestures when TUIOs present or during cooldown
    private InputPrioritizer inputPrioritizer = new InputPrioritizer();

    // 3-D hand tracking client (port 5002)
    private HandTrackClient handTrackClient;
    private float _idleRotY = 0f;      // auto-spin Y angle when no hand is present

    private GazeEmotionClient gazeEmotionClient;
    private readonly object liveGazeLock = new object();
    private bool liveGazeStreamActive;
    private bool liveGazeValid;
    private double liveGazeGx = 0.5, liveGazeGy = 0.5;
    private string liveDominantEmotion = "neutral";
    private string liveGazeIssueUserText = "";
    private YoloContextClient yoloContextClient;
    private readonly SessionAnalyticsRecorder analyticsRecorder = new SessionAnalyticsRecorder();
    private AdminAnalyticsPanel adminAnalyticsPanel;
    private bool adminAnalyticsVisible;

    // YOLO: cell phone visible → bottom banner inviting download of the museum companion app.
    // Replace URLs before production. Hysteresis reduces flicker when detection jitters.
    private const string MuseumAppStoreIosUrl = "https://apps.apple.com/app/id0000000000";
    private const string MuseumAppStoreAndroidUrl = "https://play.google.com/store/apps/details?id=com.example.smartmuseum";
    private int _yoloPhonePresenceCounter;
    private bool _showMuseumAppPhoneBanner;
    private const int YoloPhoneBannerOnFrames = 2;

    // Gesture overlay display
    private string lastDetectedGesture = null;
    private DateTime gestureDisplayTime = DateTime.MinValue;
    private const double GESTURE_DISPLAY_DURATION = 2.0; // seconds

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
        this.MinimumSize = new Size(960, 600);
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
            if (slide != null && analyticsRecorder != null && visitorProfile != null)
            {
                string title = "";
                if (!string.IsNullOrEmpty(activeStoryKey) && storyTitleByKey.ContainsKey(activeStoryKey))
                    title = storyTitleByKey[activeStoryKey];
                analyticsRecorder.NotifySlideChanged(activeStoryKey, title, slideShow.CurrentIndex, slide);
            }
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
        InitializeGestureClient();
        InitializeHandTracker();
        StartLoginFlow();
    }

    private void ShowAuthPicker()
    {
        loginPhase = LoginAuthPhase.MainPicker;
        authRingItems = new List<string>();
        authRingSelectedIndex = 0;
        pendingDuplicateUserId = null;
        pendingNewFaceUserId = null;
        pendingLoginBluetoothUser = null;
        pendingLoginBluetoothFromDuplicate = false;
        authLobbyProfilePreview = null;
        loginBluetoothFailureCount = 0;
        loginBtCooldownUntilUtc = DateTime.MinValue;
        loginBluetoothRecoveryEscalated = false;
        pendingBtDeviceName = null;
        pendingBtMac = null;
        pendingRegisterRole = "visitor";
        nameEntryBuffer = "";
        regPendingFirstName = null;
        regPendingLastName = null;
        regPendingAge = 0;
        regPendingGender = null;
        authRingGestureArmed = true;
        authRingHasLastY = false;
        authRingAccumY = 0f;
        // True immediately so gesture_service cannot grab the webcam before the face thread runs
        // (avoids NOT_FOUND / “NO FACE FOUND” when using museum_vision_server shared camera).
        authInProgress = true;
        profileWelcomeAutoContinueUtc = null;
        authStatus = "We’ll open a short camera check on this computer. Fit your face in the gold frame — when you’re done, your name appears here on the table.";
        SafeInvalidate();
        RunRegisterFaceScanThread();
    }

    private void ShowNoFaceRecovery(string message)
    {
        Action apply = () =>
        {
            loginPhase = LoginAuthPhase.NoFaceRecovery;
            authRingItems = new List<string> { "Try again", "Exit" };
            authRingSelectedIndex = 0;
            authRingGestureArmed = true;
            authRingHasLastY = false;
            authRingAccumY = 0f;
            authInProgress = false;
            authStatus = message;
            Invalidate();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => TryReleaseGestureWebcamBlocking());
        };
        if (IsHandleCreated) BeginInvoke(apply);
        else { apply(); SafeInvalidate(); }
    }

    private void ShowAuthCameraIssueRecovery(string message)
    {
        Action apply = () =>
        {
            loginPhase = LoginAuthPhase.AuthCameraIssue;
            authRingItems = new List<string> { "Try again", "Exit" };
            authRingSelectedIndex = 0;
            authRingGestureArmed = true;
            authRingHasLastY = false;
            authRingAccumY = 0f;
            authInProgress = false;
            authStatus = message;
            Invalidate();
            System.Threading.ThreadPool.QueueUserWorkItem(_ => TryReleaseGestureWebcamBlocking());
        };
        if (IsHandleCreated) BeginInvoke(apply);
        else { apply(); SafeInvalidate(); }
    }

    private void StartLoginFlow()
    {
        if (isLoggedIn && analyticsRecorder != null)
            analyticsRecorder.FlushAndSave();
        TeardownGazeAnalytics();

        isLoggedIn = false;
        visitorProfile = null;
        ShowAuthPicker();
    }

    private void RunRegisterFaceScanThread()
    {
        authInProgress = true;
        loginPhase = LoginAuthPhase.RegisterScanning;
        authStatus = "Opening the camera window… If you don’t see it, look for “Face sign-in” on the Windows taskbar.";
        SafeInvalidate();

        Thread t = new Thread(() =>
        {
            try
            {
                TryReleaseGestureWebcamBlocking();
                var faceService = new FaceRecognitionService();
                FaceRegisterScanResult outcome;
                string uid;
                string st;
                bool ok = faceService.AuthLobbyScan(out outcome, out uid, out st);
                authStatus = st;
                SafeInvalidate();

                if (!ok && outcome == FaceRegisterScanResult.Error)
                {
                    ShowAuthCameraIssueRecovery(string.IsNullOrEmpty(st)
                        ? "Check that python_server.py is running on this PC, then try again."
                        : st);
                    return;
                }

                if (outcome == FaceRegisterScanResult.NoFace)
                {
                    ShowNoFaceRecovery("We couldn’t see your face clearly. Step a little closer, face the light, and try again.");
                    return;
                }

                if (outcome == FaceRegisterScanResult.MatchedExisting)
                {
                    pendingDuplicateUserId = uid;
                    VisitorProfile preview = null;
                    TryLoadVisitorProfile(uid, out preview);
                    if (IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            loginBluetoothFailureCount = 0;
                            loginBluetoothRecoveryEscalated = false;
                            loginBtCooldownUntilUtc = DateTime.MinValue;
                            authLobbyProfilePreview = preview;
                            authInProgress = false;
                            loginPhase = LoginAuthPhase.ProfileWelcome;
                            authRingItems = new List<string>();
                            authRingSelectedIndex = 0;
                            authRingGestureArmed = true;
                            authRingHasLastY = false;
                            authRingAccumY = 0f;
                            profileWelcomeAutoContinueUtc = DateTime.UtcNow.AddSeconds(4);
                            authStatus = preview != null
                                ? "You’re in — here’s the profile we keep for your visits. We’ll verify your phone next; this step continues on its own in a few seconds."
                                : "You’re in — we matched your account. We’ll verify your phone next; this continues on its own in a few seconds.";
                            Invalidate();
                        }));
                    }
                    return;
                }

                if (outcome == FaceRegisterScanResult.NewUserCreated)
                {
                    pendingNewFaceUserId = uid;
                    if (IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            authInProgress = false;
                            authStatus = "Almost there — we’ll look for your phone or watch on Bluetooth to finish creating your account.";
                            Invalidate();
                            StartBluetoothPickForRegistration();
                        }));
                    }
                    return;
                }

                ShowNoFaceRecovery("Something unexpected happened during sign-in. Please try again.");
            }
            catch (Exception ex)
            {
                ShowNoFaceRecovery("We couldn’t finish the camera step: " + ex.Message);
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlowRegisterFace";
        t.Start();
    }

    private void StartBluetoothPickForRegistration()
    {
        authInProgress = true;
        loginPhase = LoginAuthPhase.RegisterBluetoothScanning;
        authStatus = "Turn on Bluetooth on your phone and make it discoverable. Scanning for nearby devices…";
        Invalidate();

        Thread t = new Thread(() =>
        {
            try
            {
                var bt = new BluetoothService();
                string name, mac, st;
                bool ok = bt.TryPickRegistrationDevice(out name, out mac, out st);
                authStatus = st;
                SafeInvalidate();

                if (!ok || string.IsNullOrEmpty(mac))
                {
                    authInProgress = false;
                    string displayStatus = BluetoothService.FriendlyBluetoothError(st);
                    if (IsHandleCreated)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            loginPhase = LoginAuthPhase.RegisterBluetoothRecovery;
                            authRingItems = new List<string> { "Try again", "Restart scan" };
                            authRingSelectedIndex = 0;
                            authRingGestureArmed = true;
                            authRingHasLastY = false;
                            authRingAccumY = 0f;
                            authStatus = displayStatus;
                            Invalidate();
                        }));
                    }
                    else
                    {
                        loginPhase = LoginAuthPhase.RegisterBluetoothRecovery;
                        authRingItems = new List<string> { "Try again", "Restart scan" };
                        authRingSelectedIndex = 0;
                        authStatus = displayStatus;
                    }
                    SafeInvalidate();
                    return;
                }

                pendingBtDeviceName = name;
                pendingBtMac = mac;
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        loginPhase = LoginAuthPhase.RegisterBluetoothConfirm;
                        authRingItems = new List<string> { "Use this device", "Scan again" };
                        authRingSelectedIndex = 0;
                        authRingGestureArmed = true;
                        authRingHasLastY = false;
                        authRingAccumY = 0f;
                        authInProgress = false;
                        authStatus = "Bluetooth: " + name + " — " + mac + ". Confirm or scan again.";
                        Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                authInProgress = false;
                string displayStatus = BluetoothService.FriendlyBluetoothError(ex.Message);
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        loginPhase = LoginAuthPhase.RegisterBluetoothRecovery;
                        authRingItems = new List<string> { "Try again", "Restart scan" };
                        authRingSelectedIndex = 0;
                        authRingGestureArmed = true;
                        authRingHasLastY = false;
                        authRingAccumY = 0f;
                        authStatus = displayStatus;
                        Invalidate();
                    }));
                }
                else
                {
                    loginPhase = LoginAuthPhase.RegisterBluetoothRecovery;
                    authRingItems = new List<string> { "Try again", "Restart scan" };
                    authRingSelectedIndex = 0;
                    authStatus = displayStatus;
                }
                SafeInvalidate();
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlowRegBt";
        t.Start();
    }

    private void RunDuplicateUserLoginThread()
    {
        if (string.IsNullOrEmpty(pendingDuplicateUserId)) return;

        profileWelcomeAutoContinueUtc = null;
        authInProgress = true;
        loginPhase = LoginAuthPhase.LoginScanning;
        authStatus = "Connecting you — pairing your phone next…";
        SafeInvalidate();

        Thread t = new Thread(() =>
        {
            try
            {
                string workspaceRoot = GetWorkspaceRoot();
                string csvPath = Path.Combine(workspaceRoot, "C#", "content", "auth", "users.csv");
                List<VisitorProfile> users = VisitorProfile.LoadFromCsv(csvPath);
                VisitorProfile selected = users.Find(u =>
                    string.Equals(u.FaceUserId, pendingDuplicateUserId, StringComparison.OrdinalIgnoreCase));

                if (selected == null)
                {
                    authStatus = "We couldn’t load your saved visitor details. Please start sign-in again from the camera step.";
                    authInProgress = false;
                    loginPhase = LoginAuthPhase.MainPicker;
                    SafeInvalidate();
                    return;
                }

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        authStatus = "Hi, " + selected.FullName + " — please leave Bluetooth on and make your phone or watch discoverable for a moment.";
                        Invalidate();
                    }));
                }
                Thread.Sleep(2200);

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        authStatus = "Looking for your registered device… this can take a few seconds.";
                        Invalidate();
                    }));
                }
                Thread.Sleep(400);

                var btService = new BluetoothService();
                string btStatus;
                bool btOk = btService.Verify(selected.BluetoothMacAddress, out btStatus);
                authStatus = btStatus;
                SafeInvalidate();

                if (!btOk)
                {
                    pendingLoginBluetoothUser = selected;
                    pendingLoginBluetoothFromDuplicate = true;
                    authInProgress = false;
                    if (IsHandleCreated)
                        BeginInvoke(new Action(() => ShowLoginBluetoothFailureState(btStatus)));
                    else
                        ShowLoginBluetoothFailureState(btStatus);
                    SafeInvalidate();
                    return;
                }

                visitorProfile = selected;
                authStatus = "Welcome, " + visitorProfile.FullName + ". Your visit will use " + visitorProfile.Language + ".";

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        loginBluetoothFailureCount = 0;
                        loginBluetoothRecoveryEscalated = false;
                        loginBtCooldownUntilUtc = DateTime.MinValue;
                        ApplyVisitorTheme();
                        ConfigureCircularMenuForUser();
                        authInProgress = false;
                        loginPhase = LoginAuthPhase.MainPicker;
                        isLoggedIn = true;
                        pendingDuplicateUserId = null;
                        pendingLoginBluetoothUser = null;
                        pendingLoginBluetoothFromDuplicate = false;
                        Transition(AppState.Idle, null, null, null, null);
                        InitializeGazeAnalytics();
                        InitializeYoloContext();
                        Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                authStatus = BluetoothService.FriendlyBluetoothError(ex.Message);
                authInProgress = false;
                loginPhase = LoginAuthPhase.MainPicker;
                SafeInvalidate();
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlowDupLogin";
        t.Start();
    }

    /// <summary>UI thread: first Bluetooth failure offers retry after cooldown; second failure offers restart or guest.</summary>
    private void ShowLoginBluetoothFailureState(string bluetoothDetail)
    {
        loginBluetoothFailureCount++;
        if (loginBluetoothFailureCount >= 2)
        {
            loginBluetoothRecoveryEscalated = true;
            loginBtCooldownUntilUtc = DateTime.MinValue;
            authRingItems = new List<string> { "Restart login", "Guest visitor" };
            authStatus = "Bluetooth still could not verify your device after two tries. You can return to the camera screen, or continue as a guest (no Bluetooth check) with a dedicated guest look.";
        }
        else
        {
            loginBluetoothRecoveryEscalated = false;
            loginBtCooldownUntilUtc = DateTime.UtcNow.AddSeconds(5);
            authRingItems = new List<string> { "Try again", "Restart scan" };
            string friendly = BluetoothService.FriendlyBluetoothError(bluetoothDetail);
            authStatus = "Bluetooth verification did not succeed. " + friendly + " Please wait 5 seconds, then choose Try again.";
        }
        loginPhase = LoginAuthPhase.LoginBluetoothRecovery;
        authRingSelectedIndex = 0;
        authRingGestureArmed = true;
        authRingHasLastY = false;
        authRingAccumY = 0f;
        Invalidate();
    }

    private void LoginGuestAndEnter()
    {
        visitorProfile = VisitorProfile.CreateGuestVisitor();
        ApplyVisitorTheme();
        ConfigureCircularMenuForUser();
        authInProgress = false;
        loginPhase = LoginAuthPhase.MainPicker;
        isLoggedIn = true;
        pendingDuplicateUserId = null;
        pendingLoginBluetoothUser = null;
        pendingLoginBluetoothFromDuplicate = false;
        loginBluetoothFailureCount = 0;
        loginBluetoothRecoveryEscalated = false;
        loginBtCooldownUntilUtc = DateTime.MinValue;
        authStatus = "You are visiting as a guest — enjoy the table experience.";
        Transition(AppState.Idle, null, null, null, null);
        InitializeGazeAnalytics();
        InitializeYoloContext();
        Invalidate();
    }

    /// <summary>After face ID, retry Bluetooth 2FA only (same user as last failure).</summary>
    private void RunLoginBluetoothRetryThread()
    {
        if (pendingLoginBluetoothUser == null) return;

        authInProgress = true;
        loginPhase = LoginAuthPhase.LoginScanning;
        authStatus = "Waiting before the next Bluetooth check…";
        SafeInvalidate();

        VisitorProfile user = pendingLoginBluetoothUser;
        bool fromDuplicateFlow = pendingLoginBluetoothFromDuplicate;

        Thread t = new Thread(() =>
        {
            try
            {
                double waitMs = (loginBtCooldownUntilUtc - DateTime.UtcNow).TotalMilliseconds;
                if (!loginBluetoothRecoveryEscalated && waitMs > 0)
                {
                    int sleepMs = (int)Math.Min(waitMs, 120000);
                    if (sleepMs > 0)
                        Thread.Sleep(sleepMs);
                }

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        authStatus = "Searching again for your Bluetooth device…";
                        Invalidate();
                    }));
                }

                var btService = new BluetoothService();
                string btStatus;
                bool btOk = btService.Verify(user.BluetoothMacAddress, out btStatus);
                authStatus = btStatus;
                SafeInvalidate();

                if (!btOk)
                {
                    authInProgress = false;
                    if (IsHandleCreated)
                        BeginInvoke(new Action(() => ShowLoginBluetoothFailureState(btStatus)));
                    else
                        ShowLoginBluetoothFailureState(btStatus);
                    SafeInvalidate();
                    return;
                }

                visitorProfile = user;
                authStatus = "Welcome, " + visitorProfile.FullName + ". Your experience will be in " + visitorProfile.Language + ".";

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        loginBluetoothFailureCount = 0;
                        loginBluetoothRecoveryEscalated = false;
                        loginBtCooldownUntilUtc = DateTime.MinValue;
                        ApplyVisitorTheme();
                        ConfigureCircularMenuForUser();
                        authInProgress = false;
                        loginPhase = LoginAuthPhase.MainPicker;
                        isLoggedIn = true;
                        pendingLoginBluetoothUser = null;
                        pendingLoginBluetoothFromDuplicate = false;
                        if (fromDuplicateFlow)
                            pendingDuplicateUserId = null;
                        Transition(AppState.Idle, null, null, null, null);
                        InitializeGazeAnalytics();
                        InitializeYoloContext();
                        Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                string msg = BluetoothService.FriendlyBluetoothError(ex.Message);
                authStatus = msg;
                authInProgress = false;
                if (IsHandleCreated)
                    BeginInvoke(new Action(() => ShowLoginBluetoothFailureState(msg)));
                else
                    ShowLoginBluetoothFailureState(msg);
                SafeInvalidate();
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlowBtRetry";
        t.Start();
    }

    private void RunCompleteRegistrationThread()
    {
        authInProgress = true;
        loginPhase = LoginAuthPhase.RegisterSaving;
        authStatus = "Saving your account...";
        SafeInvalidate();

        Thread t = new Thread(() =>
        {
            try
            {
                string workspaceRoot = GetWorkspaceRoot();
                string csvPath = Path.Combine(workspaceRoot, "C#", "content", "auth", "users.csv");

                string fn = string.IsNullOrWhiteSpace(regPendingFirstName) ? "New" : regPendingFirstName.Trim();
                string ln = string.IsNullOrWhiteSpace(regPendingLastName) ? "User" : regPendingLastName.Trim();
                int age = regPendingAge >= RegMinAge && regPendingAge <= RegMaxAge ? regPendingAge : 22;

                var profile = new VisitorProfile
                {
                    FaceUserId = pendingNewFaceUserId,
                    FirstName = AuthCsvStore.SanitizeField(fn),
                    LastName = AuthCsvStore.SanitizeField(ln),
                    Age = age,
                    Gender = NormalizeRegGender(regPendingGender),
                    Race = "other",
                    BluetoothMacAddress = pendingBtMac ?? "0",
                    FaceImagePath = "python/data/faces/" + pendingNewFaceUserId + ".jpg",
                    Role = string.IsNullOrEmpty(pendingRegisterRole) ? "visitor" : pendingRegisterRole
                };
                profile.ApplyDerivedPreferences();

                if (!AuthCsvStore.AppendUser(csvPath, profile))
                {
                    authStatus = "Could not write users.csv.";
                    authInProgress = false;
                    loginPhase = LoginAuthPhase.MainPicker;
                    SafeInvalidate();
                    return;
                }

                visitorProfile = profile;
                authStatus = "Welcome " + visitorProfile.FullName + " (" + visitorProfile.Language + ")";

                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        ApplyVisitorTheme();
                        ConfigureCircularMenuForUser();
                        authInProgress = false;
                        loginPhase = LoginAuthPhase.MainPicker;
                        isLoggedIn = true;
                        pendingNewFaceUserId = null;
                        pendingBtMac = null;
                        regPendingFirstName = null;
                        regPendingLastName = null;
                        regPendingAge = 0;
                        regPendingGender = null;
                        nameEntryBuffer = "";
                        Transition(AppState.Idle, null, null, null, null);
                        InitializeGazeAnalytics();
                        InitializeYoloContext();
                        Invalidate();
                    }));
                }
            }
            catch (Exception ex)
            {
                authStatus = "Save failed: " + ex.Message;
                authInProgress = false;
                loginPhase = LoginAuthPhase.MainPicker;
                SafeInvalidate();
            }
        });

        t.IsBackground = true;
        t.Name = "AuthFlowSaveReg";
        t.Start();
    }

    private static float NormalizeAuthRingAngle(float angle)
    {
        while (angle < 0f) angle += (float)(Math.PI * 2.0);
        while (angle >= (float)(Math.PI * 2.0)) angle -= (float)(Math.PI * 2.0);
        return angle;
    }

    private void UpdateAuthRingSelectionFromMarker(float angleRad, int count)
    {
        if (count <= 0) return;
        float fromTop = NormalizeAuthRingAngle(angleRad + (float)Math.PI / 2f);
        float step = (float)(Math.PI * 2.0 / count);
        // Floor into [0,count) so boundaries match pie slices (Round caused wrong wedge at π/2, etc.).
        int idx = (int)(fromTop / step);
        if (idx >= count) idx = count - 1;
        if (idx < 0) idx = 0;
        authRingSelectedIndex = idx;
    }

    private void UpdateLoginScreenTuio()
    {
        if (isLoggedIn) return;

        if (authInProgress &&
            (loginPhase == LoginAuthPhase.LoginScanning ||
             loginPhase == LoginAuthPhase.RegisterScanning ||
             loginPhase == LoginAuthPhase.RegisterBluetoothScanning ||
             loginPhase == LoginAuthPhase.RegisterSaving))
            return;

        if (authRingItems == null || authRingItems.Count == 0) return;

        TuioObject marker = GetSingleMenuMarker();
        if (marker == null)
        {
            authTuioMarkerTracked = false;
            authRingGestureArmed = true;
            authRingAccumY = 0f;
            authRingHasLastY = false;
            return;
        }

        authTuioMarkerTracked = true;
        authTuioMarkerAngleDeg = marker.Angle / (float)Math.PI * 180f;
        UpdateAuthRingSelectionFromMarker(marker.Angle, authRingItems.Count);

        if (!authRingHasLastY)
        {
            authRingHasLastY = true;
            authRingLastY = marker.Y;
            authRingAccumY = 0f;
            return;
        }

        float frameDy = marker.Y - authRingLastY;
        authRingAccumY += frameDy;

        if (Math.Abs(marker.Y - 0.5f) <= MenuMoveNeutralBandY)
        {
            authRingGestureArmed = true;
            authRingAccumY = 0f;
        }

        float upDelta = MenuUpIsPositiveY ? (-authRingAccumY) : authRingAccumY;
        float downDelta = -upDelta;

        if (authRingGestureArmed && upDelta >= MenuMoveTriggerDeltaY)
        {
            authRingGestureArmed = false;
            authRingAccumY = 0f;
            OnAuthRingConfirm();
        }
        else if (authRingGestureArmed && downDelta >= MenuMoveTriggerDeltaY)
        {
            authRingGestureArmed = false;
            authRingAccumY = 0f;
            OnAuthRingBack();
        }

        authRingLastY = marker.Y;
    }

    private void OnAuthRingConfirm()
    {
        if (authInProgress) return;

        switch (loginPhase)
        {
            case LoginAuthPhase.MainPicker:
                ShowAuthPicker();
                break;

            case LoginAuthPhase.NoFaceRecovery:
            case LoginAuthPhase.AuthCameraIssue:
                if (authRingSelectedIndex == 0)
                    ShowAuthPicker();
                else
                    Application.Exit();
                Invalidate();
                break;

            case LoginAuthPhase.RegisterDuplicateChoice:
                if (authRingSelectedIndex == 0)
                    RunDuplicateUserLoginThread();
                else
                    ShowAuthPicker();
                break;

            case LoginAuthPhase.LoginBluetoothRecovery:
                if (loginBluetoothRecoveryEscalated)
                {
                    if (authRingSelectedIndex == 0)
                        ShowAuthPicker();
                    else
                        LoginGuestAndEnter();
                }
                else
                {
                    if (authRingSelectedIndex == 0)
                        RunLoginBluetoothRetryThread();
                    else
                        ShowAuthPicker();
                }
                Invalidate();
                break;

            case LoginAuthPhase.RegisterBluetoothRecovery:
                if (authRingSelectedIndex == 0)
                    StartBluetoothPickForRegistration();
                else
                    ShowAuthPicker();
                Invalidate();
                break;

            case LoginAuthPhase.RegisterBluetoothConfirm:
                if (authRingSelectedIndex == 0)
                    BeginRegistrationFirstNameEntry();
                else
                    StartBluetoothPickForRegistration();
                break;

            case LoginAuthPhase.ProfileWelcome:
                TriggerProfileWelcomeContinueToBluetooth();
                break;

            case LoginAuthPhase.RegisterEnterFirstName:
            case LoginAuthPhase.RegisterEnterLastName:
            case LoginAuthPhase.RegisterEnterAge:
                if (authRingSelectedIndex >= 0 && authRingSelectedIndex < authRingItems.Count)
                    ProcessRegistrationTextPick(authRingItems[authRingSelectedIndex]);
                break;

            case LoginAuthPhase.RegisterGenderPick:
                regPendingGender = GenderFromRingIndex(authRingSelectedIndex);
                loginPhase = LoginAuthPhase.RegisterRolePick;
                authRingItems = new List<string> { "Role: visitor", "Role: admin" };
                authRingSelectedIndex = 0;
                pendingRegisterRole = "visitor";
                authStatus = "Gender: " + regPendingGender + ". Now select role — flick UP to confirm.";
                Invalidate();
                break;

            case LoginAuthPhase.RegisterRolePick:
                pendingRegisterRole = authRingSelectedIndex == 1 ? "admin" : "visitor";
                RunCompleteRegistrationThread();
                break;
        }
    }

    private void BeginRegistrationFirstNameEntry()
    {
        loginPhase = LoginAuthPhase.RegisterEnterFirstName;
        nameEntryBuffer = "";
        regPendingFirstName = null;
        regPendingLastName = null;
        regPendingAge = 0;
        regPendingGender = null;
        authRingItems = BuildAuthAlphabetRing();
        authRingSelectedIndex = 0;
        authRingGestureArmed = true;
        authRingHasLastY = false;
        authRingAccumY = 0f;
        authStatus = "First name: rotate to A-Z (or SPC). Flick UP adds the highlighted key. OK when done.";
        Invalidate();
    }

    private static List<string> BuildAuthAlphabetRing()
    {
        var L = new List<string>(32);
        for (char c = 'A'; c <= 'Z'; c++)
            L.Add(c.ToString());
        L.Add("SPC");
        L.Add("<-");
        L.Add("OK");
        return L;
    }

    private static List<string> BuildAuthAgeRing()
    {
        var L = new List<string>(14);
        for (int i = 0; i <= 9; i++)
            L.Add(i.ToString());
        L.Add("<-");
        L.Add("OK");
        return L;
    }

    private static List<string> BuildGenderRing()
    {
        return new List<string> { "Male", "Female", "Other" };
    }

    private static string GenderFromRingIndex(int index)
    {
        if (index == 0) return "male";
        if (index == 1) return "female";
        return "other";
    }

    private static int GenderRingIndexFromValue(string genderCsv)
    {
        string g = (genderCsv ?? "").Trim().ToLowerInvariant();
        if (g == "male") return 0;
        if (g == "female") return 1;
        return 2;
    }

    private static string NormalizeRegGender(string g)
    {
        if (string.IsNullOrWhiteSpace(g)) return "other";
        g = g.Trim().ToLowerInvariant();
        if (g == "male" || g == "female" || g == "other") return g;
        return "other";
    }

    private void ProcessRegistrationTextPick(string pick)
    {
        if (string.IsNullOrEmpty(pick)) return;

        if (loginPhase == LoginAuthPhase.RegisterEnterFirstName ||
            loginPhase == LoginAuthPhase.RegisterEnterLastName)
        {
            if (pick == "OK")
            {
                string t = (nameEntryBuffer ?? "").Trim();
                if (t.Length == 0)
                {
                    authStatus = "Enter at least one letter, then OK.";
                    Invalidate();
                    return;
                }
                t = AuthCsvStore.SanitizeField(t);
                if (t.Length > RegMaxNameChars)
                    t = t.Substring(0, RegMaxNameChars);

                if (loginPhase == LoginAuthPhase.RegisterEnterFirstName)
                {
                    regPendingFirstName = t;
                    loginPhase = LoginAuthPhase.RegisterEnterLastName;
                    nameEntryBuffer = "";
                    authStatus = "Last name: same controls. SPC = space. OK when done.";
                }
                else
                {
                    regPendingLastName = t;
                    loginPhase = LoginAuthPhase.RegisterEnterAge;
                    nameEntryBuffer = "";
                    authRingItems = BuildAuthAgeRing();
                    authRingSelectedIndex = 0;
                    authStatus = "Age (years): pick 0-9, then OK. Valid range " + RegMinAge + "-" + RegMaxAge + ".";
                }
                Invalidate();
                return;
            }

            if (pick == "<-")
            {
                if (nameEntryBuffer.Length > 0)
                    nameEntryBuffer = nameEntryBuffer.Substring(0, nameEntryBuffer.Length - 1);
                Invalidate();
                return;
            }

            if (pick == "SPC")
            {
                if (nameEntryBuffer.Length < RegMaxNameChars)
                    nameEntryBuffer += " ";
                Invalidate();
                return;
            }

            if (pick.Length == 1 && pick[0] >= 'A' && pick[0] <= 'Z')
            {
                if (nameEntryBuffer.Length < RegMaxNameChars)
                    nameEntryBuffer += pick;
                Invalidate();
            }
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterEnterAge)
        {
            if (pick == "OK")
            {
                if (!int.TryParse((nameEntryBuffer ?? "").Trim(), out int age) ||
                    age < RegMinAge || age > RegMaxAge)
                {
                    authStatus = "Invalid age. Use digits 1-120, then OK.";
                    Invalidate();
                    return;
                }

                regPendingAge = age;
                loginPhase = LoginAuthPhase.RegisterGenderPick;
                nameEntryBuffer = "";
                authRingItems = BuildGenderRing();
                authRingSelectedIndex = 0;
                authStatus = "Select gender (rotate marker). Flick UP to confirm.";
                Invalidate();
                return;
            }

            if (pick == "<-")
            {
                if (nameEntryBuffer.Length > 0)
                    nameEntryBuffer = nameEntryBuffer.Substring(0, nameEntryBuffer.Length - 1);
                Invalidate();
                return;
            }

            if (pick.Length == 1 && pick[0] >= '0' && pick[0] <= '9')
            {
                if (nameEntryBuffer.Length < 3)
                    nameEntryBuffer += pick;
                Invalidate();
            }
        }
    }

    private void OnAuthRingBack()
    {
        if (authInProgress) return;

        if (loginPhase == LoginAuthPhase.LoginBluetoothRecovery ||
            loginPhase == LoginAuthPhase.RegisterBluetoothRecovery ||
            loginPhase == LoginAuthPhase.NoFaceRecovery ||
            loginPhase == LoginAuthPhase.AuthCameraIssue ||
            loginPhase == LoginAuthPhase.ProfileWelcome)
        {
            ShowAuthPicker();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterEnterFirstName)
        {
            if (nameEntryBuffer.Length > 0)
            {
                nameEntryBuffer = nameEntryBuffer.Substring(0, nameEntryBuffer.Length - 1);
                Invalidate();
                return;
            }
            loginPhase = LoginAuthPhase.RegisterBluetoothConfirm;
            authRingItems = new List<string> { "Use this device", "Scan again" };
            authRingSelectedIndex = 0;
            authStatus = "Bluetooth: " + (pendingBtDeviceName ?? "") + " — " + (pendingBtMac ?? "");
            Invalidate();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterEnterLastName)
        {
            if (nameEntryBuffer.Length > 0)
            {
                nameEntryBuffer = nameEntryBuffer.Substring(0, nameEntryBuffer.Length - 1);
                Invalidate();
                return;
            }
            string keep = regPendingFirstName ?? "";
            regPendingFirstName = null;
            loginPhase = LoginAuthPhase.RegisterEnterFirstName;
            nameEntryBuffer = keep;
            authRingItems = BuildAuthAlphabetRing();
            authRingSelectedIndex = 0;
            authStatus = "First name — rotate and flick UP to add letters.";
            Invalidate();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterEnterAge)
        {
            if (nameEntryBuffer.Length > 0)
            {
                nameEntryBuffer = nameEntryBuffer.Substring(0, nameEntryBuffer.Length - 1);
                Invalidate();
                return;
            }
            string keepLast = regPendingLastName ?? "";
            regPendingLastName = null;
            loginPhase = LoginAuthPhase.RegisterEnterLastName;
            nameEntryBuffer = keepLast;
            authRingItems = BuildAuthAlphabetRing();
            authRingSelectedIndex = 0;
            authStatus = "Last name — rotate and flick UP.";
            Invalidate();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterRolePick)
        {
            loginPhase = LoginAuthPhase.RegisterGenderPick;
            authRingItems = BuildGenderRing();
            authRingSelectedIndex = GenderRingIndexFromValue(regPendingGender);
            pendingRegisterRole = "visitor";
            authStatus = "Select gender — flick UP to confirm choice.";
            Invalidate();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterGenderPick)
        {
            loginPhase = LoginAuthPhase.RegisterEnterAge;
            nameEntryBuffer = regPendingAge >= RegMinAge && regPendingAge <= RegMaxAge
                ? regPendingAge.ToString()
                : "";
            regPendingGender = null;
            authRingItems = BuildAuthAgeRing();
            authRingSelectedIndex = 0;
            authStatus = "Age (years): digits then OK.";
            Invalidate();
            return;
        }

        if (loginPhase == LoginAuthPhase.RegisterBluetoothConfirm ||
            loginPhase == LoginAuthPhase.RegisterDuplicateChoice)
            ShowAuthPicker();
    }

    private void HandleAuthScreenGesture(string normalizedGesture)
    {
        if (authInProgress) return;

        if (normalizedGesture == "thumbsup" || normalizedGesture == "thumbup" || normalizedGesture == "thumbs" ||
            normalizedGesture == "open")
        {
            OnAuthRingConfirm();
            Invalidate();
            return;
        }

        if (normalizedGesture == "close")
        {
            OnAuthRingBack();
            Invalidate();
        }
    }

    private void DrawAuthTuioRing(Graphics g, int cx, int cy, int radius, Color accent, Color dim)
    {
        if (authRingItems == null || authRingItems.Count == 0) return;

        int n = authRingItems.Count;
        for (int i = 0; i < n; i++)
        {
            float a0 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * i) / n);
            float a1 = (float)(-Math.PI / 2 + (Math.PI * 2.0 * (i + 1)) / n);
            bool sel = i == authRingSelectedIndex;
            using (var br = new SolidBrush(sel ? Color.FromArgb(228, accent) : Color.FromArgb(165, 38, 40, 48)))
            using (var path = new GraphicsPath())
            {
                path.AddPie(cx - radius, cy - radius, radius * 2, radius * 2,
                    (float)(a0 * 180.0 / Math.PI), (float)((a1 - a0) * 180.0 / Math.PI));
                g.FillPath(br, path);
            }

            float am = (a0 + a1) * 0.5f;
            string label = authRingItems[i];
            if (label.Length > 16) label = label.Substring(0, 14) + "..";
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            float tx = cx + (float)Math.Cos(am) * (radius * 0.62f);
            float ty = cy + (float)Math.Sin(am) * (radius * 0.62f);
            float fz = n > 20 ? Math.Max(8f, 220f / n) : Math.Max(11f, fontSmall.Size);
            using (var tbr = new SolidBrush(sel ? Color.FromArgb(245, 12, 12, 14) : Color.FromArgb(245, 248, 246, 238)))
            using (var lf = new Font("Georgia", fz, FontStyle.Bold, GraphicsUnit.Pixel))
                g.DrawString(label, lf, tbr, tx, ty, sf);
        }

        using (var p = new Pen(Color.FromArgb(195, accent), 2f))
            g.DrawEllipse(p, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private string GetWorkspaceRoot()
    {
        DirectoryInfo d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        if (d.Parent != null) d = d.Parent;
        if (d.Parent != null) d = d.Parent;
        if (d.Parent != null) d = d.Parent;
        return d.FullName;
    }

    private bool TryLoadVisitorProfile(string faceUserId, out VisitorProfile profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(faceUserId)) return false;
        try
        {
            string csvPath = Path.Combine(GetWorkspaceRoot(), "C#", "content", "auth", "users.csv");
            List<VisitorProfile> users = VisitorProfile.LoadFromCsv(csvPath);
            profile = users.Find(u =>
                string.Equals(u.FaceUserId, faceUserId, StringComparison.OrdinalIgnoreCase));
            return profile != null;
        }
        catch
        {
            return false;
        }
    }

    private void DrawAuthProfilePanel(Graphics g, float topY)
    {
        if (authLobbyProfilePreview == null)
        {
            string fallback = "Account: " + (pendingDuplicateUserId ?? "—") + " (profile row not found in users.csv)";
            DrawCentered(g, fallback, fontBody, Color.FromArgb(220, CPapyrus),
                new RectangleF(60, topY, W - 120, 80));
            return;
        }

        VisitorProfile p = authLobbyProfilePreview;
        float boxLeft = 72f;
        float boxW = W - 144f;
        const float boxH = 168f;
        using (var br = new SolidBrush(Color.FromArgb(228, 42, 42, 62)))
            g.FillRectangle(br, boxLeft, topY, boxW, boxH);
        using (var pen = new Pen(Color.FromArgb(230, 212, 175, 55), 2f))
            g.DrawRectangle(pen, boxLeft, topY, boxW, boxH);

        DrawCentered(g, "Visitor profile", fontSubtitle, CGold,
            new RectangleF(40, topY + 10f, W - 80, 28f));
        DrawCentered(g, p.FullName.Trim().Length > 0 ? p.FullName : p.FaceUserId, fontBody,
            Color.FromArgb(245, 255, 255, 255),
            new RectangleF(40, topY + 38f, W - 80, 28f));
        string line1 = "ID: " + p.FaceUserId + "  •  Role: " + (p.Role ?? "visitor");
        DrawCentered(g, line1, fontSmall, Color.FromArgb(220, CPapyrus),
            new RectangleF(40, topY + 66f, W - 80, 24f));
        string line2 = "Age: " + p.Age + "  •  Gender: " + p.Gender + "  •  Race: " + p.Race;
        DrawCentered(g, line2, fontSmall, Color.FromArgb(220, CPapyrus),
            new RectangleF(40, topY + 90f, W - 80, 24f));
        DrawCentered(g, "Language: " + (p.Language ?? ""), fontSmall, Color.FromArgb(210, CPapyrus),
            new RectangleF(40, topY + 114f, W - 80, 24f));
    }

    private void TriggerProfileWelcomeContinueToBluetooth()
    {
        if (loginPhase != LoginAuthPhase.ProfileWelcome || authInProgress) return;
        profileWelcomeAutoContinueUtc = null;
        authLobbyProfilePreview = null;
        RunDuplicateUserLoginThread();
        Invalidate();
    }

    /// <summary>Top-anchored layout after face match — no TUIO ring; auto-advance timer drives Bluetooth step.</summary>
    private void DrawProfileWelcomeScreen(Graphics g)
    {
        const float titleY = 40f;
        DrawCentered(g, "WELCOME BACK", fontTitle, themeSecondary,
            new RectangleF(0, titleY, W, 50f));
        DrawCentered(g, "Your visitor profile — Grand Egyptian Museum", fontSubtitle,
            Color.FromArgb(238, CPapyrus),
            new RectangleF(40, titleY + 48f, W - 80, 32f));
        float statusTop = titleY + 48f + 32f + 12f;
        const float statusH = 76f;
        DrawWrappedCentered(g, authStatus, fontBody, Color.White,
            new RectangleF(40, statusTop, W - 80, statusH));
        float panelTop = statusTop + statusH + 16f;
        DrawAuthProfilePanel(g, panelTop);
        const float panelH = 168f;
        float countY = panelTop + panelH + 20f;
        if (profileWelcomeAutoContinueUtc.HasValue && !authInProgress)
        {
            double rem = (profileWelcomeAutoContinueUtc.Value - DateTime.UtcNow).TotalSeconds;
            int sec = rem <= 0 ? 0 : (int)Math.Ceiling(rem);
            string tail = sec <= 0
                ? "Starting phone check…"
                : "Continuing in " + sec + " second" + (sec == 1 ? "" : "s") + "…";
            DrawCentered(g, tail, fontHint, CGoldLight,
                new RectangleF(40, countY, W - 80, 34f));
        }
    }

    private void ConfigureCircularMenuForUser()
    {
        circularMenu.TopItems.Remove("Analytics");
        if (visitorProfile != null && visitorProfile.IsAdmin &&
            !circularMenu.TopItems.Contains("Analytics"))
            circularMenu.TopItems.Insert(3, "Analytics");
    }

    private async void InitializeGazeAnalytics()
    {
        if (visitorProfile == null) return;

        string analyticsDir = Path.Combine(GetWorkspaceRoot(), "C#", "content", "analytics");
        analyticsRecorder.BeginVisit(analyticsDir, visitorProfile.FaceUserId, visitorProfile.FullName);
        adminAnalyticsPanel = new AdminAnalyticsPanel(analyticsRecorder,
            () => Path.Combine(GetWorkspaceRoot(), "C#", "content", "analytics"));

        lock (liveGazeLock)
        {
            liveGazeStreamActive = false;
            liveGazeValid = false;
            liveGazeIssueUserText = "";
        }

        // 127.0.0.1: Python services bind IPv4 only; "localhost" can resolve to ::1 and never connect.
        gazeEmotionClient = new GazeEmotionClient("127.0.0.1", 5002);
        bool ok = await gazeEmotionClient.ConnectAsync();
        if (!ok)
        {
            Console.WriteLine("Gaze/emotion service not available (start python/server/gaze_emotion_service.py on port 5002).");
            return;
        }

        gazeEmotionClient.FrameReceived += OnGazeFrame;
        bool streamOk = await gazeEmotionClient.StartStreamingAsync();
        if (!streamOk)
        {
            Console.WriteLine("Gaze/emotion STREAM handshake failed.");
            gazeEmotionClient.FrameReceived -= OnGazeFrame;
            try { gazeEmotionClient.Dispose(); } catch { }
            gazeEmotionClient = null;
            lock (liveGazeLock)
            {
                liveGazeStreamActive = false;
                liveGazeValid = false;
                liveGazeIssueUserText = "";
            }
            return;
        }

        lock (liveGazeLock)
        {
            liveGazeStreamActive = true;
            liveGazeIssueUserText = "";
        }
    }

    private async void InitializeYoloContext()
    {
        if (visitorProfile == null) return;
        TeardownYoloContext();
        yoloContextClient = new YoloContextClient("127.0.0.1", 5003);
        bool ok = await yoloContextClient.ConnectAsync();
        if (!ok)
        {
            Console.WriteLine("YOLO context service not available (start python/server/yolo_context_service.py on port 5003).");
            return;
        }
        yoloContextClient.FrameReceived += OnYoloFrame;
        bool streamOk = await yoloContextClient.StartStreamingAsync();
        if (!streamOk)
            Console.WriteLine("YOLO context STREAM handshake failed.");
    }

    private void TeardownYoloContext()
    {
        if (yoloContextClient != null)
        {
            yoloContextClient.FrameReceived -= OnYoloFrame;
            try
            {
                yoloContextClient.StopStreamingAsync().GetAwaiter().GetResult();
            }
            catch { }
            yoloContextClient.Dispose();
            yoloContextClient = null;
        }
        _yoloPhonePresenceCounter = 0;
        SetMuseumAppPhoneBannerVisible(false);
    }

    private void OnYoloFrame(YoloContextFrame frame)
    {
        if (visitorProfile == null || frame == null || !frame.Ok) return;
        bool phone = false;
        bool book = false;
        bool large = false;
        for (int i = 0; i < frame.Tracks.Count; i++)
        {
            YoloTrack t = frame.Tracks[i];
            if (t.Conf < 0.35) continue;
            string c = (t.ClassName ?? string.Empty).Trim().ToLowerInvariant();
            if (c.IndexOf("phone", StringComparison.Ordinal) >= 0) phone = true;
            if (c == "book" || c.IndexOf("laptop", StringComparison.Ordinal) >= 0) book = true;
            if (c == "person" && t.W * t.H >= 0.10) large = true;
        }
        UpdateMuseumAppPhoneBanner(phone);
        if (visitorProfile.SetCameraAmbientContext(phone, book, large))
        {
            ApplyVisitorTheme();
            Invalidate();
        }
    }

    private void UpdateMuseumAppPhoneBanner(bool phoneInFrame)
    {
        if (phoneInFrame)
            _yoloPhonePresenceCounter = Math.Min(_yoloPhonePresenceCounter + 1, 20);
        else
            _yoloPhonePresenceCounter = Math.Max(_yoloPhonePresenceCounter - 1, 0);
        bool want = _yoloPhonePresenceCounter >= YoloPhoneBannerOnFrames;
        SetMuseumAppPhoneBannerVisible(want);
    }

    private void SetMuseumAppPhoneBannerVisible(bool visible)
    {
        if (visible == _showMuseumAppPhoneBanner) return;
        _showMuseumAppPhoneBanner = visible;
        Invalidate();
    }

    private void TeardownGazeAnalytics()
    {
        TeardownYoloContext();
        if (gazeEmotionClient != null)
        {
            gazeEmotionClient.FrameReceived -= OnGazeFrame;
            try
            {
                gazeEmotionClient.StopStreamingAsync().GetAwaiter().GetResult();
            }
            catch { }
            gazeEmotionClient.Dispose();
            gazeEmotionClient = null;
        }
        lock (liveGazeLock)
        {
            liveGazeStreamActive = false;
            liveGazeValid = false;
            liveGazeIssueUserText = "";
        }
    }

    private void OnGazeFrame(GazeEmotionFrame frame)
    {
        if (frame == null) return;
        lock (liveGazeLock)
        {
            if (frame.Ok)
            {
                liveGazeValid = true;
                liveGazeGx = frame.Gx;
                liveGazeGy = frame.Gy;
                liveDominantEmotion = string.IsNullOrEmpty(frame.Dominant) ? "neutral" : frame.Dominant;
                liveGazeIssueUserText = "";
            }
            else
            {
                liveGazeValid = false;
                liveGazeIssueUserText = FormatGazeIssueHint(frame.Reason);
            }
        }
        if (frame.Ok && analyticsRecorder != null && slideShow != null && slideShow.IsRunning && currentSlide != null)
            analyticsRecorder.AddSample(frame);
        if (slideShow != null && slideShow.IsRunning && currentSlide != null)
            SafeInvalidate();
    }

    private void UpdateAdminTuio()
    {
        if (adminAnalyticsPanel == null || !adminAnalyticsVisible) return;
        TuioObject marker = GetSingleMenuMarker();
        bool has = marker != null;
        float ang = has ? marker.Angle : 0f;
        float y = has ? marker.Y : 0.5f;
        bool closePanel;
        adminAnalyticsPanel.OnMarker(has, ang, y, out closePanel);
        if (closePanel)
        {
            adminAnalyticsVisible = false;
            adminAnalyticsPanel.Exit();
        }
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
        AddFavoriteIfExists("figure:7");
        AddFavoriteIfExists("relationship:1_2");
    }

    private async void InitializeGestureClient()
    {
        try
        {
            gestureClient = new GestureClient("127.0.0.1", 5001);
            
            // Subscribe to gesture events
            gestureClient.GestureRecognized += OnGestureRecognized;
            gestureClient.StatusChanged += (s, status) => 
            {
                Console.WriteLine($"Gesture Status: {status}");
            };

            // Try to connect to gesture service
            bool connected = await gestureClient.ConnectAsync();
            
            if (connected)
            {
                Console.WriteLine("Connected to gesture recognition service");
                await gestureClient.StartTrackingAsync();
                
                // Start continuous gesture detection (check every 300ms instead of 100ms)
                gestureCheckTimer = new System.Windows.Forms.Timer { Interval = 300 };
                gestureCheckTimer.Tick += async (s, e) => await CheckForGesture();
                gestureCheckTimer.Start();
            }
            else
            {
                Console.WriteLine("Gesture service not available - continuing without gesture control");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Gesture client initialization failed: {ex.Message}");
        }
    }

    private void InitializeHandTracker()
    {
        try
        {
            // Hand tracker is separate from museum_vision_server (which uses 5002 for gaze).
            // Default port in python/server/hand_tracker_service.py is 5004.
            handTrackClient = new HandTrackClient(5004);
            handTrackClient.Connect();
            Console.WriteLine("[3D] Hand tracker connected on port 5004");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[3D] Hand tracker not available (start hand_tracker_service.py): " + ex.Message);
        }
    }

    /// <summary>Face sign-in / recovery must not compete with gesture_service for the same USB webcam.</summary>
    private bool LoginFlowBlocksGestureWebcam()    {
        if (isLoggedIn) return false;
        return loginPhase == LoginAuthPhase.NoFaceRecovery
            || loginPhase == LoginAuthPhase.AuthCameraIssue
            || loginPhase == LoginAuthPhase.RegisterScanning;
    }

    private void TryReleaseGestureWebcamBlocking()
    {
        try
        {
            if (gestureClient == null || !gestureClient.IsConnected) return;
            gestureClient.StopTrackingSilentlyAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            gestureClient.ResetAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }
        catch
        {
            // ignore — Face ID should still attempt open
        }
    }

    private async System.Threading.Tasks.Task CheckForGesture()
    {
        if (isGestureActive || gestureClient == null || !gestureClient.IsConnected) return;

        // Check input prioritization: block gestures if TUIOs present or in cooldown
        if (!inputPrioritizer.CanAcceptGestures)
        {
            int cooldownRemaining = inputPrioritizer.GetCooldownRemainingMs();
            if (cooldownRemaining > 0)
                Console.WriteLine($"[Gesture] Blocked by input prioritizer: {cooldownRemaining}ms cooldown remaining");
            return;
        }

        if (!isLoggedIn)
        {
            if (authInProgress) return;
            if (LoginFlowBlocksGestureWebcam()) return;
            try
            {
                // NEW SLIDING WINDOW API: Check status for last_gesture
                var status = await gestureClient.GetStatusAsync();
                if (status != null && !string.IsNullOrEmpty(status.LastGesture))
                {
                    // Gesture detected! Get it (this also clears it)
                    var result = await gestureClient.StopAndRecognizeAsync();
                    if (!string.IsNullOrEmpty(result.Gesture))
                    {
                        Console.WriteLine($"✓ Gesture detected: {result.Gesture}");
                        HandleGesture(result.Gesture);
                    }
                }
                else if (status != null && !status.IsTracking)
                {
                    await gestureClient.StartTrackingAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Gesture] Error: {ex.Message}");
                isGestureActive = false;
            }
            return;
        }

        if (authInProgress || adminAnalyticsVisible) return;

        try
        {
            // NEW SLIDING WINDOW API: Check status for last_gesture
            var status = await gestureClient.GetStatusAsync();

            if (status != null && !string.IsNullOrEmpty(status.LastGesture))
            {
                isGestureActive = true;

                // Get gesture (this also clears it from service)
                var result = await gestureClient.StopAndRecognizeAsync();
                if (!string.IsNullOrEmpty(result.Gesture))
                {
                    Console.WriteLine($"✓ Gesture detected: {result.Gesture}");
                    HandleGesture(result.Gesture);
                }

                isGestureActive = false;
            }
            else if (status != null && !status.IsTracking)
            {
                // Start tracking if not already tracking
                await gestureClient.StartTrackingAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gesture] Error: {ex.Message}");
            isGestureActive = false;
        }
    }

    private void OnGestureRecognized(object sender, GestureRecognizedEventArgs e)
    {
        Console.WriteLine($"Gesture Event: {e.Result.Gesture} - Confidence: {e.Result.Confidence}");
    }

    private void HandleGesture(string gesture)
    {
        Console.WriteLine($"=== HandleGesture called ===");
        Console.WriteLine($"Gesture: {gesture}");
        Console.WriteLine($"Menu visible: {circularMenu.IsVisible}");
        Console.WriteLine($"Logged in: {isLoggedIn}");
        
        // Update gesture overlay display
        lastDetectedGesture = gesture;
        gestureDisplayTime = DateTime.Now;
        Invalidate(); // Trigger redraw to show overlay

        // Ensure UI updates happen on the UI thread
        if (InvokeRequired)
        {
            Console.WriteLine("→ Invoking on UI thread...");
            BeginInvoke(new Action(() => HandleGesture(gesture)));
            return;
        }

        if (!isLoggedIn)
        {
            string ng = gesture.ToLower().Replace("_", "");
            HandleAuthScreenGesture(ng);
            return;
        }

        if (adminAnalyticsVisible)
        {
            string ng = gesture.ToLower().Replace("_", "");
            if (ng == "close")
            {
                adminAnalyticsVisible = false;
                if (adminAnalyticsPanel != null) adminAnalyticsPanel.Exit();
                Invalidate();
            }
            return;
        }

        // Normalize gesture name (remove underscores, lowercase)
        string normalizedGesture = gesture.ToLower().Replace("_", "");

        switch (normalizedGesture)
        {
            case "thumbsup":
            case "thumbup":
            case "thumbs":
                // Thumbs up opens the circular menu OR selects item if menu is already open
                Console.WriteLine("→ Matched thumbsup case");
                if (!circularMenu.IsVisible)
                {
                    Console.WriteLine("→ Opening menu...");
                    circularMenu.Show();
                    menuOpenedByGesture = true; // Mark as gesture-opened
                    menuFlickNeedsResync = true;
                    Invalidate(); // Force redraw
                    Console.WriteLine("✓ Gesture: Menu opened with thumbs up");
                }
                else
                {
                    Console.WriteLine("→ Menu already visible, selecting current item...");
                    circularMenu.MoveUpAction(); // Select the current menu item
                    Invalidate(); // Force redraw
                    Console.WriteLine("✓ Gesture: Thumbs up -> Selected menu item");
                }
                break;

            case "close":
                // Close gesture closes the circular menu
                Console.WriteLine("→ Matched close case");
                if (circularMenu.IsVisible)
                {
                    circularMenu.Hide();
                    menuOpenedByGesture = false; // Clear the flag
                    Invalidate(); // Force redraw
                    Console.WriteLine("✓ Gesture: Menu closed");
                }
                else
                {
                    Console.WriteLine("→ Menu already closed, skipping");
                }
                break;

            case "swipeleft":
            case "swipel":
            case "swipe_left":
                // Swipe left = Navigate to NEXT option in circular menu
                Console.WriteLine("→ Matched swipe left case");
                if (circularMenu.IsVisible)
                {
                    // Rotate menu selection counter-clockwise (next item)
                    int currentIndex = circularMenu.TopIndex;
                    int itemCount = circularMenu.IsInSecondLevel 
                        ? (circularMenu.SelectedTop == "Favorites" ? circularMenu.Favorites.Count : circularMenu.Watched.Count)
                        : circularMenu.TopItems.Count;
                    
                    if (itemCount > 0)
                    {
                        int newIndex = (currentIndex + 1) % itemCount;
                        float angleStep = (float)(Math.PI * 2.0 / itemCount);
                        float newAngle = newIndex * angleStep - (float)Math.PI / 2f;
                        circularMenu.UpdateRotation(newAngle);
                        Invalidate(); // Force redraw
                        Console.WriteLine($"✓ Gesture: Swipe left -> Next option (index {currentIndex} → {newIndex})");
                    }
                }
                else
                {
                    Console.WriteLine("→ Menu not visible, ignoring swipe");
                }
                break;

            case "swiperight":
            case "swiper":
            case "swipe_right":
                // Swipe right = Navigate to PREVIOUS option in circular menu
                Console.WriteLine("→ Matched swipe right case");
                if (circularMenu.IsVisible)
                {
                    // Rotate menu selection clockwise (previous item)
                    int currentIndex = circularMenu.TopIndex;
                    int itemCount = circularMenu.IsInSecondLevel 
                        ? (circularMenu.SelectedTop == "Favorites" ? circularMenu.Favorites.Count : circularMenu.Watched.Count)
                        : circularMenu.TopItems.Count;
                    
                    if (itemCount > 0)
                    {
                        int newIndex = (currentIndex - 1 + itemCount) % itemCount;
                        float angleStep = (float)(Math.PI * 2.0 / itemCount);
                        float newAngle = newIndex * angleStep - (float)Math.PI / 2f;
                        circularMenu.UpdateRotation(newAngle);
                        Invalidate(); // Force redraw
                        Console.WriteLine($"✓ Gesture: Swipe right -> Previous option (index {currentIndex} → {newIndex})");
                    }
                }
                else
                {
                    Console.WriteLine("→ Menu not visible, ignoring swipe");
                }
                break;

            case "open":
                // Open gesture selects/enters current menu item (only when menu is visible)
                Console.WriteLine("→ Matched open case");
                if (circularMenu.IsVisible)
                {
                    circularMenu.MoveUpAction();
                    Invalidate(); // Force redraw
                    Console.WriteLine("✓ Gesture: Open -> Select item");
                }
                else
                {
                    Console.WriteLine("→ Menu not visible, ignoring open gesture");
                }
                break;

            default:
                Console.WriteLine($"✗ Unknown gesture: {gesture}");
                break;
        }
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
        if (action == "Analytics")
        {
            if (visitorProfile == null || !visitorProfile.IsAdmin) return;
            circularMenu.Hide();
            menuOpenedByGesture = false;
            adminAnalyticsVisible = true;
            if (adminAnalyticsPanel == null)
                adminAnalyticsPanel = new AdminAnalyticsPanel(analyticsRecorder,
                    () => Path.Combine(GetWorkspaceRoot(), "C#", "content", "analytics"));
            adminAnalyticsPanel.Enter();
            Invalidate();
            return;
        }

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
            menuOpenedByGesture = false; // Clear flag
            StopAndUnlockSlides();
            Transition(AppState.Idle, null, null, null, null);
            return;
        }

        if (action == "Logout")
        {
            circularMenu.Hide();
            menuOpenedByGesture = false; // Clear flag
            StopAndUnlockSlides();
            adminAnalyticsVisible = false;
            if (adminAnalyticsPanel != null) adminAnalyticsPanel.Exit();
            analyticsRecorder.FlushAndSave();
            TeardownGazeAnalytics();
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
                    menuOpenedByGesture = false; // Clear flag
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
                    menuOpenedByGesture = false; // Clear flag
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
        if (analyticsRecorder != null)
            analyticsRecorder.NotifySlideShowEnded();
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
        inputPrioritizer.SetTuioPresent(true);
    }

    public void updateTuioObject(TuioObject o)
    {
        lock (objectList)
            if (objectList.ContainsKey(o.SessionID)) objectList[o.SessionID] = o;
        inputPrioritizer.SetTuioPresent(true);
    }

    public void removeTuioObject(TuioObject o)
    {
        lock (objectList) objectList.Remove(o.SessionID);
        
        // Check if any TUIOs remain
        bool anyTuioPresent;
        lock (objectList) anyTuioPresent = objectList.Count > 0;
        
        inputPrioritizer.SetTuioPresent(anyTuioPresent);
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
        if (adminAnalyticsVisible) return;
        if (circularMenu.IsVisible) return;

        List<TuioObject> onTable;
        lock (objectList) onTable = new List<TuioObject>(objectList.Values);

        // Keep only recognised figures
        onTable = onTable.FindAll(o =>
            !TuioControlMarker.IsMenuAuthMarker(o.SymbolID) &&
            !TuioControlMarker.IsReservedEmptySlot(o.SymbolID) &&
            MuseumData.Figures.ContainsKey(o.SymbolID));

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
                singleFigureIntroDone = recognizedFigureIds.Contains(activeFigureSymbolId);
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

        bool singleFigureNeedsRecognition =
            targetState == AppState.SingleFigure &&
            pendingFigureLocal != null &&
            !recognizedFigureIds.Contains(pendingFigureLocal.SymbolId);

        bool wasIdle = (state == AppState.Idle);
        if (!wasIdle)
        {
            if (singleFigureNeedsRecognition)
            {
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
            else
            {
                if (targetState == AppState.SingleFigure)
                    Transition(targetState, pendingFigureLocal, null, pendingA, pendingB);
                else
                    Transition(targetState, null, pendingRelationshipLocal, pendingA, pendingB);
            }
            return;
        }

        // Coming from Idle: skip recognition for figures already recognized once.
        if (targetState == AppState.SingleFigure && !singleFigureNeedsRecognition)
        {
            Transition(targetState, pendingFigureLocal, null, pendingA, pendingB);
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
        onTable = onTable.FindAll(o =>
            !TuioControlMarker.IsMenuAuthMarker(o.SymbolID) &&
            !TuioControlMarker.IsReservedEmptySlot(o.SymbolID) &&
            MuseumData.Figures.ContainsKey(o.SymbolID));

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
        if (analyticsRecorder != null)
            analyticsRecorder.NotifySlideShowEnded();

        slideshowLocked = false;
        slideElapsedMs = 0;
        currentSlide = null;
        hoverObject = null;
        objectHoldProgress = 0f;

        if (lockedContext == SlideShowContext.SingleFigureIntro)
        {
            if (activeFigureSymbolId > 0)
                recognizedFigureIds.Add(activeFigureSymbolId);
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
        if (!isLoggedIn && loginPhase == LoginAuthPhase.ProfileWelcome && !authInProgress &&
            profileWelcomeAutoContinueUtc.HasValue &&
            DateTime.UtcNow >= profileWelcomeAutoContinueUtc.Value)
            TriggerProfileWelcomeContinueToBluetooth();

        if (!isLoggedIn)
            UpdateLoginScreenTuio();
        UpdateCircularMenuInput();

        if (adminAnalyticsVisible && adminAnalyticsPanel != null)
            adminAnalyticsPanel.Tick(animTimer.Interval);

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
            state == AppState.SingleFigure || state == AppState.PairNotFacing || state == AppState.PairFacing ||
            fadingIn || circularMenu.IsVisible ||
            !isLoggedIn || adminAnalyticsVisible ||
            (slideShow != null && slideShow.IsRunning && currentSlide != null))
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

        if (adminAnalyticsVisible)
        {
            UpdateAdminTuio();
            return;
        }

        TuioObject marker = GetSingleMenuMarker();

        // Open the menu when TUIO menu marker (symbol 3) appears; symbol 0 is reserved and unused for now.
        if (!circularMenu.IsVisible && marker != null)
        {
            circularMenu.Show();
            menuOpenedByGesture = false; // Opened by marker, not gesture
            menuFlickNeedsResync = true;
        }

        // If menu was opened by marker control, hide when marker is removed.
        // BUT: Don't auto-close if menu was opened by hand gesture
        if (circularMenu.IsVisible && marker == null && !menuOpenedByGesture)
        {
            circularMenu.Hide();
            menuFlickNeedsResync = true;
            return;
        }

        if (!circularMenu.IsVisible) return;

        // Hide/show Favorite entry dynamically based on whether a figure context exists.
        circularMenu.ShowFavorite = !string.IsNullOrEmpty(GetCurrentFigureStoryKey());

        if (marker != null)
        {
            float a = marker.Angle;
            circularMenu.UpdateRotation(a);

            if (menuFlickNeedsResync)
            {
                menuFlickArmX = marker.X;
                menuFlickArmY = marker.Y;
                menuFlickNeedsResync = false;
                lastMenuFlickActionUtc = DateTime.UtcNow;
            }

            // Tangible menus: confirm by dragging the fiducial away from the last rest position (Reactable-style “push” / pull).
            if ((DateTime.UtcNow - lastMenuFlickActionUtc).TotalMilliseconds < MenuFlickCooldownMs)
                return;

            float rdx = marker.X - menuFlickArmX;
            float rdy = (marker.Y - menuFlickArmY) * (CircularMenuTuioInvertY ? -1f : 1f);
            float dist = (float)Math.Sqrt(rdx * rdx + rdy * rdy);

            bool confirm = rdy <= -MenuFlickPullY ||
                           (dist >= MenuFlickDiagonalDist && rdy <= -MenuFlickDiagonalBiasY);
            bool back = rdy >= MenuFlickPullY ||
                         (dist >= MenuFlickDiagonalDist && rdy >= MenuFlickDiagonalBiasY);

            if (confirm)
            {
                circularMenu.MoveUpAction();
                lastMenuFlickActionUtc = DateTime.UtcNow;
                menuFlickArmX = marker.X;
                menuFlickArmY = marker.Y;
                Invalidate();
            }
            else if (back)
            {
                circularMenu.MoveDownAction();
                lastMenuFlickActionUtc = DateTime.UtcNow;
                menuFlickArmX = marker.X;
                menuFlickArmY = marker.Y;
                Invalidate();
            }
        }
    }

    private TuioObject GetSingleMenuMarker()
    {
        List<TuioObject> onTable;
        lock (objectList) onTable = new List<TuioObject>(objectList.Values);
        onTable = onTable.FindAll(o => TuioControlMarker.IsMenuAuthMarker(o.SymbolID));
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

        if (!isLoggedIn)
        {
            DrawLoginScreen(g);
            DrawGestureOverlay(g); // Show gesture overlay even on login screen
            return;
        }

        if (adminAnalyticsVisible && adminAnalyticsPanel != null)
        {
            adminAnalyticsPanel.Draw(g, W, H, fontTitle, fontBody, fontSmall, themeSecondary, CPapyrus,
                analyticsRecorder != null ? analyticsRecorder.GetLiveSnapshot() : null);
            DrawMuseumAppPhoneDownloadBanner(g);
            DrawGestureOverlay(g);
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

        if (slideshowLocked && slideShow != null && slideShow.IsRunning && currentSlide != null &&
            state == AppState.Idle && lockedContext == SlideShowContext.MenuStory)
        {
            var menuContent = new Rectangle(40, 72, W - 80, H - 130);
            DrawSlide(g, currentSlide, menuContent, CGold, fadeAlpha);
            DrawLiveGazeEmotionOverlay(g, menuContent);
            DrawProgressDots(g, slideShow.CurrentIndex, slideShow.TotalSlides,
                W / 2, H - 26, CGoldLight);
        }

        if (circularMenu.IsVisible)
            circularMenu.Draw(g, W, H, themeSecondary, themeTertiary, fontSubtitle, fontSmall);

        DrawMuseumAppPhoneDownloadBanner(g);
        // Draw gesture overlay on top of everything
        DrawGestureOverlay(g);
    }

    /// <summary>YOLO detected a phone in frame — prompt visitor to install the companion app (URLs are constants above).</summary>
    private void DrawMuseumAppPhoneDownloadBanner(Graphics g)
    {
        if (!_showMuseumAppPhoneBanner) return;

        int padX = 24;
        int barH = 108;
        var rect = new Rectangle(padX, H - barH - 20, W - padX * 2, barH);
        using (var bg = new SolidBrush(Color.FromArgb(230, 18, 20, 28)))
            g.FillRectangle(bg, rect);
        using (var outline = new Pen(Color.FromArgb(240, themeSecondary), 2))
            g.DrawRectangle(outline, rect);

        var titleRect = new RectangleF(rect.X + 16, rect.Y + 10, rect.Width - 32, 34);
        var bodyRect = new RectangleF(rect.X + 16, rect.Y + 44, rect.Width - 32, rect.Height - 52);
        using (var titleBrush = new SolidBrush(themeSecondary))
        using (var bodyBrush = new SolidBrush(Color.FromArgb(235, 235, 240, 245)))
        using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near })
        {
            g.DrawString("Download the Museum app", fontSubtitle, titleBrush, titleRect, sf);
            string body = "We noticed a phone — get maps, audio guides, and favorites on your device.\r\n"
                + "iOS: " + MuseumAppStoreIosUrl + "\r\n"
                + "Android: " + MuseumAppStoreAndroidUrl;
            g.DrawString(body, fontSmall, bodyBrush, bodyRect, sf);
        }
    }

    private void DrawGestureOverlay(Graphics g)
    {
        // Check if we should display the gesture
        if (string.IsNullOrEmpty(lastDetectedGesture))
            return;
        
        double elapsedSeconds = (DateTime.Now - gestureDisplayTime).TotalSeconds;
        
        // Hide after 2 seconds
        if (elapsedSeconds > GESTURE_DISPLAY_DURATION)
        {
            lastDetectedGesture = null;
            return;
        }
        
        // Calculate fade-out alpha (fade in last 0.5 seconds)
        int alpha = 255;
        if (elapsedSeconds > GESTURE_DISPLAY_DURATION - 0.5)
        {
            double fadeProgress = (GESTURE_DISPLAY_DURATION - elapsedSeconds) / 0.5;
            alpha = (int)(255 * fadeProgress);
        }
        
        // Format gesture name for display (capitalize, replace underscores)
        string displayText = lastDetectedGesture.Replace("_", " ").ToUpper();
        
        // Rename gestures for better user understanding
        if (displayText == "SWIPEL" || displayText == "SWIPE LEFT")
            displayText = "SWIPE RIGHT";
        else if (displayText == "SWIPER" || displayText == "SWIPE RIGHT")
            displayText = "SWIPE LEFT";
        else if (displayText == "THUMBS" || displayText == "THUMBSUP" || displayText == "THUMBUP")
            displayText = "THUMBS UP";
        
        // Position in top-right corner
        int padding = 20;
        int boxWidth = 250;
        int boxHeight = 60;
        int x = W - boxWidth - padding;
        int y = padding;
        
        // Draw semi-transparent background
        using (var bgBrush = new SolidBrush(Color.FromArgb(alpha * 180 / 255, 12, 12, 12)))
        {
            g.FillRectangle(bgBrush, x, y, boxWidth, boxHeight);
        }
        
        // Draw border with theme color
        using (var borderPen = new Pen(Color.FromArgb(alpha, themeSecondary), 2))
        {
            g.DrawRectangle(borderPen, x, y, boxWidth, boxHeight);
        }
        
        // Draw gesture text
        using (var textBrush = new SolidBrush(Color.FromArgb(alpha, themeSecondary)))
        {
            StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            
            RectangleF textRect = new RectangleF(x, y, boxWidth, boxHeight);
            g.DrawString(displayText, fontSubtitle, textBrush, textRect, format);
        }
        
        // Request another redraw if still visible (for fade animation)
        if (elapsedSeconds < GESTURE_DISPLAY_DURATION)
        {
            Invalidate();
        }
    }

    private void DrawLoginScreen(Graphics g)
    {
        using (var veil = new SolidBrush(Color.FromArgb(158, 0, 0, 0)))
            g.FillRectangle(veil, 0, 0, W, H);

        DrawCentered(g, "Press L on this window to restart sign-in", fontSmall,
            Color.FromArgb(190, 218, 200, 150),
            new RectangleF(40, 8, W - 80, 22));

        if (loginPhase == LoginAuthPhase.ProfileWelcome)
        {
            DrawProfileWelcomeScreen(g);
            DrawOuterBorder(g);
            return;
        }

        string title = "SIGN IN";
        string subtitle = "Face sign-in and Bluetooth";
        if (loginPhase == LoginAuthPhase.MainPicker)
        {
            title = "WELCOME";
            subtitle = "We will open your webcam, then show who you are on this table. Follow the gold oval in the webcam window.";
        }
        else if (loginPhase == LoginAuthPhase.LoginScanning)
        {
            title = "LOGGING IN";
            subtitle = "Bluetooth — keep your phone or watch nearby, powered on, and discoverable while we verify.";
        }
        else if (loginPhase == LoginAuthPhase.RegisterScanning)
        {
            title = "FACE SIGN-IN";
            subtitle = "Use the small webcam window: line up with the gold oval and hold still. If you’re new here, you’ll see a short countdown before a photo is saved.";
        }
        else if (loginPhase == LoginAuthPhase.NoFaceRecovery)
        {
            title = "NO FACE FOUND";
            subtitle = "Make sure your face is in front of the camera";
        }
        else if (loginPhase == LoginAuthPhase.AuthCameraIssue)
        {
            title = "WEBCAM / SIGN-IN ERROR";
            subtitle = "This is usually the camera being busy, not your face. Read the message below.";
        }
        else if (loginPhase == LoginAuthPhase.RegisterDuplicateChoice)
            title = "FACE ALREADY REGISTERED";
        else if (loginPhase == LoginAuthPhase.RegisterBluetoothScanning)
            title = "REGISTER — BLUETOOTH";
        else if (loginPhase == LoginAuthPhase.RegisterBluetoothConfirm)
            title = "CONFIRM DEVICE";
        else if (loginPhase == LoginAuthPhase.LoginBluetoothRecovery)
        {
            title = "BLUETOOTH";
            subtitle = loginBluetoothRecoveryEscalated
                ? "Two unsuccessful tries — pick your next step"
                : "Verification did not complete — read the message below";
        }
        else if (loginPhase == LoginAuthPhase.RegisterBluetoothRecovery)
        {
            title = "BLUETOOTH";
            subtitle = "Could not find a device to pair";
        }
        else if (loginPhase == LoginAuthPhase.RegisterEnterFirstName)
            title = "FIRST NAME";
        else if (loginPhase == LoginAuthPhase.RegisterEnterLastName)
            title = "LAST NAME";
        else if (loginPhase == LoginAuthPhase.RegisterEnterAge)
            title = "AGE";
        else if (loginPhase == LoginAuthPhase.RegisterGenderPick)
            title = "GENDER";
        else if (loginPhase == LoginAuthPhase.RegisterRolePick)
            title = "SELECT ROLE";
        else if (loginPhase == LoginAuthPhase.RegisterSaving)
            title = "SAVING ACCOUNT";

        DrawCentered(g, title, fontTitle, themeSecondary,
            new RectangleF(0, H / 2f - 168f, W, 58));

        float subtitleTop = H / 2f - 108f;
        float subtitleH = 40f;
        if (loginPhase == LoginAuthPhase.RegisterScanning || loginPhase == LoginAuthPhase.MainPicker)
        {
            subtitleTop = H / 2f - 118f;
            subtitleH = 78f;
        }
        if (loginPhase == LoginAuthPhase.RegisterScanning || loginPhase == LoginAuthPhase.MainPicker)
            DrawWrappedCentered(g, subtitle, fontSubtitle, Color.FromArgb(238, CPapyrus),
                new RectangleF(48, subtitleTop, W - 96, subtitleH));
        else
            DrawCentered(g, subtitle, fontSubtitle, Color.FromArgb(238, CPapyrus),
                new RectangleF(40, subtitleTop, W - 80, subtitleH));

        float statusTop = subtitleTop + subtitleH + 12f;
        bool wrapStatus = loginPhase == LoginAuthPhase.LoginBluetoothRecovery ||
                          loginPhase == LoginAuthPhase.RegisterBluetoothRecovery ||
                          loginPhase == LoginAuthPhase.NoFaceRecovery ||
                          loginPhase == LoginAuthPhase.AuthCameraIssue;
        float statusH = wrapStatus
            ? (loginPhase == LoginAuthPhase.LoginBluetoothRecovery ? 120f
                : loginPhase == LoginAuthPhase.AuthCameraIssue ? 120f : 96f)
            : 52f;
        if (wrapStatus)
            DrawWrappedCentered(g, authStatus, fontBody, Color.White,
                new RectangleF(40, statusTop, W - 80, statusH));
        else
            DrawCentered(g, authStatus, fontBody, Color.White,
                new RectangleF(40, statusTop, W - 80, statusH));

        if (loginPhase == LoginAuthPhase.RegisterEnterFirstName ||
            loginPhase == LoginAuthPhase.RegisterEnterLastName ||
            loginPhase == LoginAuthPhase.RegisterEnterAge)
        {
            string preview = string.IsNullOrEmpty(nameEntryBuffer) ? "—" : nameEntryBuffer;
            if (preview.Length > 48) preview = preview.Substring(0, 45) + "...";
            DrawCentered(g, preview, fontSubtitle, CGoldLight,
                new RectangleF(40, H / 2f + 2, W - 80, 40));
        }
        else if (loginPhase == LoginAuthPhase.RegisterGenderPick &&
                 authRingItems != null && authRingItems.Count > 0 &&
                 authRingSelectedIndex >= 0 && authRingSelectedIndex < authRingItems.Count)
        {
            string gsel = authRingItems[authRingSelectedIndex];
            DrawCentered(g, "Selection: " + gsel, fontSubtitle, CGoldLight,
                new RectangleF(40, H / 2f + 2, W - 80, 36));
        }

        bool showRing = !authInProgress &&
            (loginPhase == LoginAuthPhase.NoFaceRecovery ||
             loginPhase == LoginAuthPhase.AuthCameraIssue ||
             loginPhase == LoginAuthPhase.RegisterDuplicateChoice ||
             loginPhase == LoginAuthPhase.RegisterBluetoothConfirm ||
             loginPhase == LoginAuthPhase.LoginBluetoothRecovery ||
             loginPhase == LoginAuthPhase.RegisterBluetoothRecovery ||
             loginPhase == LoginAuthPhase.RegisterEnterFirstName ||
             loginPhase == LoginAuthPhase.RegisterEnterLastName ||
             loginPhase == LoginAuthPhase.RegisterEnterAge ||
             loginPhase == LoginAuthPhase.RegisterGenderPick ||
             loginPhase == LoginAuthPhase.RegisterRolePick);

        if (showRing)
        {
            string hint = (loginPhase == LoginAuthPhase.LoginBluetoothRecovery &&
                          loginBluetoothRecoveryEscalated)
                ? "Rotate to choose  •  Flick UP confirms  •  Restart login = camera again, Guest visitor = no Bluetooth"
                : (loginPhase == LoginAuthPhase.LoginBluetoothRecovery ||
                          loginPhase == LoginAuthPhase.RegisterBluetoothRecovery ||
                          loginPhase == LoginAuthPhase.NoFaceRecovery ||
                          loginPhase == LoginAuthPhase.AuthCameraIssue)
                ? "Rotate marker to choose  •  Flick UP = confirm   Flick DOWN = restart scan"
                : (loginPhase == LoginAuthPhase.RegisterEnterFirstName ||
                          loginPhase == LoginAuthPhase.RegisterEnterLastName ||
                          loginPhase == LoginAuthPhase.RegisterEnterAge)
                ? "Flick UP = confirm / add   Flick DOWN = delete last or go back"
                : "Flick UP = confirm choice   Flick DOWN = go back";
            int ringR = (loginPhase == LoginAuthPhase.RegisterEnterFirstName ||
                         loginPhase == LoginAuthPhase.RegisterEnterLastName) ? 128 : 102;
            const int bottomMargin = 36;
            int minTop = (int)(H / 2f + 36f);
            int maxRadius = (H - bottomMargin - minTop) / 2 - 4;
            if (maxRadius > 52 && ringR > maxRadius)
                ringR = maxRadius;
            int ringCy = H - bottomMargin - ringR;
            if (ringCy - ringR < minTop)
                ringCy = minTop + ringR;
            if (ringCy + ringR > H - bottomMargin / 2)
                ringCy = H - bottomMargin / 2 - ringR;
            float ringTop = ringCy - ringR;
            const float hintBlockH = 52f;
            float hintTop = Math.Max(H / 2f + 40f, ringTop - hintBlockH - 10f);
            string sel = (authRingItems != null && authRingItems.Count > 0 &&
                          authRingSelectedIndex >= 0 && authRingSelectedIndex < authRingItems.Count)
                ? authRingItems[authRingSelectedIndex]
                : "—";
            float segDeg = (authRingItems != null && authRingItems.Count > 1)
                ? 360f / authRingItems.Count
                : 0f;
            string tuioHud = authTuioMarkerTracked
                ? (authRingItems != null && authRingItems.Count > 1)
                    ? string.Format("TUIO angle {0:0}° (~{1:0}° per choice) — now: {2}", authTuioMarkerAngleDeg, segDeg, sel)
                    : string.Format("TUIO angle {0:0}° — {1}", authTuioMarkerAngleDeg, sel)
                : "TUIO: place the menu marker (symbol " + TuioControlMarker.MenuAuthSymbolId + ") on the table — symbols 0 and 3 are not museum figures.";
            DrawWrappedCentered(g, hint + Environment.NewLine + tuioHud, fontSmall,
                Color.FromArgb(198, CPapyrus), new RectangleF(40, hintTop, W - 80, hintBlockH));
            DrawAuthTuioRing(g, W / 2, ringCy, ringR, themeSecondary, CGoldDim);
        }

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
            {
                DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
                DrawLiveGazeEmotionOverlay(g, contentArea);
            }
        }
        else if (hasSceneObjects)
        {
            bool isObjectStoryPlaying = lockedContext == SlideShowContext.SceneObjectStory && currentSlide != null;
            if (isObjectStoryPlaying)
            {
                // Match intro/relationship style: story slide only, no scene visible behind.
                DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
                DrawLiveGazeEmotionOverlay(g, contentArea);
            }
            else
            {
                DrawSingleFigureObjectScene(g, contentArea, activeFig);
            }
        }
        else if (currentSlide != null)
        {
            DrawSlide(g, currentSlide, contentArea, accent, fadeAlpha);
            DrawLiveGazeEmotionOverlay(g, contentArea);
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
        {
            DrawSlide(g, currentSlide, contentArea, CGold, fadeAlpha);
            DrawLiveGazeEmotionOverlay(g, contentArea);
        }

        DrawProgressDots(g, slideShow.CurrentIndex, slideShow.TotalSlides,
                         W / 2, H - 26, CGoldLight);
        DrawOuterBorder(g);
    }

    private static string FormatDominantEmotionLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Neutral";
        string t = raw.Trim();
        if (t.Length == 1) return t.ToUpperInvariant();
        return char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant();
    }

    private static string FormatGazeIssueHint(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return "No face — gesture_service.py often locks camera 0; stop it or set GAZE_EMOTION_CAMERA / GESTURE_CAMERA to different indices.";
        if (reason == "warmup")
            return "Camera starting…";
        if (reason == "no_face")
            return "No face detected — stop gesture_service if it uses the same webcam, try GAZE_EMOTION_MIRROR=1, or improve light.";
        if (reason == "mediapipe_missing")
            return "MediaPipe not installed in this Python env (pip install mediapipe).";
        if (reason.StartsWith("camera_failed:", StringComparison.Ordinal))
            return "Webcam open failed — try GAZE_EMOTION_CAMERA=1 or close apps using the camera.";
        if (string.Equals(reason, "camera_failed", StringComparison.Ordinal))
            return "Webcam open failed — try another GAZE_EMOTION_CAMERA index.";
        return reason;
    }

    /// <summary>Live gaze dot + dominant emotion on top of slideshow content (requires gaze_emotion_service).</summary>
    private void DrawLiveGazeEmotionOverlay(Graphics g, Rectangle contentArea)
    {
        if (slideShow == null || !slideShow.IsRunning || currentSlide == null) return;

        bool streamOn, valid;
        double gx, gy;
        string dom, issueHint;
        lock (liveGazeLock)
        {
            streamOn = liveGazeStreamActive;
            valid = liveGazeValid;
            gx = liveGazeGx;
            gy = liveGazeGy;
            dom = liveDominantEmotion;
            issueHint = liveGazeIssueUserText;
        }

        string emotionLine;
        if (!streamOn)
            emotionLine = "Dominant expression: — (start gaze_emotion_service.py on 127.0.0.1:5002)";
        else if (!valid)
            emotionLine = "Dominant expression: — " + (string.IsNullOrEmpty(issueHint)
                ? "No face yet (see gaze_emotion_service console)"
                : issueHint);
        else
            emotionLine = "Dominant expression: " + FormatDominantEmotionLabel(dom);
        var labelRect = new RectangleF(contentArea.X + 10, contentArea.Y + 10,
            Math.Max(40, contentArea.Width - 20), 36);
        using (var bg = new SolidBrush(Color.FromArgb(210, 12, 14, 20)))
            g.FillRectangle(bg, labelRect.X, labelRect.Y, labelRect.Width, labelRect.Height);
        using (var b = new SolidBrush(Color.FromArgb(245, 255, 248, 220)))
            g.DrawString(emotionLine, fontSmall, b, labelRect);

        if (!valid) return;

        double nx = Math.Max(0.0, Math.Min(1.0, gx));
        double ny = Math.Max(0.0, Math.Min(1.0, gy));
        int px = contentArea.X + (int)(nx * contentArea.Width);
        int py = contentArea.Y + (int)(ny * contentArea.Height);
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
            case ContentType.ThreeD: Draw3DSlide(g, slide as ThreeDObjectSlide, area, alpha); break;
        }
    }

    private void Draw3DSlide(Graphics g, ThreeDObjectSlide slide3d, Rectangle area, float alpha)
    {
        if (slide3d == null) return;

        // Advance idle rotation every frame (used when no hand is detected)
        _idleRotY += 0.008f;
        if (_idleRotY > (float)(2 * Math.PI)) _idleRotY -= (float)(2 * Math.PI);

        // Get current hand pose (null-safe: returns HandPose.Invalid when no client/hand)
        HandPose pose = (handTrackClient != null) ? handTrackClient.Current : HandPose.Invalid;

        ThreeDObjectRenderer.Draw(
            g,
            area,
            slide3d.GetMesh(),
            pose,
            _idleRotY,
            slide3d.AccentColor,
            alpha);

        // Caption
        if (!string.IsNullOrEmpty(slide3d.Caption))
        {
            var capRect = new RectangleF(area.X, area.Bottom - 48, area.Width, 42);
            using (var tbr = new SolidBrush(Color.FromArgb((int)(200 * alpha), slide3d.AccentColor)))
            using (var sf  = new StringFormat { Alignment = StringAlignment.Center,
                                               LineAlignment = StringAlignment.Center })
            {
                g.DrawString(slide3d.Caption, fontSmall, tbr, capRect, sf);
            }
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

    private void DrawTextSlide(Graphics g, string text, Rectangle area, Color accent, int alpha)
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

    private void DrawVideoSlide(Graphics g, string path, Rectangle area, Color accent, int alpha)
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

        float idealAngle = angle - deviation;
        float startDeg = (float)(idealAngle * 180f / Math.PI) - 15f;
        using (var p = new Pen(Color.FromArgb(100, CGreen), 3)
        { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(p, cx - (R + 10), cy - (R + 10), (R + 10) * 2, (R + 10) * 2,
                      startDeg, 30f);

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

    private void DrawWrappedCentered(Graphics g, string text, Font font, Color color, RectangleF bounds)
    {
        if (string.IsNullOrEmpty(text)) return;
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near,
            Trimming = StringTrimming.Word,
            FormatFlags = StringFormatFlags.LineLimit | StringFormatFlags.NoClip
        };
        using (var br = new SolidBrush(color))
            g.DrawString(text, font, br, bounds, sf);
    }

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
        if (o != null && TuioControlMarker.IsMenuAuthMarker(o.SymbolID))
            return "Menu / sign-in marker";

        if (o != null && TuioControlMarker.IsReservedEmptySlot(o.SymbolID))
            return "Reserved (unused)";

        if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
            return MuseumData.Figures[o.SymbolID].Name;
        return "Unknown";
    }

    private Color GetAccent(TuioObject o)
    {
        if (o != null && TuioControlMarker.IsMenuAuthMarker(o.SymbolID))
            return Color.FromArgb(120, 180, 240);

        if (o != null && TuioControlMarker.IsReservedEmptySlot(o.SymbolID))
            return Color.FromArgb(90, 90, 95);

        if (o != null && MuseumData.Figures.ContainsKey(o.SymbolID))
            return MuseumData.Figures[o.SymbolID].AccentColor;
        return CGold;
    }

    private void SafeInvalidate()
    {
        if (IsHandleCreated) this.BeginInvoke(new Action(this.Invalidate));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (this.ClientSize.Width > 0 && this.ClientSize.Height > 0)
        {
            W = this.ClientSize.Width;
            H = this.ClientSize.Height;
        }
    }

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
        adminAnalyticsVisible = false;
        if (adminAnalyticsPanel != null) adminAnalyticsPanel.Exit();
        analyticsRecorder.FlushAndSave();
        TeardownGazeAnalytics();

        animTimer.Stop();
        recognitionTimer.Stop();
        slideShow.Stop();
        
        // Clean up gesture client
        if (gestureCheckTimer != null)
        {
            gestureCheckTimer.Stop();
            gestureCheckTimer.Dispose();
        }
        if (gestureClient != null)
        {
            gestureClient.Dispose();
        }
        
        if (client != null)
        {
            client.removeTuioListener(this);
            client.disconnect();
        }
        Environment.Exit(0);
    }

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


