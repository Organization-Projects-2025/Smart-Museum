using System;
using System.Net.Sockets;
using System.Text;

// This class is copied from Code Samples/CSharpClient.txt and kept simple.
public class CodeSampleSocketClient
{
    public class Client
    {
        int byteCT;
        public NetworkStream stream;
        byte[] sendData;
        public TcpClient client;

        public bool connectToSocket(string host, int portNumber)
        {
            try
            {
                client = new TcpClient(host, portNumber);
                stream = client.GetStream();
                Console.WriteLine("connection made ! with " + host);
                return true;
            }
            catch (System.Net.Sockets.SocketException e)
            {
                Console.WriteLine("Connection Failed: " + e.Message);
                return false;
            }
        }

        public string recieveMessage()
        {
            try
            {
                byte[] receiveBuffer = new byte[1024];
                int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
                Console.WriteLine(bytesReceived);
                string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
                Console.WriteLine(data);
                return data;
            }
            catch (System.Exception)
            {
            }

            return null;
        }
    }
}
