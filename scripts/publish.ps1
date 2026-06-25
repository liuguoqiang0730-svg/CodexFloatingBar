param(
    [string]$DotnetPath = "C:\Users\ehang\AppData\Local\Microsoft\dotnet\dotnet.exe",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\CodexFloatingBar\CodexFloatingBar.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$publishedExe = Join-Path $publishDir "CodexFloatingBar.exe"

if (-not (Test-Path -LiteralPath $DotnetPath)) {
    throw "dotnet executable not found: $DotnetPath"
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& $DotnetPath publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Published to $publishDir"

$desktopDir = [Environment]::GetFolderPath("DesktopDirectory")
if (-not [string]::IsNullOrWhiteSpace($desktopDir)) {
    $shortcutPath = Join-Path $desktopDir "CodexFloatingBar.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $publishedExe
    $shortcut.WorkingDirectory = $publishDir
    $shortcut.Description = "Codex local status floating bar"
    $shortcut.IconLocation = "$publishedExe,0"
    $shortcut.Save()
    Write-Host "Desktop shortcut updated: $shortcutPath"
}
