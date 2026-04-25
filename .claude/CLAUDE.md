# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Repo Is

PowerToys is a collection of Windows productivity utilities. See [AGENTS.md](../AGENTS.md) for the full AI contributor guide.

| Area | Location | Description |
|------|----------|-------------|
| Runner | `src/runner/` | Main executable, tray icon, module loader, hotkey management |
| Settings UI | `src/settings-ui/` | WinUI/WPF config app communicating via named pipes |
| Modules | `src/modules/` | Individual utilities, each in its own subfolder |
| Common Libraries | `src/common/` | Logging, IPC, settings, DPI, telemetry, utilities |
| Installer | `installer/` | WiX-based installer projects |

## Build

Prerequisites: Visual Studio 2022 17.4+, Windows 10 1803+. Initialize submodules once:
```
git submodule update --init --recursive
```

| Task | Command |
|------|---------|
| First build / NuGet restore | `tools\build\build-essentials.cmd` |
| Build current folder | `tools\build\build.cmd` |
| Full build with options | `build.ps1 -Platform x64 -Configuration Release` |

- After making changes, `cd` to the changed project folder (`.csproj`/`.vcxproj`) before building
- Exit code 0 = success. On failure, read `build.<config>.<platform>.errors.log`
- Do not run tests until the build succeeds

## Tests

- Find test projects by prefix: look for `<Product>*UnitTests` or `<Product>*UITests` sibling folders
- **Do not use `dotnet test`** — use VS Test Explorer (`Ctrl+E, T`) or `vstest.console.exe`
- Build the test project first, then run
- UI Tests require WinAppDriver v1.2.1 and Developer Mode enabled
- Mouse Without Borders requires 2+ physical computers (not VMs)
- Multi-monitor utilities require 2+ monitors with different DPI settings

## Architecture

### Module Types

1. **Simple Modules** (Mouse Pointer Crosshairs, Find My Mouse) — entirely in the module DLL, no external app
2. **External Application Launchers** (Color Picker) — start a separate C# app, communicate via named pipes
3. **Context Handler Modules** (Power Rename) — shell extensions for File Explorer right-click menus
4. **Registry-based Modules** (Power Preview) — register preview handlers by modifying registry keys

### IPC / Settings Contract

- Runner ↔ Settings UI communicate via named pipes with JSON payloads
- Breaking IPC or JSON schema requires updating **both** runner and settings-ui together
- Settings are defined in `src/settings-ui/` ViewModels and serialized to `%APPDATA%\Microsoft\PowerToys\`

## Style

- C#: `src/.editorconfig` + StyleCop.Analyzers
- C++: `src/.clang-format`
- XAML: XamlStyler

## Key Rules

- Atomic PRs: one logical change, no drive-by refactors
- No noisy logging in hot paths (hooks, tight loops)
- No new third-party dependencies without PM approval and `NOTICE.md` update
- Don't break IPC/JSON contracts without updating both sides

## Areas Requiring Extra Care

- `src/common/` — changes may cause ABI breaks visible to all modules
- `src/runner/` and `src/settings-ui/` — IPC contracts must stay in sync
- Installer files — reviewed carefully before every release
- Elevation/GPO logic — confirm no policy-handling regressions
