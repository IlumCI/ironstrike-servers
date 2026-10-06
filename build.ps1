#requires -Version 5
<#
  Build (and optionally install) IronstrikeServers on Windows.

    .\build.ps1
    .\build.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\IRONSTRIKE"
    .\build.ps1 -GameDir ... -Package

  Needs the .NET SDK (6.0 or newer): https://dotnet.microsoft.com/download
  Reference assemblies are committed in refs\, so no game files are needed to compile.
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $here 'IronstrikeServers\IronstrikeServers.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet not found. Install the .NET SDK: https://dotnet.microsoft.com/download"
}

# Prefer the interop set the game generated at runtime; it is guaranteed to match the live build.
$interop = Join-Path $here 'refs'
if ($GameDir) {
    $live = Join-Path $GameDir 'BepInEx\interop'
    if (Test-Path (Join-Path $live 'GameAssembly.dll')) {
        $interop = $live
        Write-Host "refs: game-generated ($live)"
    }
}
if ($interop -eq (Join-Path $here 'refs')) { Write-Host "refs: committed refs\" }

dotnet build $proj -c Release --nologo "-p:InteropDir=$interop"
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = Join-Path $here 'IronstrikeServers\bin\Release\net6.0\IronstrikeServers.dll'
Write-Host "built: $dll"

if ($GameDir) {
    $dest = Join-Path $GameDir 'BepInEx\plugins'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item $dll $dest -Force
    Write-Host "installed to $dest"
    Write-Host "tail the log:  Get-Content -Wait '$GameDir\BepInEx\LogOutput.txt'"
}

if ($Package) {
    $ver = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $stage = Join-Path $here "dist\stage"
    $out   = Join-Path $here "dist\IronstrikeServers-$ver.zip"
    Remove-Item -Recurse -Force (Join-Path $here 'dist') -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'BepInEx\plugins') | Out-Null
    Copy-Item $dll (Join-Path $stage 'BepInEx\plugins') -Force
    Copy-Item (Join-Path $here 'README.md') $stage -Force
    Copy-Item (Join-Path $here 'icon.png')  $stage -Force
    @{
        name           = 'IronstrikeServers'
        version_number = $ver
        website_url    = ''
        description    = 'Adds a TRAINER menu next to Options. Solo and Private Match only; public matchmaking is locked while loaded.'
        dependencies   = @('BepInEx-BepInExPack_IL2CPP-6.0.755')
    } | ConvertTo-Json | Set-Content (Join-Path $stage 'manifest.json')
    Compress-Archive -Path "$stage\*" -DestinationPath $out -Force
    Remove-Item -Recurse -Force $stage
    Write-Host "packaged: $out"
}
