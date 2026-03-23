using System;
using System.Net.Sockets;
using System.Text;

// Simple Socket Client - follows pattern from Code Samples/CSharpClient.txt
public class SocketClient
{
    public NetworkStream stream;
    public TcpClient client;

    public bool connectToSocket(string host, int portNumber)
    {
        // Connect to Python server
        try
        {
            client = new TcpClient(host, portNumber);
            stream = client.GetStream();
            Console.WriteLine("Connection made with " + host);
            return true;
        }
        catch (System.Net.Sockets.SocketException e)
        {
            Console.WriteLine("Connection Failed: " + e.Message);
            return false;
        }
    }

    public void sendMessage(string msg)
    {
        // Send string message to server
        try
        {
            byte[] sendData = Encoding.UTF8.GetBytes(msg);
            stream.Write(sendData, 0, sendData.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Console.WriteLine("Send error: " + e.Message);
        }
    }

    public string recieveMessage()
    {
        // Receive string message from server
        try
        {
            byte[] receiveBuffer = new byte[1024];
            int bytesReceived = stream.Read(receiveBuffer, 0, 1024);
            string data = Encoding.UTF8.GetString(receiveBuffer, 0, bytesReceived);
            Console.WriteLine("Received: " + data);
            return data;
        }
        catch (Exception e)
        {
            Console.WriteLine("Receive error: " + e.Message);
            return null;
        }
    }

    public void closeConnection()
    {
        // Close the connection
        try
        {
            stream.Close();
            client.Close();
            Console.WriteLine("Connection closed");
        }
        catch (Exception e)
        {
            Console.WriteLine("Close error: " + e.Message);
        }
    }

    public string sendCommandAndWait(string cmd)
    {
        // Send command and wait for response
        try
        {
            sendMessage(cmd);
            return recieveMessage();
        }
        catch (Exception e)
        {
            Console.WriteLine("Command error: " + e.Message);
            return null;
        }
    }
}
