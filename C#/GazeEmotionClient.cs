using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

/// <summary>
/// Streams gaze + emotion JSON lines from gaze_emotion_service.py (port 5002).
/// Uses raw NetworkStream reads (like HandTrackClient) — reliable with Python's non-blocking send.
/// </summary>
public class GazeEmotionClient : IDisposable
{
    private readonly string host;
    private readonly int port;
    private TcpClient client;
    private NetworkStream netStream;
    private StreamWriter writer;
    private Thread readThread;
    private volatile bool streaming;
    private volatile bool disposed;
    private Control invokeTarget;

    private readonly object latestLock = new object();
    private GazeEmotionFrame latestFrame;
    private bool hasLatestFrame;
    private long latestSequence;
    private int okFramesLogged;

    public bool IsConnected { get; private set; }
    public bool IsStreaming { get { return streaming; } }
    public event Action<GazeEmotionFrame> FrameReceived;

    public GazeEmotionClient(string host = "127.0.0.1", int port = 5002, Control invokeTarget = null)
    {
        this.host = host;
        this.port = port;
        this.invokeTarget = invokeTarget;
    }

    /// <summary>Thread-safe copy of the most recent parsed frame (UI polls each anim tick).</summary>
    public bool TryGetLatestFrame(out GazeEmotionFrame frame, out long sequence)
    {
        lock (latestLock)
        {
            sequence = latestSequence;
            if (!hasLatestFrame)
            {
                frame = null;
                return false;
            }
            frame = CloneFrame(latestFrame);
            return true;
        }
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            client = new TcpClient();
            client.NoDelay = true;

            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
            {
                client?.Close();
                IsConnected = false;
                return false;
            }

            await connectTask.ConfigureAwait(false);
            netStream = client.GetStream();
            writer = new StreamWriter(netStream, new UTF8Encoding(false)) { AutoFlush = true };
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
            string reply = await SendCommandAndReadReplyAsync("PING").ConfigureAwait(false);
            return !string.IsNullOrEmpty(reply) &&
                   JObject.Parse(reply)["status"]?.ToString() == "ok";
        }
        catch { return false; }
    }

    public async Task<bool> StartStreamingAsync()
    {
        if (!IsConnected || disposed) return false;

        if (streaming)
            await StopStreamingAsync().ConfigureAwait(false);

        try
        {
            string ack = await SendCommandAndReadReplyAsync("STREAM").ConfigureAwait(false);
            if (string.IsNullOrEmpty(ack)) return false;
            if (JObject.Parse(ack)["status"]?.ToString() != "ok") return false;

            if (invokeTarget == null || !invokeTarget.IsHandleCreated)
            {
                var forms = Application.OpenForms;
                if (forms.Count > 0) invokeTarget = forms[0];
            }

            streaming = true;
            readThread = new Thread(ReadLoop) { IsBackground = true, Name = "GazeEmotionReader" };
            readThread.Start();
            return true;
        }
        catch { return false; }
    }

    private async Task<string> SendCommandAndReadReplyAsync(string command)
    {
        await writer.WriteLineAsync(command).ConfigureAwait(false);
        return await ReadOneLineAsync().ConfigureAwait(false);
    }

    private async Task<string> ReadOneLineAsync()
    {
        var sb = new StringBuilder();
        var buf = new byte[256];
        while (!disposed && netStream != null)
        {
            int n = await netStream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
            if (n <= 0) return null;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
            int nl = sb.ToString().IndexOf('\n');
            if (nl < 0) continue;
            string line = sb.ToString(0, nl).Trim();
            return line;
        }
        return null;
    }

    private void ReadLoop()
    {
        var buf = new byte[4096];
        var line = new StringBuilder();

        try
        {
            while (streaming && !disposed && netStream != null)
            {
                int n;
                try
                {
                    n = netStream.Read(buf, 0, buf.Length);
                }
                catch (IOException)
                {
                    break;
                }

                if (n <= 0) break;

                line.Append(Encoding.UTF8.GetString(buf, 0, n));

                int nl;
                while ((nl = IndexOfNewline(line)) >= 0)
                {
                    string text = line.ToString(0, nl).Trim();
                    line.Remove(0, nl + 1);
                    if (text.Length == 0) continue;

                    // Ignore command replies while streaming.
                    if (text.StartsWith("{\"status\"", StringComparison.Ordinal))
                        continue;

                    GazeEmotionFrame frame = ParseFrame(text);
                    if (frame == null) continue;

                    if (frame.Ok && ++okFramesLogged % 45 == 1)
                        Console.WriteLine("[GazeClient] received ok dominant=" + frame.Dominant);

                    lock (latestLock)
                    {
                        latestFrame = frame;
                        hasLatestFrame = true;
                        latestSequence++;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (!disposed)
                Console.WriteLine("[GazeClient] Read error: " + ex.Message);
        }
        finally
        {
            streaming = false;
        }
    }

    private static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++)
            if (sb[i] == '\n') return i;
        return -1;
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
                Ok       = o["ok"]?.ToObject<bool>()   ?? false,
                Tms      = o["t_ms"]?.ToObject<long>()  ?? 0L,
                Gx       = o["gx"]?.ToObject<double>()  ?? 0.5,
                Gy       = o["gy"]?.ToObject<double>()  ?? 0.5,
                Dominant = o["dominant"]?.ToString()    ?? "neutral",
                Reason   = reason
            };
            var em = o["emotions"] as JObject;
            if (em != null)
                foreach (var p in em.Properties())
                    f.Emotions[p.Name] = p.Value.ToObject<double>();
            return f;
        }
        catch { return null; }
    }

    private static GazeEmotionFrame CloneFrame(GazeEmotionFrame src)
    {
        if (src == null) return null;
        var copy = new GazeEmotionFrame
        {
            Ok = src.Ok,
            Tms = src.Tms,
            Gx = src.Gx,
            Gy = src.Gy,
            Dominant = src.Dominant,
            Reason = src.Reason
        };
        foreach (var kv in src.Emotions)
            copy.Emotions[kv.Key] = kv.Value;
        return copy;
    }

    public async Task StopStreamingAsync()
    {
        streaming = false;
        if (readThread != null)
        {
            try { readThread.Join(500); }
            catch { }
            readThread = null;
        }

        if (!IsConnected || disposed) return;
        try
        {
            await writer.WriteLineAsync("PAUSE").ConfigureAwait(false);
            await ReadOneLineAsync().ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        disposed = true;
        streaming = false;
        if (readThread != null)
        {
            try { readThread.Join(300); }
            catch { }
            readThread = null;
        }
        try { writer?.Dispose(); } catch { }
        try { netStream?.Dispose(); } catch { }
        try { client?.Close(); } catch { }
        IsConnected = false;
    }
}

public class GazeEmotionFrame
{
    public bool   Ok;
    public long   Tms;
    public double Gx;
    public double Gy;
    public string Dominant;
    public string Reason;
    public Dictionary<string, double> Emotions =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
}
