# MUTASSIM GSM CLOUD - corrected source package

## What was corrected
- `MUTASSIM_SERVER/Program.cs` was incorrectly running the old Device Agent V1.0 console program. It is now the ASP.NET Core API server.
- Added API routes:
  - `GET /api/health`
  - `POST /api/commands`
  - `GET /api/commands/next/{deviceId}`
  - `POST /api/commands/result`
  - `POST /api/device/report`
- Updated `MUTASSIM_DEVICE_AGENT/Program.cs` to V1.3.
- The agent now locates ADB using `MUTASSIM_ADB_PATH`, the Desktop `platform-tools` folder, or PATH.
- Added a `PING` command so server/agent command flow can be tested without a phone.
- Removed generated `bin` and `obj` folders from this source package.

## Requirements
- .NET SDK 10 (the projects target `net10.0`)
- Windows PowerShell / VS Code
- Android platform-tools only when testing ADB against a real Android device

## Run the server
Open PowerShell in the folder containing `MUTASSIM_GSM_CLOUD.slnx`:

```powershell
dotnet restore .\MUTASSIM_GSM_CLOUD.slnx
dotnet build .\MUTASSIM_GSM_CLOUD.slnx
dotnet run --project .\MUTASSIM_SERVER\MUTASSIM_SERVER.csproj --launch-profile http
```

Keep this terminal open. The server is configured for `http://localhost:5179`.
Swagger should be available at `http://localhost:5179/swagger` when using the `http` launch profile (Development).

## Run the agent
Open a second PowerShell in the same solution folder:

```powershell
dotnet run --project .\MUTASSIM_DEVICE_AGENT\MUTASSIM_DEVICE_AGENT.csproj
```

The agent should show `[CONNECTED] MUTASSIM SERVER ONLINE`.

## Test without a phone
With both server and agent running, open Swagger and execute:

`POST /api/commands`

Request body:

```json
{
  "deviceId": "TEST-DEVICE-001",
  "action": "PING"
}
```

The agent should receive `PING` and send a successful result back to the server. `ADB_INFO` and `DEVICE_SCAN` require ADB; a real connected and authorized phone is required to return phone details.

## Optional ADB path
If ADB is not in `C:\Users\<your-user>\Desktop\platform-tools\adb.exe` or PATH, set it before running the agent:

```powershell
$env:MUTASSIM_ADB_PATH = "C:\platform-tools\adb.exe"
```

## Important
This package was inspected and source-corrected. The execution environment used for this repair did not have the .NET 10 SDK installed, so a local `dotnet build` could not be run here. Please run the build commands above on your Windows machine and share any compiler output if an error appears.
