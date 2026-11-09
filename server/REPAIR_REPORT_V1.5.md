# MUTASSIM GSM CLOUD — V1.5 Repair Report

Date: 2026-11-08 · Owner: MUTASSIM

Originals of every modified file are kept in `_backup_before_v1.5\` (same folder layout).
Nothing was deleted.

---

## 1. Project structure (audit)

```
MUTASSIM_GSM_CLOUD.slnx
├─ MUTASSIM_SERVER.Core      (class library, net10.0) — shared models + CommandQueue + DeviceRegistry
├─ MUTASSIM_SERVER           (ASP.NET Core minimal API, net10.0) → references Core
│    packages: Microsoft.AspNetCore.OpenApi 10.0.14, Swashbuckle.AspNetCore 10.3.1
└─ MUTASSIM_DEVICE_AGENT     (console, net10.0) → now ALSO references Core (was duplicating models)
```

Agent ⇄ Server contract (HTTP, JSON, header `X-MUTASSIM-KEY` when auth is on):

| Route | Use |
|---|---|
| `GET  /api/health` | agent connection check (no auth) |
| `POST /api/commands` | queue `PING` / `DEVICE_SCAN` / `ADB_INFO` for an agent |
| `GET  /api/commands/next/{deviceId}` | agent poll → `200` + command, or `204` when nothing to do |
| `POST /api/commands/result` | agent returns the result |
| `POST /api/device/report` | agent sends one Android device's info |
| `GET  /api/commands/{id}` · `GET /api/commands?deviceId=` | **new** – read command status + result |
| `GET  /api/devices` · `GET /api/agents` | **new** – latest device reports / agents last seen |

Existing route names and JSON field names were **not** changed. New fields are optional
(`CommandResult.deviceId`, `DeviceReport.agentId`), so older agents keep working.

## 2. Problems found → fixed

| # | Problem (verified by running the original code) | Fix |
|---|---|---|
| 1 | Server queued **any** text as a command (e.g. `rm -rf` was accepted) | `AllowedCommands` allow-list in Core; server returns 400 for unknown actions, agent also refuses them |
| 2 | Empty/invalid JSON body → full **stack trace** returned to the client | JSON error middleware: clean 400/500 responses, details only in the server log |
| 3 | `CommandQueue.GetNext` not atomic → two polls could take the **same** command | Whole lifecycle under one lock; Pending→Running→Completed/Failed enforced |
| 4 | `Complete` accepted results for any state, could overwrite; **results were discarded** | Results stored; duplicate result → 409; result from wrong agent → 400; not-dispatched → 409 |
| 5 | Agent **exited forever** if the server was down at startup | Reconnect loop with back-off (2s→30s), quiet logging (no error spam) |
| 6 | Server restart during polling → repeated errors | Agent detects disconnect, returns to reconnect state, resends undelivered results |
| 7 | POLL ERROR "input does not contain any JSON tokens" | Root cause: parsing an empty body. Agent now checks the **HTTP status first** (204 = no command), then Content-Type, then JSON; 401/5xx handled separately |
| 8 | No authentication | Optional API key (`MUTASSIM_API_KEY`), constant-time compare; **required** outside Development (server refuses to start without it) |
| 9 | Device reports were **never sent** by the agent | `ADB_INFO` sends one report per phone; server stores them (`GET /api/devices`) |
| 10 | ADB: single fixed lookup, no timeout, unauthorized phones not handled | `Adb.cs`: MUTASSIM_ADB_PATH (file **or** folder) → Desktop\platform-tools → OneDrive\Desktop → ANDROID_SDK_ROOT/ANDROID_HOME → %LOCALAPPDATA%\Android\Sdk → PATH; clear error listing every searched path; 30s timeout; parses `device` / `unauthorized` / `offline` / `no permissions` with hints; one `getprop` call per phone |
| 11 | Device ID hard-coded | `MUTASSIM_DEVICE_ID` (default stays `TEST-DEVICE-001`), validated |
| 12 | Agent duplicated server models (`Status` as `int`) | Agent uses Core models; enum accepted as number or string |
| 13 | MSB3027/MSB3021 file locks | Caused by a running Server/Agent exe during build. `tools\stop-mutassim.ps1` stops **only** this solution's processes |
| 14 | `MUTASSIM_SERVER.http` still pointed to template `/weatherforecast` | Replaced with real requests |

## 3. Files

**Modified:** `MUTASSIM_SERVER.Core/Commands/CommandModels.cs`, `MUTASSIM_SERVER.Core/Commands/CommandQueue.cs`,
`MUTASSIM_SERVER/Program.cs`, `MUTASSIM_SERVER/MUTASSIM_SERVER.http`,
`MUTASSIM_DEVICE_AGENT/Program.cs`, `MUTASSIM_DEVICE_AGENT/MUTASSIM_DEVICE_AGENT.csproj`

**Added:** `MUTASSIM_SERVER.Core/Commands/AllowedCommands.cs`, `MUTASSIM_SERVER.Core/Devices/DeviceModels.cs`,
`MUTASSIM_DEVICE_AGENT/Adb.cs`, `tools/stop-mutassim.ps1`, `tools/start-server.ps1`, `tools/start-agent.ps1`, this report.

**Inspected, unchanged:** `.slnx`, Server/Core `.csproj`, `appsettings*.json`, `launchSettings.json`, `Class1.cs`, `README_REPAIR.md`.

## 4. Test results

Where they ran: .NET SDK 10.0.114 on Linux, using the **same NuGet packages copied from this PC's cache**.
Then re-verified on this Windows PC with a real phone (see below).

| Test | Result |
|---|---|
| 1 Build whole solution | ✅ 0 errors, 0 warnings |
| 2 Start server | ✅ |
| 3 `GET /api/health` | ✅ 200 |
| 4 Start agent | ✅ |
| 5 Agent connects | ✅ `[CONNECTED]` |
| 6 PING | ✅ result stored on server |
| 7 DEVICE_SCAN | ✅ *simulated adb*: 1 authorized + 1 unauthorized phone, hint shown |
| 8 ADB_INFO | ✅ *simulated adb*: Brand/Manufacturer/Model/Android/SDK/Build |
| 9 Device report | ✅ both reports visible in `GET /api/devices` |
| 10 Results reach server | ✅ every command has its result in `GET /api/commands/{id}` |
| 11 No devices connected | ✅ "No Android devices connected." (success, no error) |
| 12 Server killed 12s, restarted | ✅ agent showed OFFLINE, reconnected alone, next PING worked |
| 13 No duplicate execution | ✅ 80 parallel polls on 30 commands → 30 unique; 2 agents same ID → 20 commands executed exactly 20 times; second result → 409 |
| 14 Clean rebuild after stopping test processes | ✅ |
| Extra: auth | ✅ no key → 401; wrong key → agent shows `[AUTH ERROR]` |
| Extra: bad input | ✅ unknown command, empty body, bad DeviceId, empty result → clean 400 JSON |
| Extra: ADB missing | ✅ clear message listing searched paths, sent as a failed result |
| Extra: agent started before server | ✅ waits, then connects |
| Idle polling (≈20s with no commands) | ✅ no POLL/JSON errors |

### Confirmed on Windows (this PC, 2026-11-08)
Build OK (0 errors / 0 warnings) · server + agent connected · PING ✅ · ADB found automatically in Desktop\platform-tools ✅ ·
ADB_INFO with no phone ✅ · **ADB_INFO with a real phone ✅** — TECNO CM7, Android 16, SDK 36, build CM7-16.3.0.155SP08, report stored in `GET /api/devices`.

**Still untested on real hardware:** the *unauthorized* prompt flow and several phones connected at once (both passed with simulated adb).

## 5. How to run (PowerShell, in the solution folder)

```powershell
.\tools\stop-mutassim.ps1                 # only if a previous run is still open
dotnet build .\MUTASSIM_GSM_CLOUD.slnx

