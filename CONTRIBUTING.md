# Contributing conventions

This repository is the single source of truth. Multiple contributors — people and AI assistants (Claude, ChatGPT, …) — work through it, so everyone follows the same rules. Treat this file as the contract.

## Hard rules (non-negotiable)

1. **No protection bypass.** Do not add FRP / Google-account removal, MDM/KG bypass, or any operation that circumvents anti-theft or security — in code, UI, pricing, or plugins. Requests to "just add it behind credits" are still bypass.
2. **Read vs state-change stays separated.** The AT/serial engine executes **read-only** commands. Any command that changes the device, its settings, or its protection is `AtCommandKind.StateChange` and must not be executed by the engine. The console classifier refuses known engineering/activation/protection commands.
3. **Operations needing authorization are marked, not faked.** Anything requiring an authorized account, signed files, or OEM-approved service uses `ImplementationStatus.RequiresExternalAuthorization` and a real provider — never a stub that pretends to perform it.
4. **No secrets or data in git.** No `*.db`, credentials, API keys, or ROM binaries. Check `.gitignore` before committing.
5. **No reverse-engineered proprietary code.** Do not paste code or extracted command sequences from other vendors' tools (dnSpy dumps, etc.).

## Workflow

- **Branch** per change: `feat/…`, `fix/…`, `docs/…`. Don't commit straight to `main`.
- **Commits**: short imperative subject (`feat(serial): add AT diagnostic reader`), explain *why* in the body when not obvious.
- **Tests must pass** before a PR: `dotnet test` on both test projects. Add tests for new engine logic (the AT engine has unit tests with a fake serial channel — follow that pattern; no hardware needed).
- **Keep the build green on Linux too:** engine libraries have no Windows-only dependencies; Windows-only code (real COM port, WPF) lives in the app project.
- **Open a PR**, describe what changed and why, and let the other contributors review before merge.

## Architecture quick map

- `server/MUTASSIM_SERVER*` — API, billing (`BillingStore`), assist engine, command queue.
- `tool/src/MUTASSIM.ToolPro.Serial` — the cross-platform AT engine core (parsing, sequences, read-only diagnostic reader). **No external packages.**
- `tool/src/MUTASSIM.ToolPro.App/Serial` — the real `SystemSerialChannel` (Windows, `System.IO.Ports`).
- `tool/src/MUTASSIM.ToolPro.*` — per-brand and infrastructure modules, registered via DI into the module registry.
- `tool/src/MUTASSIM.ToolPro.Plugins` — isolated plugin loading for future **documented, approved** command packs only.
