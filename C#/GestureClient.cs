using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


/// <summary>
/// Client for communicating with Python Gesture Recognition Service
/// </summary>
public class GestureClient : IDisposable
{
    private TcpClient client;
    private NetworkStream stream;
    private StreamReader reader;       // line-buffered reader — guarantees complete JSON lines
    private bool isConnected = false;
    private string host;
    private int port;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public event EventHandler<GestureRecognizedEventArgs> GestureRecognized;
        public event EventHandler<string> StatusChanged;

        public bool IsConnected => isConnected;

        public GestureClient(string host = "127.0.0.1", int port = 5001)
        {
            this.host = host;
            this.port = port;
        }

        /// <summary>
        /// Connect to the Python gesture service
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                string[] connectHosts = host == "127.0.0.1"
                    ? new[] { "127.0.0.1", "localhost" }
                    : new[] { host, "127.0.0.1", "localhost" };

                Exception lastError = null;

                foreach (var connectHost in connectHosts)
                {
                    try
                    {
                        // Dispose previous connection cleanly
                        reader?.Dispose();
                        stream?.Dispose();
                        client?.Close();

                        client = new TcpClient();
                        client.NoDelay      = true;
                        client.SendTimeout    = 5000;
                        client.ReceiveTimeout = 5000;

                        StatusChanged?.Invoke(this, $"Connecting to {connectHost}:{port}...");
                        
                        // Add 3-second timeout to prevent UI freeze
                        var connectTask = client.ConnectAsync(connectHost, port);
                        if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                        {
                            throw new TimeoutException($"Connection to {connectHost}:{port} timed out after 3 seconds");
                        }
                        await connectTask; // Propagate any exception

                        stream     = client.GetStream();
                        reader     = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                        isConnected = true;

                        if (!await PingAsync())
                        {
                            throw new Exception("Connected but PING failed — check gesture_service.py is running");
                        }

                        StatusChanged?.Invoke(this, $"Connected to gesture service at {connectHost}:{port}");
                        Console.WriteLine($"[GestureClient] ✓✓✓ Connected to {connectHost}:{port}");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        StatusChanged?.Invoke(this, $"Connection attempt failed for {connectHost}:{port}: {ex.Message}");
                        Console.WriteLine($"[GestureClient] Connection to {connectHost}:{port} failed: {ex.Message}");
                    }
                }

