# CodexFloatingBar

[中文](#中文) | [English](#english)

> Unofficial community tool. This project is not affiliated with, endorsed by, or maintained by OpenAI.

## 中文

CodexFloatingBar 是一个用于 Windows 桌面的 Codex 本地状态悬浮条，基于 .NET 8 和 WPF 构建。它会读取当前 Windows 用户目录下的本地 Codex 配置和日志，在桌面上显示模型、推理强度、速率、账号信息以及 5 小时 / 1 周剩余额度。

### 功能

- 无边框、置顶、可拖动的悬浮窗口
- 横版 / 竖版布局，可一键切换
- 黑色色调和灰白色色调，可一键切换
- 支持窗口缩放、拖拽调整大小，并记住位置和尺寸
- 横版默认贴近当前屏幕正上方居中，竖版默认贴近当前屏幕右上角
- 悬浮条隐藏按钮，可从系统托盘恢复
- 系统托盘菜单：刷新、复制状态、显示 / 隐藏、开机启动、账号相关链接、退出
- 支持当前用户开机自启
- 单实例保护，减少重复托盘图标
- 读取 `%USERPROFILE%\.codex\config.toml` 中配置的 `model` 和 `model_reasoning_effort`
- 优先从本地日志显示当前活跃 Codex 会话的模型、推理强度和速率
- 从本地 ID token claims 显示 Codex 账号昵称 / 邮箱，不展示 token 原文
- 从本地 `codex.rate_limits` 日志事件显示 5 小时和 1 周剩余额度
- 用进度条显示剩余额度，并按绿色 / 黄色 / 红色提示额度状态
- 监听本地 Codex 文件变化并自动刷新

### 读取的数据

CodexFloatingBar 只读取当前 Windows 用户本机文件：

- `%USERPROFILE%\.codex\config.toml`
- `%USERPROFILE%\.codex\auth.json`
- `%USERPROFILE%\.codex\logs_2.sqlite*`
- `%USERPROFILE%\.codex\state_5.sqlite*`

这些文件仅用于展示本地配置、账号身份、当前会话状态和剩余额度。应用不会上传数据，也不会发送遥测。

### 限制

- Codex / ChatGPT 目前没有为所有账号信息提供稳定的本地 API。
- 余额和账单明细暂不从本地读取，请通过托盘菜单里的官方链接查看。
- 剩余额度需要本地 Codex 客户端产生 `codex.rate_limits` 日志事件后才会显示。
- 本地日志格式不是公开稳定协议，后续 Codex 客户端变化可能需要本项目适配。

### 运行要求

- Windows 10 或更高版本
- 开发构建需要 .NET 8 SDK

### 下载使用

从 [Releases](https://github.com/liuguoqiang0730-svg/CodexFloatingBar/releases) 下载最新版本，解压后运行：

```text
CodexFloatingBar.exe
```

### 开发运行

```powershell
dotnet run --project .\src\CodexFloatingBar\CodexFloatingBar.csproj
```

### 构建

```powershell
dotnet build .\CodexFloatingBar.sln
```

### 发布

```powershell
.\scripts\publish.ps1
```

默认发布输出目录：

```text
artifacts\publish\win-x64
```

发布脚本会创建或刷新桌面快捷方式 `CodexFloatingBar.lnk`，并指向发布后的可执行文件。

如果 `dotnet` 不在 `PATH` 中，可以指定 SDK 路径：

```powershell
.\scripts\publish.ps1 -DotnetPath "C:\Path\To\dotnet.exe"
```

### 本地设置位置

- 窗口位置：`%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`
- 外观设置：`%LOCALAPPDATA%\CodexFloatingBar\appearance.json`
- 开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### 许可证

本项目使用 MIT License，详见 [LICENSE](LICENSE)。

## English

CodexFloatingBar is a Windows desktop floating bar for local Codex status. It is built with .NET 8 and WPF. The app reads local Codex configuration and log files from the current Windows user profile, then shows the selected model, reasoning effort, speed tier, account identity, and 5-hour / weekly remaining usage.

### Features

- Borderless, always-on-top, draggable floating window
- Horizontal and vertical layouts with quick switching
- Dark and gray-white themes with quick switching
- Resizable window with persisted size and position
- Desktop-friendly placement: horizontal mode anchors near the top center of the current screen; vertical mode anchors near the top right
- Hide button on the floating bar, with restore from tray
- Tray menu for refresh, copy status, show/hide, startup toggle, account links, and exit
- Optional current-user startup registration
- Single-instance guard to reduce duplicate tray icons
- Reads configured `model` and `model_reasoning_effort` from `%USERPROFILE%\.codex\config.toml`
- Shows the latest active Codex conversation model, reasoning effort, and speed tier from local logs when available
- Shows Codex account display name/email from local ID-token claims without displaying token values
- Shows Codex 5-hour and weekly remaining usage from local `codex.rate_limits` log events
- Displays remaining usage as progress bars with green / yellow / red status colors
- Watches local Codex files and refreshes status automatically

### Data Read By The App

CodexFloatingBar only reads local files from the current Windows user profile:

- `%USERPROFILE%\.codex\config.toml`
- `%USERPROFILE%\.codex\auth.json`
- `%USERPROFILE%\.codex\logs_2.sqlite*`
- `%USERPROFILE%\.codex\state_5.sqlite*`

The app uses these files to display local configuration, account identity, active session status, and remaining usage. It does not upload data or send telemetry.

### Limitations

- Codex and ChatGPT do not currently provide a stable local API for every account detail.
- Balance and billing details are not read locally; use the tray links to check official pages.
- Remaining usage appears after the local Codex client receives `codex.rate_limits` events.
- The local log format is not a public stability contract, so future Codex client changes may require updates.

### Requirements

- Windows 10 or later
- .NET 8 SDK for development

### Download

Download the latest build from [Releases](https://github.com/liuguoqiang0730-svg/CodexFloatingBar/releases), extract it, then run:

```text
CodexFloatingBar.exe
```

### Run From Source

```powershell
dotnet run --project .\src\CodexFloatingBar\CodexFloatingBar.csproj
```

### Build

```powershell
dotnet build .\CodexFloatingBar.sln
```

### Publish

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

### Local Settings

- Window placement: `%LOCALAPPDATA%\CodexFloatingBar\window-placement.json`
- Appearance settings: `%LOCALAPPDATA%\CodexFloatingBar\appearance.json`
- Startup registration: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`

### License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
