using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
            // Add newline if not present - many socket protocols expect line-terminated messages
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

public class CsvUserRecord
{
    public string FaceUserId;
    public string FirstName;
    public string LastName;
    public int Age;
    public string Gender;
    public string Race;
    public string PreferredBluetoothName;
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
    public string PreferredBluetoothName;

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
}

public static class CsvUserDatabase
{
    public static List<CsvUserRecord> Load(string csvPath)
    {
        EnsureCsvExists(csvPath);

        var rows = new List<CsvUserRecord>();
        var lines = File.ReadAllLines(csvPath);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;

            string[] parts = line.Split(',');
            if (parts.Length < 7) continue;

            int age;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out age))
                age = 25;

            var rec = new CsvUserRecord();
            rec.FaceUserId = parts[0].Trim();
            rec.FirstName = parts[1].Trim();
            rec.LastName = parts[2].Trim();
            rec.Age = age;
            rec.Gender = parts[4].Trim();
            rec.Race = parts[5].Trim();
            rec.PreferredBluetoothName = parts[6].Trim();
            rows.Add(rec);
        }
        return rows;
    }

    public static void EnsureCsvExists(string csvPath)
    {
        string dir = Path.GetDirectoryName(csvPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(csvPath)) return;

        var lines = new List<string>();
        lines.Add("face_user_id,first_name,last_name,age,gender,race,preferred_bluetooth_name");
        lines.Add("user0,Ali,Hassan,10,male,black,AliPhone");
        lines.Add("user1,Sara,Nabil,11,female,white,SaraWatch");
        lines.Add("user2,Omar,Farid,16,male,asian,OmarBuds");
        lines.Add("user3,Lina,Adel,22,female,indian,LinaPhone");
        lines.Add("user4,Mark,Stone,30,male,white,MarkLaptop");
        lines.Add("user5,Ana,Lopez,34,female,latino,AnaPhone");
        lines.Add("user6,Yousef,Kamal,45,male,indian,YousefCar");
        lines.Add("user7,Nour,Salem,52,female,asian,NourTablet");
        lines.Add("user8,Ibrahim,Saad,66,male,black,IbrahimPhone");
        lines.Add("user9,Mona,Fouad,71,female,white,MonaWatch");
        lines.Add("user10,Carlos,Diaz,28,male,latino,CarlosPhone");
        lines.Add("user11,Mei,Lin,67,female,asian,MeiPhone");

        File.WriteAllLines(csvPath, lines.ToArray());
    }
}

public static class ProfileMapper
{
    public static VisitorProfile ToVisitorProfile(CsvUserRecord rec)
    {
        var p = new VisitorProfile();
        p.FaceUserId = rec.FaceUserId;
        p.FirstName = rec.FirstName;
        p.LastName = rec.LastName;
        p.Age = rec.Age;
        p.Gender = rec.Gender;
        p.Race = rec.Race;
        p.PreferredBluetoothName = rec.PreferredBluetoothName;

        // Age -> font sizes
        if (p.Age >= 5 && p.Age <= 12)
        {
            p.BodySizePx = 22f;
            p.SmallSizePx = 20f;
            p.SubtitleSizePx = 30f;
            p.TitleSizePx = 52f;
        }
        else if (p.Age >= 65)
        {
            p.BodySizePx = 20f;
            p.SmallSizePx = 19f;
            p.SubtitleSizePx = 30f;
            p.TitleSizePx = 52f;
        }
        else
        {
            p.BodySizePx = 18f;
            p.SmallSizePx = 16f;
            p.SubtitleSizePx = 28f;
            p.TitleSizePx = 48f;
        }

        // Gender -> theme colors
        string g = (p.Gender ?? string.Empty).Trim().ToLowerInvariant();
        p.PrimaryColor = Color.FromArgb(12, 12, 12);
        if (g == "female")
        {
            p.SecondaryColor = Color.FromArgb(232, 185, 35);
            p.TertiaryColor = Color.FromArgb(65, 105, 163);
        }
        else
        {
            p.SecondaryColor = Color.FromArgb(212, 175, 55);
            p.TertiaryColor = Color.FromArgb(201, 166, 107);
        }

        // Race -> language
        string r = (p.Race ?? string.Empty).Trim().ToLowerInvariant();
        if (r == "black") p.Language = "Arabic";
        else if (r == "indian") p.Language = "Hindi";
        else if (r == "latino") p.Language = "Spanish";
        else p.Language = "English";

        return p;
    }
}

public class BluetoothTwoFactorService
{
    // Connect via socket to Python server running on localhost:5000
    // See python_server.py for server implementation
    
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
            // Create socket client and connect to Python server
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("localhost", 5000))
            {
                status = "Bluetooth 2FA failed. Cannot connect to server.";
                return false;
            }

            // Send bluetooth_scan command with target MAC
            string command = "bluetooth_scan " + targetMac;
            client.sendMessage(command);

            // Receive response: "FOUND:DeviceName:MAC" or "NOT_FOUND" or "ERROR:message"
            string response = client.recieveMessage();
            client.closeConnection();

            if (response == null || response == "")
            {
                status = "Bluetooth 2FA failed. No response from server.";
                return false;
            }

            // Parse response
            if (response.StartsWith("FOUND:"))
            {
                // Format: FOUND:DeviceName:MAC
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

// Face ID Service - connects via socket to Python server
public class FaceIdService
{
    // Connect to Python server running on localhost:5000
    // See python_server.py for server implementation (face_id_scan command)

    public bool Scan(out string userId, out string status)
    {
        userId = string.Empty;
        status = "Running Face ID scan...";

        try
        {
            // Create socket client and connect to Python server
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("localhost", 5000))
            {
                status = "Face ID failed. Cannot connect to server.";
                return false;
            }

            // Send face_id_scan command
            string command = "face_id_scan";
            string response = client.sendCommandAndWait(command);
            client.closeConnection();

            if (response == null || response == "")
            {
                status = "Face ID failed. No response from server.";
                return false;
            }

            // Parse response: "FOUND:user_id" or "NOT_FOUND" or "ERROR:message"
            if (response.StartsWith("FOUND:"))
            {
                // Format: FOUND:user_id
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
