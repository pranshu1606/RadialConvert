# Builds dist\RadialConvert.exe: a single self-contained exe that runs on any 64-bit Windows 10/11 PC
# without installing .NET. Usage:  powershell -ExecutionPolicy Bypass -File publish.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet publish "$root\src\RadialConvert\RadialConvert.csproj" -c Release -o "$root\dist"
Get-Item "$root\dist\RadialConvert.exe" | Select-Object FullName, @{ n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 1) } }
