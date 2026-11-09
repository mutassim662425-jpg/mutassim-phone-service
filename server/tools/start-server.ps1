# Usage:
#   .\tools\start-server.ps1                                  (Development, no API key)
#   .\tools\start-server.ps1 -ApiKey "your-secret"
#   .\tools\start-server.ps1 -AdminUser admin -AdminPass "strong-pass"   (create the first admin, once)
#   .\tools\start-server.ps1 -DbPath "C:\MUTASSIM\mutassim.db"           (choose where credits are stored)
param([string]$ApiKey, [string]$AdminUser, [string]$AdminPass, [string]$DbPath)
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
if ($ApiKey)    { $env:MUTASSIM_API_KEY   = $ApiKey }
if ($AdminUser) { $env:MUTASSIM_ADMIN_USER = $AdminUser }
if ($AdminPass) { $env:MUTASSIM_ADMIN_PASS = $AdminPass }
if ($DbPath)    { $env:MUTASSIM_DB_PATH    = $DbPath }
dotnet run --project (Join-Path $root "MUTASSIM_SERVER\MUTASSIM_SERVER.csproj") --launch-profile http
