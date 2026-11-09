# MUTASSIM Phone Service

A commercial Windows phone-servicing platform, in two parts:

- **`server/` — MUTASSIM GSM CLOUD** · ASP.NET Core server (device command queue, Smart Assist solution engine, and a credits/billing system: admin → distributors → technicians) plus a device agent.
- **`tool/` — MUTASSIM TOOL PRO** · WPF desktop tool (.NET 10). Device detection (Samsung / Qualcomm / MediaTek / Apple), official Samsung firmware flashing, a read-only Samsung **AT / serial diagnostic engine**, Smart Assist, and the account/credit UI.

## Scope & principles

This project implements **official, legitimate servicing only**:

- **Read/diagnostic vs state-change are separated.** The AT engine executes read-only commands; state-changing, engineering, activation and protection commands are modelled and logged but **not executed** by the engine.
- **No FRP / anti-theft bypass.** There is deliberately no "remove Google account / FRP bypass" operation anywhere, and none in the pricing table. Operations that genuinely need authorization (e.g. official IMEI network unlock) are marked `RequiresExternalAuthorization` and go through an authorized provider the operator signs up with.
- **Firmware flashing uses the user's own official ROM files** (e.g. from samfw.com) via Heimdall; protected partitions (EFS/IMEI/FRP/PERSISTENT) are refused and no repartition is performed.

## Build

Both parts target **.NET 10**.

```powershell
# Server
dotnet build server/MUTASSIM_GSM_CLOUD.slnx -c Release
dotnet test  server/MUTASSIM_SERVER.Tests

# Tool
dotnet build tool/MUTASSIM_TOOL_PRO.sln -c Release
dotnet test  tool/tests/MUTASSIM.ToolPro.Tests.Unit
```

> The WPF app (`tool/src/MUTASSIM.ToolPro.App`) builds and runs on **Windows** only. The engine libraries and their unit tests are cross-platform.

## Running the server

```powershell
# First run only — create the admin:
.\server\tools\start-server.ps1 -AdminUser admin -AdminPass "<strong-pass>" -ApiKey "<device-key>"
# Later runs:
.\server\tools\start-server.ps1 -ApiKey "<device-key>"
```

The credits database (`mutassim.db`) is **never** committed — see `.gitignore`. Back it up like any financial data.

## Documentation

- `server/BILLING.md` — the credits/billing system
- `tool/docs/` — Smart Assist, Samsung flash, and AT engine notes
- `CONTRIBUTING.md` — conventions every contributor (human or AI) follows
