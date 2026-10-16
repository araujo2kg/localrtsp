# localrtsp

A Windows desktop app that provides a local RTSP stream for development and testing. Built with C#, .NET 10 LTS, and Avalonia.

## Run

Requires Windows x64 and the .NET SDK specified in [global.json](global.json). Run from the repository root:

```powershell
# Once: download and verify FFmpeg and MediaMTX
./scripts/setup-tools.ps1

dotnet run --project src/localrtsp/localrtsp.csproj
```

You can also select existing FFmpeg and MediaMTX executables under **Dependencies**. Setup does not change PATH or install anything globally.

The app starts with black frames. Choose a source and click **Apply & Restart**, then copy the displayed URL into your client, using RTSP over TCP.

- Sources: solid color, moving test pattern, PNG/JPEG image, video file, or local webcam.
- **Unresponsive connection** simulates a connection that never returns RTSP replies.
- Example URL: `rtsp://127.0.0.1:8554/camera`. Connections are limited to this machine.
- **Stop** or closing the app releases the stream and camera. There is no background service.

## Development

```powershell
dotnet restore localrtsp.sln
dotnet build localrtsp.sln
dotnet test tests/localrtsp.Tests/localrtsp.Tests.csproj --no-build
```

Unit tests do not require a webcam or streaming tools. To include real streaming integration tests, run setup, build the solution, and set `$env:LOCALRTSP_INTEGRATION = '1'` before running tests.

## Publish

```powershell
dotnet publish src/localrtsp/localrtsp.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

Run `artifacts/win-x64/localrtsp.exe`. Streaming tools are separate: select their paths under **Dependencies**, or copy the `tools/ffmpeg` and `tools/mediamtx` folders into a `tools` folder beside the executable. Keep their upstream licenses with the tools.

For connection or source errors, check **Diagnostic logs**. For webcam errors, check Windows camera permissions and close other apps using the device.
