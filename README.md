# CodexFloatingBar

CodexFloatingBar is a compact .NET 8 WPF floating bar for Windows.

Repository: https://github.com/liuguoqiang0730-svg/CodexFloatingBar

## MVP features
- Borderless, small, topmost, draggable window
- Slim status-bar UI with grouped config, runtime, and account sections
- Switchable dark and gray-white themes with persisted scale options
- Defaults to 70% of the current work-area width
- Resizable window with persisted width, height, and position
- Optional current-user startup toggle from the tray menu
- Single-instance guard to avoid duplicate floating bars and tray icons
- Close button hides the window to tray; tray menu can show or hide it
- Reads `C:\Users\ehang\.codex\config.toml` for `model` and `model_reasoning_effort`
- Shows Codex account name/email from local ID-token claims without displaying token values
- Shows Codex 5-hour and weekly remaining usage from local `codex.rate_limits` log events
- Surfaces missing, inaccessible, or temporarily unreadable config files in the bar
- Tray menu can copy the current visible status to the clipboard
- Manual refresh plus file change monitoring
- Tray menu: refresh, copy current status, show/hide window, open config, open ChatGPT account page, open Billing/API usage/API Keys pages, open GitHub repository, startup toggle, exit

## Notes
- Balance and expiry are shown as manual-check only because there is no stable local read for them.
- Codex remaining usage is parsed from local client logs under `%USERPROFILE%\.codex\logs_2.sqlite*`; it updates after Codex receives a `codex.rate_limits` event.
- Window placement and size are stored under `%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`.
- Appearance is stored under `%LOCALAPPDATA%\CodexFloatingBar\appearance.json`.
- Startup is stored under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## Run

```powershell
C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe run --project .\src\CodexFloatingBar\CodexFloatingBar.csproj
```

## Build

```powershell
C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe build .\CodexFloatingBar.sln
```

## Publish

```powershell
.\scripts\publish.ps1
```

The default publish output is `artifacts\publish\win-x64`.
