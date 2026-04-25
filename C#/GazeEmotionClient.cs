using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

/// <summary>
/// Streams gaze + 7-emotion estimates from python/server/gaze_emotion_service.py (default port 5002).
/// </summary>
public class GazeEmotionClient : IDisposable
{
    private readonly string host;
    private readonly int port;
    private TcpClient client;
    private NetworkStream netStream;
    private StreamReader reader;
    private StreamWriter writer;
    private System.Windows.Forms.Timer pollTimer;
    private volatile bool streaming;

    public bool IsConnected { get; private set; }

    public event Action<GazeEmotionFrame> FrameReceived;

    public GazeEmotionClient(string host = "localhost", int port = 5002)
    {
        this.host = host;
        this.port = port;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync(host, port).ConfigureAwait(false);
            netStream = client.GetStream();
            reader = new StreamReader(netStream, Encoding.UTF8, false, 4096, leaveOpen: true);
            writer = new StreamWriter(netStream, new UTF8Encoding(false)) { AutoFlush = true };
            pollTimer = new System.Windows.Forms.Timer { Interval = 40 };
            pollTimer.Tick += OnPollTick;
            IsConnected = true;
            return true;
        }
        catch
        {
            IsConnected = false;
            return false;
        }
    }

    public async Task<bool> PingAsync()
    {
        if (!IsConnected) return false;
        try
        {
            await writer.WriteLineAsync("PING").ConfigureAwait(false);
            string line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) return false;
            var o = JObject.Parse(line);
            return o["status"]?.ToString() == "ok";
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> StartStreamingAsync()
    {
        if (!IsConnected || streaming) return false;
        try
        {
            await writer.WriteLineAsync("STREAM").ConfigureAwait(false);
            string ack = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(ack)) return false;
            var ackObj = JObject.Parse(ack);
            if (ackObj["status"]?.ToString() != "ok") return false;
            streaming = true;
            pollTimer.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnPollTick(object sender, EventArgs e)
    {
        if (!streaming || netStream == null || !netStream.DataAvailable) return;
        try
        {
            GazeEmotionFrame last = null;
            while (netStream.DataAvailable)
            {
                string line = reader.ReadLine();
                if (line == null) break;
                last = ParseFrame(line);
            }
            if (last != null && FrameReceived != null)
                FrameReceived(last);
        }
        catch
        {
            streaming = false;
            pollTimer.Stop();
        }
    }

    private static GazeEmotionFrame ParseFrame(string line)
    {
        try
        {
            var o = JObject.Parse(line);
            string reason = o["reason"]?.ToString();
            if (reason == "camera_failed" && o["detail"] != null)
                reason = reason + ":" + o["detail"].ToString();

            var f = new GazeEmotionFrame
            {
                Ok = o["ok"]?.ToObject<bool>() ?? false,
                Tms = o["t_ms"]?.ToObject<long>() ?? 0L,
                Gx = o["gx"]?.ToObject<double>() ?? 0.5,
                Gy = o["gy"]?.ToObject<double>() ?? 0.5,
                Dominant = o["dominant"]?.ToString() ?? "neutral",
                Reason = reason
            };
            var em = o["emotions"] as JObject;
            if (em != null)
            {
                foreach (var p in em.Properties())
                    f.Emotions[p.Name] = p.Value.ToObject<double>();
            }
            return f;
        }
        catch
        {
            return null;
        }
    }

    public async Task StopStreamingAsync()
    {
        pollTimer?.Stop();
        if (!IsConnected || !streaming)
        {
            streaming = false;
            return;
        }
        streaming = false;
        try
        {
            while (netStream != null && netStream.DataAvailable)
                reader.ReadLine();
            await writer.WriteLineAsync("PAUSE").ConfigureAwait(false);
            string line = await reader.ReadLineAsync().ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        streaming = false;
        if (pollTimer != null)
        {
            pollTimer.Stop();
            pollTimer.Tick -= OnPollTick;
            pollTimer.Dispose();
            pollTimer = null;
        }
        reader?.Dispose();
        writer?.Dispose();
        netStream?.Dispose();
        client?.Close();
        IsConnected = false;
    }
}

public class GazeEmotionFrame
{
    public bool Ok;
    public long Tms;
    public double Gx;
    public double Gy;
    public string Dominant;
    /// <summary>Optional server hint when Ok is false (e.g. no_face, warmup, mediapipe_missing).</summary>
    public string Reason;
    public Dictionary<string, double> Emotions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}
