// HandTrackingReceiver.cs
// TCP socket client that connects to the Python hand tracker and fires an event
// whenever a new hand state arrives.
//
// Usage:
//   var receiver = new HandTrackingReceiver("127.0.0.1", 5555);
//   receiver.HandDataReceived += hands => { /* hands is List<HandData> */ };
//   receiver.Start();
//   ...
//   receiver.Stop();

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace HandTracking
{
    public class HandTrackingReceiver
    {
        public event Action<List<HandData>> HandDataReceived;

        private readonly string _host;
        private readonly int    _port;
        private Thread          _thread;
        private volatile bool   _running;

        private static readonly DataContractJsonSerializer _serializer =
            new DataContractJsonSerializer(typeof(List<HandData>));

        public HandTrackingReceiver(string host = "127.0.0.1", int port = 5555)
        {
            _host = host;
            _port = port;
        }

        public void Start()
        {
            _running = true;
            _thread  = new Thread(ReceiveLoop) { IsBackground = true, Name = "HandTracker" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
        }

        private void ReceiveLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = new TcpClient();
                    client.Connect(_host, _port);
                    Console.WriteLine($"[HandTracking] Connected to {_host}:{_port}");

                    using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
                    {
                        while (_running)
                        {
                            string line = reader.ReadLine();
                            if (line == null) break;          // server closed
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            List<HandData> hands = Deserialize(line);
                            if (hands != null)
                                HandDataReceived?.Invoke(hands);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HandTracking] {ex.Message} — retrying in 2s...");
                    Thread.Sleep(2000);
                }
                finally
                {
                    client?.Close();
                }
            }
        }

        private static List<HandData> Deserialize(string json)
        {
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (List<HandData>)_serializer.ReadObject(ms);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HandTracking] Deserialize error: {ex.Message}");
                return null;
            }
        }
    }
}
