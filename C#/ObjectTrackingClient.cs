using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;


/// <summary>
/// Client for communicating with Python Object Tracking Service (port 5005).
/// 
/// Receives object detection events and swipe gestures:
/// - objectswiperight, objectswipeleft, objectswipeup, objectswipedown
/// - object_visible (bool flag for priority)
/// 
/// Compatible with GestureClient protocol for seamless integration.
/// </summary>
public class ObjectTrackingClient : IDisposable
{
    private TcpClient client;
    private NetworkStream stream;
    private StreamReader reader;
    private bool isConnected = false;
    private string host;
    private int port;

    public event EventHandler<ObjectGestureRecognizedEventArgs> ObjectGestureRecognized;
    public event EventHandler<bool> ObjectVisibilityChanged;
    public event EventHandler<string> StatusChanged;

    public bool IsConnected => isConnected;

    public ObjectTrackingClient(string host = "127.0.0.1", int port = 5005)
    {
        this.host = host;
        this.port = port;
    }

    /// <summary>
    /// Connect to the Python object tracking service
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
                    if (isConnected)
                        Disconnect();

                    client = new TcpClient();
                    await client.ConnectAsync(connectHost, port);

                    stream = client.GetStream();
                    stream.WriteTimeout = 2000;
                    // No ReadTimeout — ListenAsync uses ReadLineAsync which must not time out
                    // (YOLO inference can take >2s on first frame, killing the connection)

                    // Use StreamReader for line-buffered input (handles JSON line format)
                    reader = new StreamReader(stream, Encoding.UTF8, false, 4096);

                    isConnected = true;
                    Console.WriteLine($"[ObjectTrackingClient] Connected to {connectHost}:{port}");
                    StatusChanged?.Invoke(this, "Connected");

                    // Start listening loop
                    _ = ListenAsync();
                    return true;
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }

            throw lastError ?? new Exception("Could not connect to object tracking service");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ObjectTrackingClient] Connection failed: {e.Message}");
            StatusChanged?.Invoke(this, $"Connection failed: {e.Message}");
            isConnected = false;
            return false;
        }
    }

    /// <summary>
    /// Disconnect from the service
    /// </summary>
    public void Disconnect()
    {
        if (client != null)
        {
            isConnected = false;
            try { stream?.Close(); } catch { }
            try { reader?.Close(); } catch { }
            try { client?.Close(); } catch { }
            client = null;
        }
    }

    /// <summary>
    /// Send a command to the object tracking service
    /// </summary>
    private async Task SendCommandAsync(string command)
    {
        if (!isConnected) return;

        try
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ObjectTrackingClient] Send error: {e.Message}");
            Disconnect();
        }
    }

    /// <summary>
    /// Start tracking objects
    /// </summary>
    public async Task StartTrackingAsync()
    {
        await SendCommandAsync("START_TRACKING");
    }

    /// <summary>
    /// Stop tracking objects
    /// </summary>
    public async Task StopTrackingAsync()
    {
        await SendCommandAsync("STOP_TRACKING");
    }

    /// <summary>
    /// Get status from the service
    /// </summary>
    public async Task<ObjectTrackingStatus> GetStatusAsync()
    {
        try
        {
            await SendCommandAsync("STATUS");
            // Response will arrive asynchronously through ListenAsync
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ObjectTrackingClient] Status request error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Recognize last gesture (retrieves and clears it)
    /// </summary>
    public async Task<string> RecognizeGestureAsync()
    {
        try
        {
            await SendCommandAsync("RECOGNIZE");
            // Response will arrive asynchronously through ListenAsync
            return null;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[ObjectTrackingClient] Recognize error: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Listen for responses from the service
    /// </summary>
    private async Task ListenAsync()
    {
        while (isConnected)
        {
            try
            {
                string line = await reader.ReadLineAsync();
                if (line == null)
                {
                    Disconnect();
                    break;
                }

                try
                {
                    JObject response = JObject.Parse(line);

                    // Debug: log every response that has object_visible=true
                    if (response.TryGetValue("object_visible", out var dbgVis) && dbgVis.Value<bool>())
                        Console.WriteLine($"[ObjectTrackingClient] object_visible=TRUE received");
                    
                    // Parse gesture response
                    if (response.TryGetValue("gesture", out var gestureToken) && gestureToken.Type != JTokenType.Null)
                    {
                        string gesture = gestureToken.Value<string>();
                        if (!string.IsNullOrEmpty(gesture))
                        {
                            Console.WriteLine($"[ObjectTrackingClient] Gesture recognized: {gesture}");
                            ObjectGestureRecognized?.Invoke(this, new ObjectGestureRecognizedEventArgs { Gesture = gesture });
                        }
                    }
                    
                    // Parse visibility status
                    if (response.TryGetValue("object_visible", out var visToken))
                    {
                        bool visible = visToken.Value<bool>();
                        ObjectVisibilityChanged?.Invoke(this, visible);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed JSON
                }
            }
            catch (IOException)
            {
                Disconnect();
                break;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[ObjectTrackingClient] Listen error: {e.Message}");
                Disconnect();
                break;
            }
        }
    }

    public void Dispose()
    {
        Disconnect();
    }
}


/// <summary>
/// Event args for object gesture recognition
/// </summary>
public class ObjectGestureRecognizedEventArgs : EventArgs
{
    public string Gesture { get; set; }
}


/// <summary>
/// Status info from object tracking service
/// </summary>
public class ObjectTrackingStatus
{
    public string Status { get; set; }
    public bool Tracking { get; set; }
    public string LastGesture { get; set; }
    public int FramesCollected { get; set; }
    public int Templates { get; set; }
    public bool WaitingForMotion { get; set; }
    public bool Capturing { get; set; }
    public bool ObjectVisible { get; set; }
}
