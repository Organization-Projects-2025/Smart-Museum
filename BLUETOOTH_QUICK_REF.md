# Bluetooth Authentication - Quick Reference

## 🚀 Quick Start

### **Check if User Requires Bluetooth**

```csharp
// Using VisitorProfile
bool requiresBt = BluetoothService.RequiresBluetoothVerification(userProfile);

// Using MAC address directly
bool requiresBt = BluetoothService.RequiresBluetoothVerification(macAddress);
```

### **Verify Bluetooth During Login**

```csharp
var btService = new BluetoothService();
string status;
bool verified = btService.Verify(user.BluetoothMacAddress, out status);

if (verified)
{
    // Login successful
    Console.WriteLine($"✓ {status}");
}
else
{
    // Login failed - Bluetooth is mandatory
    Console.WriteLine($"✗ {status}");
}
```

### **Register New User (With Bluetooth)**

```csharp
// Step 1: Pick Bluetooth device
var btService = new BluetoothService();
string deviceName, mac, status;
bool deviceFound = btService.TryPickRegistrationDevice(out deviceName, out mac, out status);

if (deviceFound)
{
    // Step 2: Create user profile with MAC
    var newUser = new VisitorProfile
    {
        FaceUserId = "user123",
        FirstName = "John",
        LastName = "Doe",
        BluetoothMacAddress = mac, // e.g., "4C:7C:D9:B2:4A:55"
        // ... other fields
    };

    // Step 3: Save to database
    VisitorProfile.AppendUser("content/auth/users.csv", newUser);
}
```

### **Register New User (Without Bluetooth)**

```csharp
// Create user profile WITHOUT Bluetooth
var newUser = new VisitorProfile
{
    FaceUserId = "user456",
    FirstName = "Jane",
    LastName = "Smith",
    BluetoothMacAddress = "0", // No Bluetooth device
    // ... other fields
};

// Save to database
VisitorProfile.AppendUser("content/auth/users.csv", newUser);
```

## 📋 Decision Tree

```
User Login Attempt
    │
    ├─ Face Recognition
    │   │
    │   └─ User Identified?
    │       │
    │       ├─ YES → Check Bluetooth MAC
    │       │   │
    │       │   ├─ MAC == "0" or empty?
    │       │   │   │
    │       │   │   ├─ YES → Skip Bluetooth ✓
    │       │   │   │        └─ Login Successful
    │       │   │   │
    │       │   │   └─ NO → Bluetooth Required
    │       │   │       │
    │       │   │       ├─ Device Found? → ✓ Login Successful
    │       │   │       └─ Device NOT Found? → ✗ Login Failed
    │       │   │
    │       └─ NO → Show registration
    │
    └─ NO → Show registration
```

## 🔧 Common Code Patterns

### **Pattern 1: Login Flow with Bluetooth**

```csharp
public bool AttemptLogin(VisitorProfile user, out string message)
{
    // Step 1: Verify Bluetooth if required
    if (BluetoothService.RequiresBluetoothVerification(user))
    {
        var btService = new BluetoothService();
        bool btVerified = btService.Verify(user.BluetoothMacAddress, out string btStatus);

        if (!btVerified)
        {
            message = $"Bluetooth verification failed: {btStatus}";
            return false; // BLOCK login
        }

        message = $"✓ Bluetooth verified: {btStatus}";
    }
    else
    {
        message = "✓ Bluetooth verification skipped (no device registered)";
    }

    // Step 2: Continue with login logic
    return true; // ALLOW login
}
```

### **Pattern 2: Registration Flow**

