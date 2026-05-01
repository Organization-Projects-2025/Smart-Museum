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

    /// <summary>visitor (default) or admin from users.csv; guest sessions use Role visitor plus GuestSession=true.</summary>
    public string Role { get; set; }

    /// <summary>In-memory only: guest entry without Bluetooth (distinct theme, not written to users.csv).</summary>
    public bool GuestSession { get; set; }

    public bool IsAdmin
    {
        get { return string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>Guest table session: same museum access pattern as visitor, with guest-only colors and typography.</summary>
    public static VisitorProfile CreateGuestVisitor()
    {
        var p = new VisitorProfile
        {
            GuestSession = true,
            FaceUserId = "guest",
            FirstName = "Guest",
            LastName = "Visitor",
            Age = 28,
            Gender = "other",
            Race = "other",
            Language = "English",
            BluetoothMacAddress = "0",
            FaceImagePath = "",
            Role = "visitor"
        };
        p.ApplyDerivedPreferences();
        return p;
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
            profile.Role = parts.Length > 8 ? parts[8].Trim() : "visitor";
            if (string.IsNullOrEmpty(profile.Role)) profile.Role = "visitor";
            profile.ApplyDerivedPreferences();
            rows.Add(profile);
        }
        return rows;
    }

    /// <summary>YOLO / camera context: phone, book, or large person bbox — nudges typography and warmth.</summary>
    public bool YoloPhoneNearby;
    public bool YoloBookNearby;
    public bool YoloLargePersonNearby;

    /// <returns>True if flags changed and derived preferences were recomputed.</returns>
    public bool SetCameraAmbientContext(bool phone, bool book, bool personLarge)
    {
        if (GuestSession)
            return false;
        if (YoloPhoneNearby == phone && YoloBookNearby == book && YoloLargePersonNearby == personLarge)
            return false;
        YoloPhoneNearby = phone;
        YoloBookNearby = book;
        YoloLargePersonNearby = personLarge;
        ApplyDerivedPreferences();
        return true;
    }

    public void ClearCameraAmbientContext()
    {
        YoloPhoneNearby = false;
        YoloBookNearby = false;
        YoloLargePersonNearby = false;
        ApplyDerivedPreferences();
    }

    private static Color BlendRgb(Color a, Color b, float t)
    {
        if (t <= 0f) return a;
        if (t >= 1f) return b;
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static float ClampFont(float v, float min, float max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    public void ApplyDerivedPreferences()
    {
        if (GuestSession)
        {
            PrimaryColor = Color.FromArgb(16, 36, 52);
            SecondaryColor = Color.FromArgb(110, 192, 178);
            TertiaryColor = Color.FromArgb(238, 156, 112);
            Language = "English";
            TitleSizePx = ClampFont(52f, 44f, 58f);
            SubtitleSizePx = ClampFont(32f, 26f, 36f);
            BodySizePx = ClampFont(23f, 18f, 28f);
            SmallSizePx = ClampFont(18f, 15f, 24f);
            return;
        }

        // --- Age rubric → font sizes (broader bands for accessibility) ---
        if (Age <= 8)
        {
            BodySizePx = 24f;
            SmallSizePx = 22f;
            SubtitleSizePx = 34f;
            TitleSizePx = 56f;
        }
        else if (Age >= 9 && Age <= 12)
        {
            BodySizePx = 22f;
            SmallSizePx = 20f;
            SubtitleSizePx = 30f;
            TitleSizePx = 52f;
        }
        else if (Age >= 13 && Age <= 17)
        {
            BodySizePx = 20f;
            SmallSizePx = 17f;
            SubtitleSizePx = 29f;
            TitleSizePx = 50f;
        }
        else if (Age >= 18 && Age <= 54)
        {
            BodySizePx = 18f;
            SmallSizePx = 16f;
            SubtitleSizePx = 28f;
            TitleSizePx = 48f;
        }
        else if (Age >= 55 && Age <= 64)
        {
            BodySizePx = 19f;
            SmallSizePx = 17f;
            SubtitleSizePx = 29f;
            TitleSizePx = 50f;
        }
        else
        {
            BodySizePx = 21f;
            SmallSizePx = 19f;
            SubtitleSizePx = 31f;
            TitleSizePx = 54f;
        }

        // --- Gender rubric → gold / accent (distinct paths for female / male / other) ---
        string g = (Gender ?? string.Empty).Trim().ToLowerInvariant();
        PrimaryColor = Color.FromArgb(10, 10, 14);
        if (g == "female")
        {
            SecondaryColor = Color.FromArgb(232, 185, 55);
            TertiaryColor = Color.FromArgb(72, 118, 178);
        }
        else if (g == "other" || g == "nonbinary" || g == "nb")
        {
            SecondaryColor = Color.FromArgb(205, 165, 225);
            TertiaryColor = Color.FromArgb(130, 95, 200);
        }
        else
        {
            SecondaryColor = Color.FromArgb(212, 175, 55);
            TertiaryColor = Color.FromArgb(175, 145, 85);
        }

        // --- Race rubric → language + subtle accent tint (still readable on dark bg) ---
        string r = (Race ?? string.Empty).Trim().ToLowerInvariant();
        if (r == "black") Language = "Arabic";
        else if (r == "indian") Language = "Hindi";
        else if (r == "latino") Language = "Spanish";
        else if (r == "asian") Language = "English";
        else Language = "English";

        Color raceWarm = Color.FromArgb(230, 200, 140);
        Color raceCool = Color.FromArgb(120, 170, 210);
        if (r == "latino" || r == "black")
            TertiaryColor = BlendRgb(TertiaryColor, raceWarm, 0.12f);
        else if (r == "asian" || r == "indian")
            TertiaryColor = BlendRgb(TertiaryColor, raceCool, 0.10f);
        else if (r == "white")
            TertiaryColor = BlendRgb(TertiaryColor, Color.FromArgb(200, 195, 175), 0.06f);

        // --- Camera / YOLO context layer (museum: reading device, guide, distance) ---
        if (YoloBookNearby)
        {
            SecondaryColor = BlendRgb(SecondaryColor, Color.FromArgb(255, 210, 150), 0.14f);
            BodySizePx += 1.5f;
            SmallSizePx += 1f;
        }
        if (YoloPhoneNearby)
        {
            BodySizePx += 2f;
            SmallSizePx += 1.5f;
            SubtitleSizePx += 1f;
        }
        if (YoloLargePersonNearby)
        {
            TitleSizePx += 3f;
            SubtitleSizePx += 2.5f;
            BodySizePx += 1f;
        }

        TitleSizePx = ClampFont(TitleSizePx, 40f, 62f);
        SubtitleSizePx = ClampFont(SubtitleSizePx, 24f, 38f);
        BodySizePx = ClampFont(BodySizePx, 16f, 28f);
        SmallSizePx = ClampFont(SmallSizePx, 14f, 26f);
    }
}

public class BluetoothService
{
    /// <summary>Check if a user requires mandatory Bluetooth verification.</summary>
    /// <returns>True if user has a registered MAC address (not "0" or empty), False otherwise.</returns>
    public static bool RequiresBluetoothVerification(VisitorProfile profile)
    {
        if (profile == null) return false;
        return !string.IsNullOrWhiteSpace(profile.BluetoothMacAddress) && profile.BluetoothMacAddress != "0";
    }

    /// <summary>Check if a user requires mandatory Bluetooth verification.</summary>
    /// <returns>True if MAC address is registered (not "0" or empty), False otherwise.</returns>
    public static bool RequiresBluetoothVerification(string macAddress)
    {
        return !string.IsNullOrWhiteSpace(macAddress) && macAddress != "0";
    }

    /// <summary>Turns Python Bluetooth exception text into a short, user-readable message.</summary>
    public static string FriendlyBluetoothError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Bluetooth could not be checked. Try again in a moment.";
        string s = raw.Trim();
        if (s.IndexOf("POWERED_OFF", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("radio is not powered", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("BluetoothUnavailableError", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("BleakBluetoothNotAvailableReason", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("Bluetooth is not turned on", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Bluetooth is off on this computer. Turn it on in Windows settings, then try again.";
        if (s.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
            && s.IndexOf("bluetooth", StringComparison.OrdinalIgnoreCase) >= 0)
            return "This PC does not support the Bluetooth scan used for login.";
        int cut = s.IndexOf('(');
        if (cut > 12 && cut < 100 && s.Length > cut + 40)
            s = s.Substring(0, cut).Trim().TrimEnd(',');
        if (s.Length > 180)
            s = s.Substring(0, 177).Trim() + "...";
        return s;
    }

    public bool Verify(string targetMac, out string status)
    {
        // MANDATORY Bluetooth verification for users with registered MAC addresses
        // SKIPPED completely for users without MAC addresses (MAC = "0" or empty)

        // Case 1: User has no Bluetooth device registered - skip verification
        if (string.IsNullOrWhiteSpace(targetMac) || targetMac == "0")
        {
            status = "Bluetooth verification skipped (no device registered)";
            return true; // Allow login without Bluetooth
        }

        // Case 2: User has registered Bluetooth device - MANDATORY verification
        status = "Verifying your Bluetooth device…";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("127.0.0.1", 5000))
            {
                status = "Cannot reach authentication server. Please ensure Python server is running.";
                return false; // BLOCK login - Bluetooth is mandatory
            }

            string command = "bluetooth_scan " + targetMac;
            client.sendMessage(command);

            string response = client.recieveMessage();
            client.closeConnection();

            if (response == null || response == "")
            {
                status = "No response from authentication server. Please try again.";
                return false; // BLOCK login - Bluetooth is mandatory
            }

            if (response.StartsWith("FOUND:"))
            {
                string deviceName;
                string mac;
                string[] parts = response.Split(':');
                if (parts.Length >= 8)
                {
                    mac = string.Join(":", parts, parts.Length - 6, 6);
                    deviceName = string.Join(":", parts, 1, parts.Length - 7);
                }
                else if (parts.Length >= 3)
                {
                    deviceName = parts[1];
                    mac = parts[2];
                }
                else
                {
                    deviceName = "Unknown";
                    mac = targetMac;
                }
                status = "✓ Bluetooth verified - " + deviceName + " detected";
                return true; // ALLOW login - Bluetooth verified
            }

            if (response.StartsWith("NOT_FOUND"))
            {
                status = "✗ Bluetooth verification failed - your device was not found. Please ensure Bluetooth is enabled and your device is nearby.";
                return false; // BLOCK login - Bluetooth is mandatory
            }

            if (response.StartsWith("ERROR:"))
            {
                string errorMsg = response.Substring(6);
                status = "✗ Bluetooth error: " + FriendlyBluetoothError(errorMsg);
                return false; // BLOCK login - Bluetooth is mandatory
            }

            status = "✗ Unknown Bluetooth response: " + FriendlyBluetoothError("Unknown response: " + response);
            return false; // BLOCK login - Bluetooth is mandatory
        }
        catch (Exception ex)
        {
            status = "✗ Bluetooth verification error: " + FriendlyBluetoothError(ex.Message);
            return false; // BLOCK login - Bluetooth is mandatory
        }
    }

    /// <summary>Registration: discover a nearby BLE device (Python picks best named candidate).</summary>
    /// <remarks>
    /// This is OPTIONAL during registration. Users can choose to register without Bluetooth.
    /// If they skip Bluetooth, they will not need Bluetooth verification during login.
    /// If they register with Bluetooth, it becomes MANDATORY for all future logins.
    /// </remarks>
    public bool TryPickRegistrationDevice(out string deviceName, out string mac, out string status)
    {
        deviceName = string.Empty;
        mac = string.Empty;
        status = "Scanning for Bluetooth devices...";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("127.0.0.1", 5000))
            {
                status = "Cannot connect to the Python server (port 5000). Start python_server.py and try again.";
                return false;
            }

            string response = client.sendCommandAndWait("bluetooth_register_pick");
            client.closeConnection();

            if (string.IsNullOrEmpty(response))
            {
                status = "No response from server.";
                return false;
            }

            if (response.StartsWith("FOUND\t", StringComparison.Ordinal))
            {
                string payload = response.Length > 6 ? response.Substring(6) : "";
                string[] parts = payload.Split(new[] { '\t' }, StringSplitOptions.None);
                if (parts.Length >= 2)
                {
                    deviceName = parts[0].Trim();
                    mac = parts[1].Trim().ToUpperInvariant().Replace("-", ":");
                    status = "Selected device: " + deviceName + " (" + mac + ")";
                    return true;
                }
            }

            if (string.Equals(response, "NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                status = "No Bluetooth devices found. Enable Bluetooth and try again.";
                return false;
            }

            if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                status = FriendlyBluetoothError(response.Substring(6));
                return false;
            }

            status = FriendlyBluetoothError("Unknown response: " + response);
            return false;
        }
        catch (Exception ex)
        {
            status = FriendlyBluetoothError(ex.Message);
            return false;
        }
    }
}

public enum FaceRegisterScanResult
{
    Error,
    NoFace,
    MatchedExisting,
    NewUserCreated
}

public class FaceRecognitionService
{
    /// <summary>Registration: capture face; FOUND existing user or NEW id with image saved by Python.</summary>
    public bool RegisterFaceScan(out FaceRegisterScanResult result, out string userId, out string status)
    {
        result = FaceRegisterScanResult.Error;
        userId = string.Empty;
        status = "Starting face capture for registration...";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("127.0.0.1", 5000))
            {
                status = "Cannot connect to Face ID server.";
                return false;
            }

            string response = client.sendCommandAndWait("face_register_scan");
            client.closeConnection();

            if (string.IsNullOrEmpty(response))
            {
                status = "No response from Face ID server.";
                return false;
            }

            if (response.StartsWith("FOUND:", StringComparison.OrdinalIgnoreCase))
            {
                userId = response.Substring(6).Trim();
                result = FaceRegisterScanResult.MatchedExisting;
                status = "This face is already registered as " + userId + ".";
                return true;
            }

            if (response.StartsWith("NEW:", StringComparison.OrdinalIgnoreCase))
            {
                userId = response.Substring(4).Trim();
                result = FaceRegisterScanResult.NewUserCreated;
                status = "New face saved as " + userId + ". Continue with Bluetooth.";
                return true;
            }

            if (response.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                result = FaceRegisterScanResult.NoFace;
                status = "No face detected. Try again with better lighting.";
                return true;
            }

            if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                status = response.Substring(6);
                return false;
            }

            status = "Unexpected response: " + response;
            return false;
        }
        catch (Exception ex)
        {
            status = "Register face exception: " + ex.Message;
            return false;
        }
    }

    /// <summary>
    /// OpenCV lobby on Python (oval guide, centering, hold-still, countdown for new users).
    /// Same wire protocol as RegisterFaceScan: FOUND:userId, NEW:userId, NOT_FOUND, ERROR:, plus CANCELLED.
    /// </summary>
    public bool AuthLobbyScan(out FaceRegisterScanResult result, out string userId, out string status)
    {
        result = FaceRegisterScanResult.Error;
        userId = string.Empty;
        status = "Opening your webcam — look for the “Face sign-in” window on this PC.";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("127.0.0.1", 5000))
            {
                status = "Cannot connect to Face ID server.";
                return false;
            }

            string response = client.sendCommandAndWait("face_auth_lobby");
            client.closeConnection();

            if (string.IsNullOrEmpty(response))
            {
                status = "No response from Face ID server.";
                return false;
            }

            if (response.StartsWith("FOUND:", StringComparison.OrdinalIgnoreCase))
            {
                userId = response.Substring(6).Trim();
                result = FaceRegisterScanResult.MatchedExisting;
                status = "Welcome back — we matched your face (" + userId + ").";
                return true;
            }

            if (response.StartsWith("NEW:", StringComparison.OrdinalIgnoreCase))
            {
                userId = response.Substring(4).Trim();
                result = FaceRegisterScanResult.NewUserCreated;
                status = "Your photo was saved. Next we will pair Bluetooth for your account.";
                return true;
            }

            if (string.Equals(response, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                result = FaceRegisterScanResult.NoFace;
                status = "Sign-in was cancelled from the webcam window.";
                return true;
            }

            if (response.StartsWith("NOT_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                result = FaceRegisterScanResult.NoFace;
                status = "We could not finish in time. Try again with your face clearly in the oval.";
                return true;
            }

            if (response.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
            {
                status = response.Substring(6);
                return false;
            }

            status = "Unexpected response: " + response;
            return false;
        }
        catch (Exception ex)
        {
            status = "Auth lobby exception: " + ex.Message;
            return false;
        }
    }

    public bool Scan(out string userId, out string status)
    {
        userId = string.Empty;
        status = "Running Face ID scan...";

        try
        {
            SocketClient client = new SocketClient();
            if (!client.connectToSocket("127.0.0.1", 5000))
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

/// <summary>Append a new visitor row to users.csv (Face ID server must already have saved the face image).</summary>
public static class AuthCsvStore
{
    public static string SanitizeField(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace(",", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    }

    public static bool AppendUser(string csvPath, VisitorProfile profile)
    {
        try
        {
            string line = string.Join(",",
                SanitizeField(profile.FaceUserId),
                SanitizeField(profile.FirstName),
                SanitizeField(profile.LastName),
                profile.Age.ToString(CultureInfo.InvariantCulture),
                SanitizeField(profile.Gender),
                SanitizeField(profile.Race),
                SanitizeField(profile.BluetoothMacAddress),
                SanitizeField(profile.FaceImagePath),
                SanitizeField(string.IsNullOrEmpty(profile.Role) ? "visitor" : profile.Role));

            File.AppendAllText(csvPath, Environment.NewLine + line, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
