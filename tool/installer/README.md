# Installer (Phase 9)

Planned: Inno Setup 6 script (`MutassimToolPro.iss`) packaging `artifacts/publish/win-x64` (self-contained .NET 10),
signed with the MUTASSIM SOFTWARE code-signing certificate, with Start-menu/desktop shortcuts and per-user data in
`%LOCALAPPDATA%\MUTASSIM\ToolPro` (kept on uninstall unless the user chooses to remove it).

Until then: `./build.ps1 -Publish` produces a runnable self-contained folder.
