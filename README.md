# CodexFloatingBar

CodexFloatingBar is a compact Windows floating bar for showing local Codex status. It is built with .NET 8 and WPF.

> This is an unofficial community tool. It is not affiliated with, endorsed by, or maintained by OpenAI.

## Features

- Borderless, always-on-top, draggable floating window
- Horizontal and vertical layouts with quick switching
- Black frosted and gray-white themes
- Resizable window with persisted size and position
- Desktop-friendly placement: horizontal mode anchors near the top center, vertical mode anchors near the top right
- Hide button on the floating bar, with restore from tray
- Tray menu for refresh, copy status, show/hide, startup toggle, useful account links, and exit
- Optional current-user startup registration
- Single-instance guard to avoid duplicate tray icons
- Reads `%USERPROFILE%\.codex\config.toml` for configured `model` and `model_reasoning_effort`
- Shows the latest active Codex conversation model, reasoning effort, and speed tier from local logs when available
- Shows Codex account display name/email from local ID-token claims without displaying token values
- Shows Codex 5-hour and weekly remaining usage from local `codex.rate_limits` log events
- Watches local Codex files and refreshes status automatically

## What It Reads

CodexFloatingBar only reads local files from the current Windows user profile:

- `%USERPROFILE%\.codex\config.toml`
- `%USERPROFILE%\.codex\auth.json`
- `%USERPROFILE%\.codex\logs_2.sqlite*`
- `%USERPROFILE%\.codex\state_5.sqlite*`

The app uses these files to display local configuration, account identity, active session status, and remaining usage. It does not upload data or send telemetry.

## Limitations

- Codex and ChatGPT do not currently provide a stable local API for every account detail.
- Balance and billing details are not read locally; use the tray links to check official pages.
- Remaining usage appears after the local Codex client receives `codex.rate_limits` events.
- The local log format is not a public stability contract, so future Codex client changes may require updates.

## Requirements

- Windows 10 or later
- .NET 8 SDK for development

## Run

```powershell
dotnet run --project .\src\CodexFloatingBar\CodexFloatingBar.csproj
```

If `dotnet` is not on `PATH`, pass the full path to your local SDK executable when publishing.

## Build

```powershell
dotnet build .\CodexFloatingBar.sln
```

## Publish

```powershell
.\scripts\publish.ps1
```

The default publish output is:

```text
artifacts\publish\win-x64
```

Publishing also creates or refreshes a desktop shortcut named `CodexFloatingBar.lnk` that points to the published executable.

To use a custom .NET SDK path:

```powershell
.\scripts\publish.ps1 -DotnetPath "C:\Path\To\dotnet.exe"
```

## Local Settings

- Window placement: `%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`
- Appearance settings: `%LOCALAPPDATA%\CodexFloatingBar\appearance.json`
- Startup registration: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
