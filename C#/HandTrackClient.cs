using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

/// <summary>
/// Normalized hand pose received from hand_tracker_service.py (default port 5004).
/// All spatial fields are in 0–1 range unless noted.
/// </summary>
public struct HandPose
{
    /// <summary>Wrist X: 0 = camera-left, 1 = camera-right.</summary>
    public float X;
    /// <summary>Wrist Y: 0 = bottom, 1 = top  (already flipped by the Python service).</summary>
    public float Y;
    /// <summary>Estimated depth: 0 = hand very close (large), 1 = hand far (small).</summary>
    public float Z;
    /// <summary>True when a closed-fist "grab" gesture is detected.</summary>
    public bool Fist;
    /// <summary>True when a hand is visible in the current frame.</summary>
    public bool Valid;

    /// <summary>Static instance representing an invalid/unknown hand pose.</summary>
    public static readonly HandPose Invalid = new HandPose
    {
        X = 0.5f,
        Y = 0.5f,
        Z = 0.5f,
        Fist = false,
        Valid = false
    };
}

/// <summary>
/// Lightweight TCP client for hand_tracker_service.py (default port 5004).
/// Connects in the background, reads newline-delimited JSON frames at ~30 fps,
/// and applies lerp smoothing so the host can safely read <see cref="Current"/>
/// from the UI thread at any time.
/// </summary>
public class HandTrackClient : IDisposable
{
    // Smoothing: how quickly pose follows raw data.  0=frozen, 1=no smoothing.
    private const float SmoothFactor = 0.20f;

    public int Port { get; }

    /// <summary>True once the TCP connection is established.</summary>
    public bool IsConnected { get { return _connected; } }

    private HandPose _current = new HandPose { X = 0.5f, Y = 0.5f, Z = 0.5f };
    private readonly object _lock = new object();
    private volatile bool _connected;
    private volatile bool _disposed;
    private TcpClient _tcp;
    private Thread _thread;

    // -----------------------------------------------------------------------
    // Construction / connection
    // -----------------------------------------------------------------------

    public HandTrackClient(int port = 5004)
    {
        Port = port;
    }

    /// <summary>
    /// Establishes the TCP connection and starts the background reader thread.
    /// Returns false immediately if the service is not reachable.
    /// </summary>
    public bool Connect()
    {
        if (_connected || _disposed) return false;
        try
        {
            _tcp = new TcpClient();
            _tcp.ReceiveTimeout = 2000;
            _tcp.Connect("127.0.0.1", Port);
            _connected = true;

            var ns = _tcp.GetStream();
            // Tell the service to start streaming frames.
            var startBytes = Encoding.UTF8.GetBytes("START\n");
            ns.Write(startBytes, 0, startBytes.Length);

            _thread = new Thread(() => ReadLoop(ns))
            {
                IsBackground = true,
                Name = "HandTrackReader"
            };
            _thread.Start();
            return true;
        }
        catch
        {
            _connected = false;
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Latest smoothed hand pose.  Thread-safe; safe to call from the UI thread.
    /// </summary>
    public HandPose Current
    {
        get { lock (_lock) { return _current; } }
    }

    // -----------------------------------------------------------------------
    // Background reader
    // -----------------------------------------------------------------------

    private void ReadLoop(NetworkStream ns)
    {
        var raw = new byte[512];
        var line = new StringBuilder();

        try
        {
            while (!_disposed)
            {
                int n;
                try
                {
                    n = ns.Read(raw, 0, raw.Length);
                }
                catch (System.IO.IOException)
                {
                    break;  // service closed the connection or timeout
                }

                if (n == 0) break;

                line.Append(Encoding.UTF8.GetString(raw, 0, n));

                // Consume every complete JSON line.
                int nl;
                while ((nl = IndexOfNewline(line)) >= 0)
                {
                    string text = line.ToString(0, nl).Trim();
                    line.Remove(0, nl + 1);

                    if (text.Length == 0) continue;
                    TryApplyPose(text);
                }
            }
        }
        catch { /* swallow – connection lost */ }
        finally
        {
            _connected = false;
        }
    }

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '\n') return i;
        return -1;
    }

    private void TryApplyPose(string json)
    {
        try
        {
            var j = JObject.Parse(json);
            bool valid = j["valid"]?.ToObject<bool>() ?? false;

            lock (_lock)
            {
                _current.Valid = valid;
                if (!valid) return;

                float nx = j["wx"]?.ToObject<float>() ?? 0.5f;
                float ny = j["wy"]?.ToObject<float>() ?? 0.5f;
                float nz = j["wz"]?.ToObject<float>() ?? 0.5f;

                _current.X = Lerp(_current.X, nx, SmoothFactor);
                _current.Y = Lerp(_current.Y, ny, SmoothFactor);
                _current.Z = Lerp(_current.Z, nz, SmoothFactor);
                _current.Fist = j["fist"]?.ToObject<bool>() ?? false;
            }
        }
        catch { /* bad frame – skip */ }
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    // -----------------------------------------------------------------------
    // Disposal
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connected = false;
        try
        {
            var ns = _tcp?.GetStream();
            if (ns != null)
            {
                var quit = Encoding.UTF8.GetBytes("QUIT\n");
                ns.Write(quit, 0, quit.Length);
            }
        }
        catch { }
        try { _tcp?.Close(); } catch { }
    }
}
