using System;
using System.Net.Sockets;
using System.Text;
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
    private bool isConnected = false;
    private string host;
    private int port;

        public event EventHandler<GestureRecognizedEventArgs> GestureRecognized;
        public event EventHandler<string> StatusChanged;

        public bool IsConnected => isConnected;

        public GestureClient(string host = "localhost", int port = 5001)
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
                client = new TcpClient();
                await client.ConnectAsync(host, port);
                stream = client.GetStream();
                isConnected = true;

                StatusChanged?.Invoke(this, "Connected to gesture service");
                return true;
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
        /// Stop tracking and recognize the gesture
        /// </summary>
        public async Task<GestureResult> StopAndRecognizeAsync()
        {
            // Stop tracking
            await SendCommandAsync("STOP_TRACKING");

            // Recognize gesture
            var response = await SendCommandAsync("RECOGNIZE");

            if (response != null && response["status"]?.ToString() == "ok")
            {
                var result = new GestureResult
                {
                    Gesture = response["gesture"]?.ToString(),
                    Score = response["score"]?.ToObject<double>() ?? 0.0,
                    Confidence = response["confidence"]?.ToString() ?? "low"
                };

                // Fire event if gesture detected
                if (!string.IsNullOrEmpty(result.Gesture))
                {
                    GestureRecognized?.Invoke(this, new GestureRecognizedEventArgs(result));
                }

                return result;
            }

            return new GestureResult { Gesture = null, Score = 0.0, Confidence = "low" };
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
                    PointsCollected = response["points"]?.ToObject<int>() ?? 0,
                    TemplatesLoaded = response["templates"]?.ToObject<int>() ?? 0,
                    LastGesture = response["last_gesture"]?.ToString(),
                    WaitingForMotion = response["waiting_for_motion"]?.ToObject<bool>() ?? false,
                    Capturing = response["capturing"]?.ToObject<bool>() ?? false
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
        /// Send a command to the Python service
        /// </summary>
        private async Task<JObject> SendCommandAsync(string command)
        {
            if (!isConnected || stream == null)
            {
                StatusChanged?.Invoke(this, "Not connected to service");
                return null;
            }

            try
            {
                // Send command
                byte[] data = Encoding.UTF8.GetBytes(command + "\n");
                await stream.WriteAsync(data, 0, data.Length);

                // Read response
                byte[] buffer = new byte[4096];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                // Parse JSON
                return JObject.Parse(response);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Communication error: {ex.Message}");
                isConnected = false;
                return null;
            }
        }

        public void Dispose()
        {
            stream?.Close();
            client?.Close();
            isConnected = false;
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

