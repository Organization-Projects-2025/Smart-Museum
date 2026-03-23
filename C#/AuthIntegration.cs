using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;

public class SocketClient
{
    public NetworkStream stream;
    public TcpClient client;

    public bool connectToSocket(string host, int portNumber)
    {
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
        try
        {
            if (!msg.EndsWith("\n"))
                msg = msg + "\n";
            
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

public class VisitorProfile
{
    public string FaceUserId;
    public string FirstName;
    public string LastName;
    public int Age;
    public string Gender;
    public string Race;
    public string Language;
    public string BluetoothMacAddress;
    public string FaceImagePath;

    public Color PrimaryColor;
    public Color SecondaryColor;
    public Color TertiaryColor;

    public float TitleSizePx;
    public float SubtitleSizePx;
    public float BodySizePx;
    public float SmallSizePx;

    public string FullName
    {
        get { return (FirstName + " " + LastName).Trim(); }
    }

    public static List<VisitorProfile> LoadFromCsv(string csvPath)
    {
        var rows = new List<VisitorProfile>();
        if (!File.Exists(csvPath)) return rows;

        var lines = File.ReadAllLines(csvPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 7) continue;

            var profile = new VisitorProfile();
            profile.FaceUserId = parts[0].Trim();
            profile.FirstName = parts[1].Trim();
            profile.LastName = parts[2].Trim();
            profile.Age = int.Parse(parts[3]);
            profile.Gender = parts[4].Trim();
            profile.Race = parts[5].Trim();
            profile.BluetoothMacAddress = parts[6].Trim();
            profile.FaceImagePath = parts.Length > 7 ? parts[7].Trim() : string.Empty;
            profile.ApplyDerivedPreferences();
            rows.Add(profile);
        }
        return rows;
    }

    private void ApplyDerivedPreferences()
    {
        // Age -> font sizes
        if (Age >= 9 && Age <= 12)
        {
            BodySizePx = 22f;
            SmallSizePx = 20f;
            SubtitleSizePx = 30f;
            TitleSizePx = 52f;
        }
        else if (Age >= 65)
        {
            BodySizePx = 20f;
            SmallSizePx = 19f;
            SubtitleSizePx = 30f;
            TitleSizePx = 52f;
        }
        else
        {
            BodySizePx = 18f;
            SmallSizePx = 16f;
            SubtitleSizePx = 28f;
            TitleSizePx = 48f;
        }

        // Gender -> theme colors
        string g = (Gender ?? string.Empty).Trim().ToLowerInvariant();
        PrimaryColor = Color.FromArgb(12, 12, 12);
        if (g == "female")
        {
            SecondaryColor = Color.FromArgb(232, 185, 35);
            TertiaryColor = Color.FromArgb(65, 105, 163);
        }
        else
        {
            SecondaryColor = Color.FromArgb(212, 175, 55);
            TertiaryColor = Color.FromArgb(201, 166, 107);
        }

        // Race -> language
        string r = (Race ?? string.Empty).Trim().ToLowerInvariant();
        if (r == "black") Language = "Arabic";
        else if (r == "indian") Language = "Hindi";
        else if (r == "latino") Language = "Spanish";
        else Language = "English";
    }
}

public class BluetoothService
{
    public bool Verify(string targetMac, out string status)
    {
        status = "Running Bluetooth scan...";

        if (string.IsNullOrWhiteSpace(targetMac))
        {
            status = "Bluetooth 2FA failed. Expected user MAC is missing.";
            return false;
        }

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("localhost", 5000))
            {
                status = "Bluetooth 2FA failed. Cannot connect to server.";
                return false;
            }

            string command = "bluetooth_scan " + targetMac;
            client.sendMessage(command);

            string response = client.recieveMessage();
            client.closeConnection();

            if (response == null || response == "")
            {
                status = "Bluetooth 2FA failed. No response from server.";
                return false;
            }

            if (response.StartsWith("FOUND:"))
            {
                string[] parts = response.Split(':');
                string deviceName = parts.Length > 1 ? parts[1] : "Unknown";
                string mac = parts.Length > 2 ? parts[2] : targetMac;
                status = "Bluetooth verified: " + deviceName + " (MAC: " + mac + ")";
                return true;
            }

            if (response.StartsWith("NOT_FOUND"))
            {
                status = "Bluetooth 2FA failed. Expected MAC not found in discovered devices: " + targetMac;
                return false;
            }

            if (response.StartsWith("ERROR:"))
            {
                string errorMsg = response.Substring(6);
                status = "Bluetooth 2FA failed. " + errorMsg;
                return false;
            }

            status = "Bluetooth 2FA failed. Unknown response: " + response;
            return false;
        }
        catch (Exception ex)
        {
            status = "Bluetooth verify failed: " + ex.Message;
            return false;
        }
    }
}

public class FaceRecognitionService
{
    public bool Scan(out string userId, out string status)
    {
        userId = string.Empty;
        status = "Running Face ID scan...";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("localhost", 5000))
            {
                status = "Face ID failed. Cannot connect to server.";
                return false;
            }

            string command = "face_id_scan";
            string response = client.sendCommandAndWait(command);
            client.closeConnection();

            if (response == null || response == "")
            {
                status = "Face ID failed. No response from server.";
                return false;
            }

            if (response.StartsWith("FOUND:"))
            {
                userId = response.Substring(6);
                status = "Face ID verified: " + userId;
                return true;
            }

            if (response.StartsWith("NOT_FOUND"))
            {
                status = "Face ID failed. Face not recognized.";
                return false;
            }

            if (response.StartsWith("ERROR:"))
            {
                string errorMsg = response.Substring(6);
                status = "Face ID failed. " + errorMsg;
                return false;
            }

            status = "Face ID failed. Unknown response: " + response;
            return false;
        }
        catch (Exception ex)
        {
            status = "Face ID exception: " + ex.Message;
            return false;
        }
    }
}
