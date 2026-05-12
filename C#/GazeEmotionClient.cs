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
/// Streams gaze + 7-emotion estimates from python/server/gaze_emotion_service.py (port 5002).
/// Frames are read on a background thread and marshalled to the UI thread via BeginInvoke.
/// 
/// The old poll-timer + DataAvailable approach was broken: StreamReader buffers data internally
/// so DataAvailable returns false even when frames are waiting. This version uses a blocking
/// ReadLine() loop on a background thread instead.
/// </summary>
public class GazeEmotionClient : IDisposable
{
    private readonly string host;
    private readonly int port;
    private TcpClient client;
    private NetworkStream netStream;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread readThread;
    private volatile bool streaming;
    private volatile bool disposed;
    private Control invokeTarget;

    public bool IsConnected { get; private set; }
    public event Action<GazeEmotionFrame> FrameReceived;

    public GazeEmotionClient(string host = "127.0.0.1", int port = 5002, Control invokeTarget = null)
    {
        this.host = host;
        this.port = port;
        this.invokeTarget = invokeTarget;
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            client = new TcpClient();
            client.NoDelay = true;
            
            // Add 3-second timeout to prevent UI freeze
            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
            {
                client?.Close();
                IsConnected = false;
                return false; // Timeout
            }
            
            await connectTask; // Propagate any exception
            netStream = client.GetStream();
            reader = new StreamReader(netStream, Encoding.UTF8, false, 4096, leaveOpen: true);
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
            await writer.WriteLineAsync("PING").ConfigureAwait(false);
            string line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) return false;
            return JObject.Parse(line)["status"]?.ToString() == "ok";
        }
        catch { return false; }
    }

    public async Task<bool> StartStreamingAsync()
    {
        if (!IsConnected || streaming) return false;
        try
        {
            await writer.WriteLineAsync("STREAM").ConfigureAwait(false);
            string ack = await reader.ReadLineAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(ack)) return false;
            if (JObject.Parse(ack)["status"]?.ToString() != "ok") return false;

            streaming = true;

            // Resolve invoke target from open forms if not provided
            if (invokeTarget == null || !invokeTarget.IsHandleCreated)
            {
                var forms = Application.OpenForms;
                if (forms.Count > 0) invokeTarget = forms[0];
            }

            readThread = new Thread(ReadLoop) { IsBackground = true, Name = "GazeEmotionReader" };
            readThread.Start();
            return true;
        }
        catch { return false; }
    }

    private void ReadLoop()
    {
        try
        {
            while (streaming && !disposed)
            {
                // Check if data is available with timeout to prevent indefinite blocking
                if (!netStream.DataAvailable)
                {
                    Thread.Sleep(50); // Small delay to prevent CPU spinning
                    continue;
                }
                
                string line = reader.ReadLine(); // blocks until a line arrives
                if (line == null) break;

                GazeEmotionFrame frame = ParseFrame(line);
                if (frame == null) continue;

                var handler = FrameReceived;
                if (handler == null) continue;

                if (invokeTarget != null && invokeTarget.IsHandleCreated && !invokeTarget.IsDisposed)
                {
                    try { invokeTarget.BeginInvoke(handler, frame); }
                    catch { /* form closing */ }
                }
                else
                {
                    handler(frame);
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

    public async Task StopStreamingAsync()
    {
        streaming = false;
        if (!IsConnected) return;
        try
        {
            await writer.WriteLineAsync("PAUSE").ConfigureAwait(false);
            await reader.ReadLineAsync().ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        disposed  = true;
        streaming = false;
        try { reader?.Dispose(); }    catch { }
        try { writer?.Dispose(); }    catch { }
        try { netStream?.Dispose(); } catch { }
        try { client?.Close(); }      catch { }
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