```csharp
public void RegisterUser(VisitorProfile user, bool registerBluetooth)
{
    if (registerBluetooth)
    {
        // Try to find Bluetooth device
        var btService = new BluetoothService();
        string deviceName, mac, status;

        if (btService.TryPickRegistrationDevice(out deviceName, out mac, out status))
        {
            user.BluetoothMacAddress = mac;
            Console.WriteLine($"✓ Registered device: {deviceName}");
        }
        else
        {
            // Failed to find device - ask user to retry or skip
            user.BluetoothMacAddress = "0";
            Console.WriteLine($"⚠ No device found: {status}");
        }
    }
    else
    {
        // User chose not to register Bluetooth
        user.BluetoothMacAddress = "0";
        Console.WriteLine("ℹ Bluetooth registration skipped");
    }

    // Save user to database
    VisitorProfile.AppendUser("content/auth/users.csv", user);
}
```

### **Pattern 3: Admin User Management**

```csharp
public void UpdateUserBluetooth(string userId, string newMac)
{
    // Load all users
    var users = VisitorProfile.LoadFromCsv("content/auth/users.csv");

    // Find and update user
    var user = users.FirstOrDefault(u => u.FaceUserId == userId);
    if (user != null)
    {
        user.BluetoothMacAddress = newMac;

        // Save updated list
        // (Implementation depends on your CSV handling)
        Console.WriteLine($"✓ Updated {userId} Bluetooth to {newMac}");
    }
}

public void DisableUserBluetooth(string userId)
{
    UpdateUserBluetooth(userId, "0");
}
```

## 🎯 Status Messages Reference

### **Success Messages**

| Message | Meaning | Action |
|---------|---------|--------|
| `"✓ Bluetooth verified - iPhone detected"` | Device found | Allow login |
| `"Bluetooth verification skipped (no device registered)"` | No device required | Allow login |

### **Failure Messages**

| Message | Meaning | Action |
|---------|---------|--------|
| `"✗ Bluetooth verification failed - your device was not found"` | Device not detected | Block login |
| `"✗ Bluetooth error: Bluetooth is off on this computer"` | System Bluetooth disabled | Block login |
| `"✗ Bluetooth verification error: Cannot reach authentication server"` | Server unavailable | Block login |

## 🧪 Testing Checklist

### **Unit Tests**

```csharp
// Test 1: User with Bluetooth requires verification
var userWithBt = new VisitorProfile { BluetoothMacAddress = "4C:7C:D9:B2:4A:55" };
Assert.IsTrue(BluetoothService.RequiresBluetoothVerification(userWithBt));

// Test 2: User without Bluetooth does not require verification
var userWithoutBt = new VisitorProfile { BluetoothMacAddress = "0" };
Assert.IsFalse(BluetoothService.RequiresBluetoothVerification(userWithoutBt));

// Test 3: Empty MAC does not require verification
Assert.IsFalse(BluetoothService.RequiresBluetoothVerification(""));

// Test 4: "0" MAC does not require verification
Assert.IsFalse(BluetoothService.RequiresBluetoothVerification("0"));

// Test 5: Valid MAC requires verification
Assert.IsTrue(BluetoothService.RequiresBluetoothVerification("AA:BB:CC:DD:EE:FF"));
```

### **Integration Tests**

```csharp
// Test 1: Login with Bluetooth (device present)
var btService = new BluetoothService();
bool result = btService.Verify("4C:7C:D9:B2:4A:55", out string status);
// Expected: result == true (if device nearby)

// Test 2: Login with Bluetooth (device absent)
bool result = btService.Verify("AA:BB:CC:DD:EE:FF", out string status);
// Expected: result == false

// Test 3: Login without Bluetooth
bool result = btService.Verify("0", out string status);
// Expected: result == true, status contains "skipped"
```

## 📊 Database Operations

### **Read Users**

```csharp
var users = VisitorProfile.LoadFromCsv("content/auth/users.csv");
foreach (var user in users)
{
    bool requiresBt = BluetoothService.RequiresBluetoothVerification(user);
    Console.WriteLine($"{user.FullName}: Bluetooth {(requiresBt ? "REQUIRED" : "OPTIONAL")}");
}
```

### **Filter Users by Bluetooth Status**

