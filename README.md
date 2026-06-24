# CodexFloatingBar

CodexFloatingBar is a compact .NET 8 WPF floating bar for Windows.

## MVP features
- Borderless, small, topmost, draggable window
- Reads `C:\Users\ehang\.codex\config.toml` for `model` and `model_reasoning_effort`
- Manual refresh plus file change monitoring
- Tray menu: refresh, open config, open ChatGPT account page, open Billing page, exit

## Notes
- Balance, quota, and expiry are shown as manual-check only because there is no stable local read for them.

## Build

```powershell
C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe build .\CodexFloatingBar.sln
```
