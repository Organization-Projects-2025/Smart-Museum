using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

/// <summary>
/// Streams YOLO track summaries from python/server/yolo_context_service.py (default port 5003).
/// </summary>
public class YoloContextClient : IDisposable
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

    public event Action<YoloContextFrame> FrameReceived;

    public YoloContextClient(string host = "localhost", int port = 5003)
    {
        this.host = host;
        this.port = port;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            client = new TcpClient();
            await client.ConnectAsync(host, port).ConfigureAwait(false);
            netStream = client.GetStream();
            reader = new StreamReader(netStream, Encoding.UTF8, false, 4096, leaveOpen: true);
            writer = new StreamWriter(netStream, new UTF8Encoding(false)) { AutoFlush = true };
            pollTimer = new System.Windows.Forms.Timer { Interval = 50 };
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
            YoloContextFrame last = null;
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

    private static YoloContextFrame ParseFrame(string line)
    {
        try
        {
            var o = JObject.Parse(line);
            var f = new YoloContextFrame
            {
                Ok = o["ok"]?.ToObject<bool>() ?? false,
                Tms = o["t_ms"]?.ToObject<long>() ?? 0L
            };
            var arr = o["tracks"] as JArray;
            if (arr != null)
            {
                foreach (var tok in arr)
                {
                    var t = tok as JObject;
                    if (t == null) continue;
                    f.Tracks.Add(new YoloTrack
                    {
                        Id = t["id"]?.ToObject<int>() ?? 0,
                        ClassName = t["cls"]?.ToString() ?? "",
                        Cx = t["cx"]?.ToObject<double>() ?? 0.0,
                        Cy = t["cy"]?.ToObject<double>() ?? 0.0,
                        W = t["w"]?.ToObject<double>() ?? 0.0,
                        H = t["h"]?.ToObject<double>() ?? 0.0,
                        Conf = t["conf"]?.ToObject<double>() ?? 0.0
                    });
                }
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
            await reader.ReadLineAsync().ConfigureAwait(false);
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

public class YoloContextFrame
{
    public bool Ok;
    public long Tms;
    public List<YoloTrack> Tracks = new List<YoloTrack>();
}

public class YoloTrack
{
    public int Id;
    public string ClassName;
    public double Cx;
    public double Cy;
    public double W;
    public double H;
    public double Conf;
}
