using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;


/// <summary>
/// TCP client for the Python laser tracking server (port 5006).
/// Streams rating updates and raises events when the user holds a star rating.
/// </summary>
public class LaserRatingClient : IDisposable
{
    private TcpClient client;
    private NetworkStream stream;
    private StreamReader reader;
    private bool isConnected = false;
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    public event EventHandler<RatingUpdateEventArgs> RatingUpdate;
    public event EventHandler<RatingConfirmedEventArgs> RatingConfirmed;
    public event EventHandler<string> StatusChanged;

    public bool IsConnected => isConnected;

    public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 5006)
    {
        try
        {
            reader?.Dispose();
            stream?.Dispose();
            client?.Close();

            client = new TcpClient();
            client.NoDelay = true;
            client.SendTimeout = 5000;
            client.ReceiveTimeout = 5000;

            StatusChanged?.Invoke(this, $"Connecting to laser server {host}:{port}...");

            var connectTask = client.ConnectAsync(host, port);
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask)
                throw new TimeoutException("Connection to laser server timed out");

            await connectTask;

            stream = client.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
            isConnected = true;

            if (!await PingAsync())
            {
                isConnected = false;
                throw new Exception("PING failed");
            }

            StatusChanged?.Invoke(this, "Connected to laser server");
            Console.WriteLine("[LaserRatingClient] Connected to laser server on :5006");
            return true;
        }
        catch (Exception ex)
        {
            isConnected = false;
            StatusChanged?.Invoke(this, $"Connection failed: {ex.Message}");
            Console.WriteLine($"[LaserRatingClient] Connection failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Start laser tracking. The server will stream rating updates.
    /// </summary>
    public async Task<bool> StartRatingAsync()
    {
        var response = await SendCommandAsync("START");
        if (response?["status"]?.ToString() == "ok")
        {
            _ = Task.Run(() => StreamUpdatesAsync());
            return true;
        }
        return false;
    }

    /// <summary>
    /// Stop laser tracking.
    /// </summary>
    public async Task StopRatingAsync()
    {
        await SendCommandAsync("STOP");
    }

    public async Task<bool> PingAsync()
    {
        var response = await SendCommandAsync("PING");
        return response?["status"]?.ToString() == "ok";
    }

    private async Task StreamUpdatesAsync()
    {
        try
        {
            while (isConnected && stream != null && reader != null)
            {
                string line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch
                {
                    break;
                }

                if (line == null)
                {
                    isConnected = false;
                    break;
                }

                try
                {
                    var json = JObject.Parse(line.Trim());
                    string type = json["type"]?.ToString();

                    if (type == "update")
                    {
                        int rating = json["rating"]?.ToObject<int>() ?? 0;
                        double progress = json["hold_progress"]?.ToObject<double>() ?? 0.0;
                        RatingUpdate?.Invoke(this, new RatingUpdateEventArgs(rating, (float)progress));
                    }
                    else if (type == "confirmed")
                    {
                        int rating = json["rating"]?.ToObject<int>() ?? 0;
                        RatingConfirmed?.Invoke(this, new RatingConfirmedEventArgs(rating));
                        break;
                    }
                    else if (type == "stopped")
                    {
                        break;
                    }
                }
                catch
                {
                    // ignore malformed JSON
                }
            }
        }
        catch
        {
            // stream ended
        }
    }

    private async Task<JObject> SendCommandAsync(string command)
    {
        if (!isConnected || stream == null || reader == null)
            return null;

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            byte[] data = Encoding.UTF8.GetBytes(command + "\n");
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);

            string line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
            {
                isConnected = false;
                return null;
            }

            return JObject.Parse(line.Trim());
        }
        catch
        {
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
        try { client?.Close(); } catch { }
    }
}

public class RatingUpdateEventArgs : EventArgs
{
    public int Rating { get; }
    public float HoldProgress { get; }

    public RatingUpdateEventArgs(int rating, float holdProgress)
    {
        Rating = rating;
        HoldProgress = holdProgress;
    }
}

public class RatingConfirmedEventArgs : EventArgs
{
    public int Rating { get; }

    public RatingConfirmedEventArgs(int rating)
    {
        Rating = rating;
    }
}
