# Usage:  .\tools\start-agent.ps1 [-ApiKey "your-secret"] [-DeviceId "PC-01"] [-ServerUrl "http://localhost:5179"] [-AdbPath "C:\platform-tools\adb.exe"]
param([string]$ApiKey, [string]$DeviceId, [string]$ServerUrl, [string]$AdbPath)
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if ($ApiKey)    { $env:MUTASSIM_API_KEY    = $ApiKey }
if ($DeviceId)  { $env:MUTASSIM_DEVICE_ID  = $DeviceId }
if ($ServerUrl) { $env:MUTASSIM_SERVER_URL = $ServerUrl }
if ($AdbPath)   { $env:MUTASSIM_ADB_PATH   = $AdbPath }
dotnet run --project (Join-Path $root "MUTASSIM_DEVICE_AGENT\MUTASSIM_DEVICE_AGENT.csproj")