                isConnected = false;
                Console.WriteLine($"[GestureClient] All connection attempts failed. Last error: {lastError?.Message}");
                StatusChanged?.Invoke(this, $"Connection failed: {lastError?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start tracking hand gestures
        /// </summary>
        public async Task<bool> StartTrackingAsync()
        {
            var response = await SendCommandAsync("START_TRACKING");
            return response != null && response["status"]?.ToString() == "ok";
        }

        /// <summary>
        /// Stop tracking and recognize the gesture.
        /// Use only when you explicitly want to halt the tracker (e.g., before face scan).
        /// For continuous polling, use RecognizeOnlyAsync instead.
        /// </summary>
        public async Task<GestureResult> StopAndRecognizeAsync()
        {
            await SendCommandAsync("STOP_TRACKING");
            return await RecognizeOnlyAsync();
        }

        /// <summary>
        /// Fetch the last detected gesture WITHOUT stopping the tracker.
        /// Use this in the continuous polling loop — keeps the camera running.
        /// </summary>
        public async Task<GestureResult> RecognizeOnlyAsync()
        {
            var response = await SendCommandAsync("RECOGNIZE");
            if (response == null)
                return new GestureResult { Gesture = null, Score = 0.0, Confidence = "low" };

            string st = response["status"]?.ToString() ?? "";
            // Legacy servers may still return status=cooldown with no gesture; treat as empty.
            if (st == "ok" || st == "cooldown")
            {
                var result = new GestureResult
                {
                    Gesture = response["gesture"]?.ToString(),
                    Score   = response["score"]?.ToObject<double>() ?? 0.0,
                    Confidence = response["confidence"]?.ToString() ?? "low"
                };

                if (!string.IsNullOrEmpty(result.Gesture))
                {
                    Console.WriteLine($"[GestureClient] \u2713 Gesture received: {result.Gesture} (score={result.Score:F3})");
                    GestureRecognized?.Invoke(this, new GestureRecognizedEventArgs(result));
                }
                else if (st == "cooldown")
                    Console.WriteLine("[GestureClient] RECOGNIZE: server cooldown (no gesture payload)");

                return result;
            }

            return new GestureResult { Gesture = null, Score = 0.0, Confidence = "low" };
        }

        /// <summary>
        /// Stop tracking and release the camera (shared hub / local OpenCV).
        /// Does not run RECOGNIZE — use before Face ID or when idle.
        /// </summary>
        public async Task StopTrackingSilentlyAsync()
        {
            await SendCommandAsync("STOP_TRACKING");
        }

        /// <summary>
        /// Pause gesture detection (keeps camera running, stops collecting frames)
        /// Use when TUIO objects are detected
        /// </summary>
        public async Task PauseDetectionAsync()
        {
            await SendCommandAsync("PAUSE_DETECTION");
        }

        /// <summary>
        /// Resume gesture detection after pause
        /// </summary>
        public async Task ResumeDetectionAsync()
        {
            await SendCommandAsync("RESUME_DETECTION");
        }

        /// <summary>
        /// Reset the gesture tracking
        /// </summary>
        public async Task ResetAsync()
        {
            await SendCommandAsync("RESET");
        }

        /// <summary>
        /// Get service status
        /// </summary>
        public async Task<ServiceStatus> GetStatusAsync()
        {
            var response = await SendCommandAsync("STATUS");

            if (response != null && response["status"]?.ToString() == "ok")
            {
                return new ServiceStatus
                {
                    IsTracking = response["tracking"]?.ToObject<bool>() ?? false,
                    PointsCollected = response["frames_collected"]?.ToObject<int>() ?? 0,
                    TemplatesLoaded = response["templates"]?.ToObject<int>() ?? 0,
                    LastGesture = response["last_gesture"]?.ToString(),
                    WaitingForMotion = response["waiting_for_motion"]?.ToObject<bool>() ?? false,
                    Capturing = response["capturing"]?.ToObject<bool>() ?? false,
                    ObjectVisible = response["object_visible"]?.ToObject<bool>() ?? false,
                    ClockPriorityActive = response["clock_priority_active"]?.ToObject<bool>() ?? false,
                    IdleCloseSec = response["idle_close_sec"]?.ToObject<double>() ?? 10.0,
                    SecondsSinceClock = response["seconds_since_clock"]?.ToObject<double?>()
                };
            }

            return null;
        }

        /// <summary>
        /// Ping the service to check if it's alive
        /// </summary>
        public async Task<bool> PingAsync()
        {
            var response = await SendCommandAsync("PING");
            return response != null && response["status"]?.ToString() == "ok";
        }

        /// <summary>
        /// Send a command to the Python service and read a single newline-terminated JSON response.
        /// Uses StreamReader so partial TCP packets are never incorrectly parsed.
        /// </summary>
        private async Task<JObject> SendCommandAsync(string command)
        {
            if (!isConnected || stream == null || reader == null)
            {
                StatusChanged?.Invoke(this, "Not connected to service");
                return null;
            }

            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Send command (newline is the Python server's delimiter)
                byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);

                // ReadLineAsync reads exactly one \n-terminated line — safe across packet splits
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    isConnected = false;
                    Console.WriteLine($"[GestureClient] Server closed connection (port {port}).");
                    StatusChanged?.Invoke(this, "Server closed connection");
                    return null;
                }

                return JObject.Parse(line.Trim());
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Communication error: {ex.Message}");
                Console.WriteLine($"[GestureClient:{port}] SendCommandAsync error ({command}): {ex.GetType().Name}: {ex.Message}");
                isConnected = false;
                return null;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            isConnected = false;
            try { reader?.Dispose(); } catch { }
            try { stream?.Dispose(); } catch { }
            try { client?.Close();  } catch { }
        }
    }

    /// <summary>
    /// Result of gesture recognition
    /// </summary>
    public class GestureResult
    {
        public string Gesture { get; set; }
        public double Score { get; set; }
        public string Confidence { get; set; }

        public bool IsValid => !string.IsNullOrEmpty(Gesture) && Score > 0.5;
    }

    /// <summary>
    /// Service status information
    /// </summary>
    public class ServiceStatus
    {
        public bool IsTracking { get; set; }
        public int PointsCollected { get; set; }
        public int TemplatesLoaded { get; set; }
        public string LastGesture { get; set; }
        /// <summary>Python service: hand visible but stroke not started (waiting for wrist movement).</summary>
        public bool WaitingForMotion { get; set; }
        /// <summary>Python service: movement threshold passed; points are being recorded.</summary>
        public bool Capturing { get; set; }
        /// <summary>YOLO watch service: clock/watch visible in the current frame.</summary>
        public bool ObjectVisible { get; set; }
        /// <summary>YOLO watch service: clock visible or within post-clock idle window.</summary>
        public bool ClockPriorityActive { get; set; }
        /// <summary>YOLO watch service: seconds without clock before hand gestures resume (YOLO_IDLE_CLOSE_SEC).</summary>
        public double IdleCloseSec { get; set; }
        /// <summary>YOLO: seconds since last stable clock detection (null if never seen).</summary>
        public double? SecondsSinceClock { get; set; }
    }

    /// <summary>
    /// Event args for gesture recognized event
    /// </summary>
    public class GestureRecognizedEventArgs : EventArgs
    {
        public GestureResult Result { get; }

        public GestureRecognizedEventArgs(GestureResult result)
        {
            Result = result;
        }
    }