# Terminal 1 – server
.\tools\start-server.ps1                  # dev, no key   (or:  -ApiKey "choose-a-long-secret")

# Terminal 2 – agent
.\tools\start-agent.ps1                   # add the same -ApiKey if you set one
```

If PowerShell blocks the scripts: `powershell -ExecutionPolicy Bypass -File .\tools\start-server.ps1`
(this only affects that one command; no system setting is changed).

Send a command: Swagger `http://localhost:5179/swagger` → `POST /api/commands`
`{ "deviceId": "TEST-DEVICE-001", "action": "PING" }` → see result with `GET /api/commands/{id}`.

Agent settings: `MUTASSIM_SERVER_URL`, `MUTASSIM_DEVICE_ID`, `MUTASSIM_API_KEY`, `MUTASSIM_ADB_PATH`.

## 6. Remaining items / next steps

1. Test a phone that has not yet accepted USB debugging (unauthorized), and two phones at once.
3. Storage is in-memory: restarting the server clears commands/reports (agent handles this safely). Add a database (e.g. SQLite) before production.
4. Production: run with `ASPNETCORE_ENVIRONMENT=Production`, a strong `MUTASSIM_API_KEY`, and an HTTPS certificate. Do not open firewall ports / expose to the internet until then.
5. One shared API key for all agents; per-agent keys would be the next security step.

---

# V1.6 — Smart Assist for MUTASSIM TOOL PRO (2026-11-08)

New: `/api/assist/problems`, `/api/assist/actions`, `POST /api/assist/diagnose`, `POST /api/assist/feedback`,
`/api/assist/sessions[/{id}]` (same API-key protection as the rest of `/api`).
MUTASSIM TOOL PRO V1.5 sends the problem + phone state; the server's `SolutionEngine` answers with steps chosen
from a fixed list of 20 tool actions (no raw commands, no bypass/IMEI/flash/erase/unlock); the tool runs them and
reports each result back.

Files: `MUTASSIM_SERVER.Core/Assist/{AssistModels,AssistActions,SolutionEngine,AssistSessionStore}.cs`,
`MUTASSIM_SERVER/Program.cs` (assist endpoints, version 1.6), new `MUTASSIM_SERVER.Tests` project (added to the .slnx).
Previous `Program.cs` and `.slnx` are in `_backup_before_v1.6\`.

Tests: `dotnet test .\MUTASSIM_SERVER.Tests`: 1,396 passed (every problem × 21 modes × 6 phone types, queue
concurrency, session feedback). End-to-end with the tool's real client against the running server: all checks passed.
Details: `MUTASSIM_TOOL_PRO_V1.5\docs\SMART_ASSIST.md`.