```csharp
var users = VisitorProfile.LoadFromCsv("content/auth/users.csv");

// Users WITH Bluetooth
var usersWithBt = users.Where(u => BluetoothService.RequiresBluetoothVerification(u)).ToList();

// Users WITHOUT Bluetooth
var usersWithoutBt = users.Where(u => !BluetoothService.RequiresBluetoothVerification(u)).ToList();
```

## 🚨 Error Handling

### **Handle Bluetooth Service Errors**

```csharp
try
{
    var btService = new BluetoothService();
    bool verified = btService.Verify(macAddress, out string status);

    if (!verified)
    {
        // Bluetooth verification failed
        ShowError($"Bluetooth required: {status}");
        return false;
    }

    // Success
    return true;
}
catch (Exception ex)
{
    // Unexpected error
    ShowError($"Bluetooth error: {ex.Message}");
    return false;
}
```

### **Handle Missing Python Server**

```csharp
var btService = new BluetoothService();
bool verified = btService.Verify(macAddress, out string status);

if (!verified && status.Contains("Cannot reach"))
{
    // Python server not running
    ShowError("Authentication server unavailable. Please contact staff.");
    // Consider allowing login without Bluetooth in emergency
}
```

## 🎨 UI Integration

### **Show Bluetooth Status in UI**

```csharp
public void UpdateBluetoothStatus(VisitorProfile user)
{
    if (BluetoothService.RequiresBluetoothVerification(user))
    {
        lblBluetoothStatus.Text = "📱 Bluetooth Required";
        lblBluetoothStatus.ForeColor = Color.Orange;
    }
    else
    {
        lblBluetoothStatus.Text = "📱 Bluetooth Optional";
        lblBluetoothStatus.ForeColor = Color.Gray;
    }
}
```

### **Bluetooth Verification Progress**

```csharp
public async Task<bool> VerifyBluetoothAsync(string macAddress)
{
    lblStatus.Text = "🔄 Verifying Bluetooth...";
    lblStatus.ForeColor = Color.Blue;

    var btService = new BluetoothService();
    bool verified = btService.Verify(macAddress, out string status);

    if (verified)
    {
        lblStatus.Text = $"✓ {status}";
        lblStatus.ForeColor = Color.Green;
    }
    else
    {
        lblStatus.Text = $"✗ {status}";
        lblStatus.ForeColor = Color.Red;
    }

    return verified;
}
```

## 📝 Configuration

### **Environment Variables**

```bash
# Python server port (default: 5000)
export PYTHON_SERVER_PORT=5000

# Bluetooth scan timeout (default: 8 seconds)
export BLUETOOTH_SCAN_TIMEOUT=8
```

### **App Settings**

```csharp
// In your app configuration
public class AppConfig
{
    public const string PythonServerHost = "127.0.0.1";
    public const int PythonServerPort = 5000;
    public const int BluetoothScanTimeout = 8; // seconds
}
```

## 🔍 Debugging

### **Enable Bluetooth Logging**

```csharp
// In BluetoothService.Verify()
public bool Verify(string targetMac, out string status)
{
    // Debug logging
    Console.WriteLine($"[BT] Verifying MAC: {targetMac}");

    // ... verification logic ...

    Console.WriteLine($"[BT] Result: {verified}, Status: {status}");
    return verified;
}
```

### **Test Bluetooth Service Connection**

```csharp
public bool TestBluetoothService()
{
    try
    {
        var client = new SocketClient();
        bool connected = client.connectToSocket("127.0.0.1", 5000);
        client.closeConnection();

        Console.WriteLine($"[BT] Service connection: {(connected ? "✓" : "✗")}");
        return connected;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BT] Service connection error: {ex.Message}");
        return false;
    }
}
```

---

**Quick Reference Summary:**
- ✅ MAC != "0" → Bluetooth REQUIRED
- ✅ MAC == "0" → Bluetooth SKIPPED
- ✅ Use `BluetoothService.RequiresBluetoothVerification()` to check
- ✅ Use `BluetoothService.Verify()` for authentication
- ✅ Handle both success and failure cases properly