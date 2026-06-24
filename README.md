# CodexFloatingBar

CodexFloatingBar is a compact .NET 8 WPF floating bar for Windows.

Repository: https://github.com/liuguoqiang0730-svg/CodexFloatingBar

## MVP features
- Borderless, small, topmost, draggable window
- Remembers the last window position
- Reads `C:\Users\ehang\.codex\config.toml` for `model` and `model_reasoning_effort`
- Manual refresh plus file change monitoring
- Tray menu: refresh, open config, open ChatGPT account page, open Billing page, open GitHub repository, exit

## Notes
- Balance, quota, and expiry are shown as manual-check only because there is no stable local read for them.
- Window placement is stored under `%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`.

## Run

```powershell
C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe run --project .\src\CodexFloatingBar\CodexFloatingBar.csproj
```

## Build

```powershell
C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe build .\CodexFloatingBar.sln
```
