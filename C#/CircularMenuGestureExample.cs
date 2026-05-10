using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SmartMuseum
{
    /// <summary>
    /// Example: Integrating gesture recognition with a circular menu
    /// </summary>
    public class CircularMenuWithGestures
    {
        private GestureClient gestureClient;
        private Timer gestureTimer;
        private bool isGestureActive = false;

        // Your circular menu reference
        // private CircularMenu menu;

        public CircularMenuWithGestures()
        {
            InitializeGestureClient();
        }

        private async void InitializeGestureClient()
        {
            gestureClient = new GestureClient("127.0.0.1", 5001);

            // Subscribe to events
            gestureClient.GestureRecognized += OnGestureRecognized;
            gestureClient.StatusChanged += OnStatusChanged;

            // Connect to service
            bool connected = await gestureClient.ConnectAsync();
            if (connected)
            {
                Console.WriteLine("✓ Connected to gesture service");
                StartGestureDetection();
            }
            else
            {
                Console.WriteLine("✗ Failed to connect to gesture service");
                MessageBox.Show(
                    "Could not connect to gesture service.\n" +
                    "Please start the Python service first:\n" +
                    "python python/server/unified_museum_server.py",
                    "Gesture Service - Connection Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        /// <summary>
        /// Start continuous gesture detection
        /// </summary>
        private void StartGestureDetection()
        {
            gestureTimer = new Timer();
            gestureTimer.Interval = 100; // Check every 100ms
            gestureTimer.Tick += async (s, e) => await CheckForGesture();
            gestureTimer.Start();
        }

        /// <summary>
        /// Continuously check for gestures
        /// </summary>
        private async Task CheckForGesture()
        {
            if (isGestureActive)
                return;

            try
            {
                var status = await gestureClient.GetStatusAsync();

                // If hand is detected and we have enough frames (25 frames = ~0.4s at 60 FPS), recognize
                if (status != null && status.PointsCollected >= 25)
                {
                    isGestureActive = true;

                    // Stop tracking and recognize
                    var result = await gestureClient.StopAndRecognizeAsync();

                    if (result.IsValid)
                    {
                        HandleGesture(result.Gesture, result.Score);
                    }

                    // Reset and start tracking again
                    await gestureClient.ResetAsync();
                    await Task.Delay(500); // Small delay before restarting
                    await gestureClient.StartTrackingAsync();

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
                Console.WriteLine($"✗ Gesture check error: {ex.Message}");
                isGestureActive = false;
            }
        }

        /// <summary>
        /// Handle recognized gestures for circular menu
        /// </summary>
        private void HandleGesture(string gesture, double score)
        {
            Console.WriteLine($"Gesture detected: {gesture} (score: {score:F2})");

            switch (gesture.ToLower())
            {
                case "swipel":
                case "swipe_left":
                    // Navigate menu counter-clockwise
                    NavigateMenuLeft();
                    break;

                case "swiper":
                case "swipe_right":
                    // Navigate menu clockwise
                    NavigateMenuRight();
                    break;

                case "open":
                    // Expand menu or select item
                    SelectMenuItem();
                    break;

                case "close":
                    // Close menu or go back
                    CloseMenu();
                    break;

                default:
                    Console.WriteLine($"Unknown gesture: {gesture}");
                    break;
            }
        }

        /// <summary>
        /// Navigate circular menu to the left (counter-clockwise)
        /// </summary>
        private void NavigateMenuLeft()
        {
            Console.WriteLine("Action: Navigate menu LEFT");
            // menu.RotateCounterClockwise();
            // or
            // menu.SelectPreviousItem();
        }

        /// <summary>
        /// Navigate circular menu to the right (clockwise)
        /// </summary>
        private void NavigateMenuRight()
        {
            Console.WriteLine("Action: Navigate menu RIGHT");
            // menu.RotateClockwise();
            // or
            // menu.SelectNextItem();
        }

        /// <summary>
        /// Select the current menu item
        /// </summary>
        private void SelectMenuItem()
        {
            Console.WriteLine("Action: SELECT menu item");
            // menu.SelectCurrentItem();
            // or
            // menu.ExpandSubmenu();
        }

        /// <summary>
        /// Close the menu
        /// </summary>
        private void CloseMenu()
        {
            Console.WriteLine("Action: CLOSE menu");
            // menu.Close();
            // or
            // menu.GoBack();
        }

        /// <summary>
        /// Event handler for gesture recognized
        /// </summary>
        private void OnGestureRecognized(object sender, GestureRecognizedEventArgs e)
        {
            Console.WriteLine($"Gesture Event: {e.Result.Gesture} - {e.Result.Confidence}");
        }

        /// <summary>
        /// Event handler for status changes
        /// </summary>
        private void OnStatusChanged(object sender, string status)
        {
            Console.WriteLine($"Status: {status}");
        }

        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            gestureTimer?.Stop();
            gestureTimer?.Dispose();
            gestureClient?.Dispose();
        }
    }

    /// <summary>
    /// Alternative: Manual gesture detection (user-triggered)
    /// </summary>
    public class ManualGestureDetection
    {
        private GestureClient gestureClient;

        public async Task InitializeAsync()
        {
            gestureClient = new GestureClient();
            await gestureClient.ConnectAsync();
        }

        /// <summary>
        /// Call this when user starts performing a gesture
        /// (e.g., when hand enters detection zone)
        /// </summary>
        public async Task OnGestureStartAsync()
        {
            await gestureClient.StartTrackingAsync();
            Console.WriteLine("Gesture tracking started");
        }

        /// <summary>
        /// Call this when user finishes performing a gesture
        /// (e.g., after 2 seconds or when hand leaves zone)
        /// </summary>
        public async Task<string> OnGestureEndAsync()
        {
            var result = await gestureClient.StopAndRecognizeAsync();

            if (result.IsValid)
            {
                Console.WriteLine($"Recognized: {result.Gesture} ({result.Score:F2})");
                return result.Gesture;
            }

            return null;
        }
    }
}
