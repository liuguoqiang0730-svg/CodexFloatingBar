# CodexFloatingBar

CodexFloatingBar is a compact .NET 8 WPF floating bar for Windows.

Repository: https://github.com/liuguoqiang0730-svg/CodexFloatingBar

## MVP features
- Borderless, small, topmost, draggable window
- Remembers the last window position
- Optional current-user startup toggle from the tray menu
- Close button hides the window to tray; tray menu can show or hide it
- Reads `C:\Users\ehang\.codex\config.toml` for `model` and `model_reasoning_effort`
- Surfaces missing, inaccessible, or temporarily unreadable config files in the bar
- Manual refresh plus file change monitoring
- Tray menu: refresh, show/hide window, open config, open ChatGPT account page, open Billing page, open GitHub repository, startup toggle, exit

## Notes
- Balance, quota, and expiry are shown as manual-check only because there is no stable local read for them.
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
