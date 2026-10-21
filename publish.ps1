# Builds the release files in dist\:
#   RadialConvert.exe        single self-contained exe (runs without installing .NET)
#   RadialConvert-Setup.exe  installer: desktop icon, launch at startup, uninstaller
# Usage:  powershell -ExecutionPolicy Bypass -File publish.ps1
# Needs the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = "$root\src\RadialConvert\RadialConvert.csproj"

dotnet publish $project -c Release -o "$root\dist"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
          "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc /Q "/DAppVersion=$version" "$root\installer\RadialConvert.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Get-Item "$root\dist\RadialConvert.exe", "$root\dist\RadialConvert-Setup.exe" |
    Select-Object Name, @{ n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 1) } }
