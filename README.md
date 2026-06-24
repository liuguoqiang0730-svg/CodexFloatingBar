# CodexFloatingBar

CodexFloatingBar is a compact .NET 8 WPF floating bar for Windows.

Repository: https://github.com/liuguoqiang0730-svg/CodexFloatingBar

## MVP features
- Borderless, small, topmost, draggable window
- Compact status-panel UI with grouped config, runtime, and account sections
- Remembers the last window position
- Optional current-user startup toggle from the tray menu
- Single-instance guard to avoid duplicate floating bars and tray icons
- Close button hides the window to tray; tray menu can show or hide it
- Reads `C:\Users\ehang\.codex\config.toml` for `model` and `model_reasoning_effort`
- Optionally reads today's API cost and token usage when `OPENAI_ADMIN_API_KEY` is set
- Surfaces missing, inaccessible, or temporarily unreadable config files in the bar
- Tray menu can copy the current visible status to the clipboard
- Manual refresh plus file change monitoring
- Tray menu: refresh, copy current status, show/hide window, open config, open ChatGPT account page, open Billing page, open GitHub repository, startup toggle, exit

## Notes
- Balance, quota, and expiry are shown as manual-check only because there is no stable local read for them.
- API usage/cost uses OpenAI Admin API endpoints and requires an Admin API key in `OPENAI_ADMIN_API_KEY`.
- Window placement is stored under `%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`.
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
