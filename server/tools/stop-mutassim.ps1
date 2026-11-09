# Stops ONLY this solution's running Server/Agent processes (fixes MSB3027 / MSB3021
# "file is being used by another process"). Never touches unrelated processes.
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$names = @("MUTASSIM_SERVER", "MUTASSIM_DEVICE_AGENT")

$targets = Get-CimInstance Win32_Process | Where-Object {
    $exe = $_.ExecutablePath; $cmd = $_.CommandLine
    (($names -contains [IO.Path]::GetFileNameWithoutExtension($_.Name)) -and $exe -and $exe.StartsWith($root, 'OrdinalIgnoreCase')) -or
    ($_.Name -eq "dotnet.exe" -and $cmd -and $cmd.Contains($root) -and ($names | Where-Object { $cmd.Contains($_) }))
}

if (-not $targets) { Write-Host "No MUTASSIM processes running from $root"; exit 0 }

foreach ($p in $targets) {
    Write-Host "Stopping $($p.Name) (PID $($p.ProcessId))"
    Stop-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 1
Write-Host "Done. You can rebuild now."
