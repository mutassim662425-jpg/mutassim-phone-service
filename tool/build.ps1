# MUTASSIM TOOL PRO — build & test script (Windows PowerShell 5+ / PowerShell 7)
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $Publish
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "== .NET SDK ==" -ForegroundColor Cyan
dotnet --version

Write-Host "== Restore ==" -ForegroundColor Cyan
dotnet restore MUTASSIM_TOOL_PRO.sln

Write-Host "== Build ($Configuration) ==" -ForegroundColor Cyan
dotnet build MUTASSIM_TOOL_PRO.sln -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "== Unit tests ==" -ForegroundColor Cyan
dotnet test tests/MUTASSIM.ToolPro.Tests.Unit -c $Configuration --no-build --logger "trx;LogFileName=unit.trx" --results-directory artifacts/test-results
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

if ($Publish) {
    Write-Host "== Publish win-x64 ==" -ForegroundColor Cyan
    dotnet publish src/MUTASSIM.ToolPro.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
}

Write-Host "Done." -ForegroundColor Green
