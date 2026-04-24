# Build Instructions for TuioDemo with Gesture Recognition

## Prerequisites

1. Visual Studio 2019 or later (or MSBuild)
2. .NET Framework 4.8
3. NuGet Package Manager

## Building the Project

### Option 1: Using Visual Studio

1. Open `TUIO_DEMO.csproj` in Visual Studio
2. Right-click on the solution → "Restore NuGet Packages"
3. Build → Build Solution (or press F6)
4. Run the application (F5)

### Option 2: Using MSBuild (Command Line)

```bash
# Navigate to C# directory
cd Smart-Museum/C#

# Restore NuGet packages
nuget restore TUIO_DEMO.csproj

# Build the project
msbuild TUIO_DEMO.csproj /p:Configuration=Release

# Run the application
bin\Release\TuioDemo.exe
```

### Option 3: Using dotnet CLI (if available)

```bash
cd Smart-Museum/C#
dotnet restore TUIO_DEMO.csproj
dotnet build TUIO_DEMO.csproj
```

## Required NuGet Packages

The project requires:
- **Newtonsoft.Json** (v13.0.3) - For JSON serialization in gesture communication

This is automatically restored when you build the project.

## New Files Added

- `GestureClient.cs` - Client for communicating with Python gesture service
- `packages.config` - NuGet package configuration

## Project Structure

```
C#/
├── TUIO_DEMO.csproj          # Main project file (updated)
├── packages.config            # NuGet packages (new)
├── TuioDemo.cs               # Main application (updated with gesture support)
├── GestureClient.cs          # Gesture client (new)
├── AuthIntegration.cs        # Authentication services
├── CircularMenuController.cs # Menu controller
├── FigureData.cs            # Figure definitions
├── SlideShowManager.cs      # Slideshow logic
└── ...
```

## Troubleshooting

### Error: "Newtonsoft.Json not found"

**Solution:**
```bash
# Install NuGet CLI if not available
# Download from: https://www.nuget.org/downloads

# Restore packages
nuget restore TUIO_DEMO.csproj
```

### Error: "GestureClient could not be found"

**Solution:**
- Ensure `GestureClient.cs` is in the C# folder
- Check that `TUIO_DEMO.csproj` includes the line:
  ```xml
  <Compile Include="GestureClient.cs" />
  ```
- Rebuild the project

### Error: "The type or namespace name 'Task' could not be found"

**Solution:**
Add to the top of TuioDemo.cs:
```csharp
using System.Threading.Tasks;
```

## Running with Gesture Recognition

1. **Start Python Gesture Service:**
   ```bash
   cd Smart-Museum/dollarpy-service
   ..\.venv\Scripts\python.exe gesture_service.py
   ```

2. **Start Python Auth Server:**
   ```bash
   cd Smart-Museum/python/server
   ..\..\..venv\Scripts\python.exe python_server.py
   ```

3. **Run TuioDemo:**
   ```bash
   cd Smart-Museum/C#
   bin\Release\TuioDemo.exe
   ```

## Verification

After building, you should see:
- `bin\Debug\TuioDemo.exe` (Debug build)
- `bin\Release\TuioDemo.exe` (Release build)
- `bin\Debug\Newtonsoft.Json.dll` (NuGet package)

## Next Steps

Once built successfully:
1. Application connects to gesture service on startup
2. Perform face ID authentication
3. Open circular menu
4. Use hand gestures to control the menu

---

**Last Updated:** March 23, 2026
