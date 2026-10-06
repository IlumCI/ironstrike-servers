#requires -Version 5
<#
  Diagnose why BepInEx didn't load. Run it with the game closed:

    .\bepinex-doctor.ps1 -GameDir "C:\Program Files (x86)\Steam\steamapps\common\IRONSTRIKE"

  "No new folders appeared on launch" means Doorstop never injected. Everything below is a
  known cause of exactly that.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDir)

$ErrorActionPreference = 'Continue'
$fail = 0
function Pass($m) { Write-Host "  [ ok ] $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  [FAIL] $m" -ForegroundColor Red; $script:fail++ }
function Warn($m) { Write-Host "  [warn] $m" -ForegroundColor Yellow }
function Note($m) { Write-Host "         $m" -ForegroundColor DarkGray }

Write-Host "`nIRONSTRIKE / BepInEx doctor" -ForegroundColor Cyan
Write-Host "game dir: $GameDir`n"

if (-not (Test-Path (Join-Path $GameDir 'Ironstrike.exe'))) {
    Fail "Ironstrike.exe not here. This is the wrong folder."
    Note "In Steam: right-click IRONSTRIKE -> Manage -> Browse local files."
    exit 1
}
Pass "Ironstrike.exe found"

Write-Host "`n1. Is the game IL2CPP (it must be, and it decides which BepInEx you need)"
if (Test-Path (Join-Path $GameDir 'GameAssembly.dll')) {
    Pass "GameAssembly.dll present -> IL2CPP. You need the Unity.IL2CPP win-x64 build."
} else {
    Fail "No GameAssembly.dll"
}

Write-Host "`n2. Doorstop files, which must sit NEXT TO Ironstrike.exe"
$wh = Join-Path $GameDir 'winhttp.dll'
if (Test-Path $wh) {
    $len = (Get-Item $wh).Length
    Pass "winhttp.dll present ($len bytes)"
    if ($len -lt 20000) { Warn "suspiciously small for Doorstop" }
} else {
    Fail "winhttp.dll MISSING -- this alone explains no folders appearing"
    Note "Most common causes: the zip extracted into a subfolder, so move the CONTENTS up;"
    Note "or antivirus deleted it. Doorstop proxy DLLs get flagged constantly."
}

$cfg = Join-Path $GameDir 'doorstop_config.ini'
$v   = Join-Path $GameDir '.doorstop_version'
if (Test-Path $cfg) {
    Pass "doorstop_config.ini present"
    $txt = Get-Content $cfg -Raw
    if ($txt -match '(?im)^\s*enabled\s*=\s*false') { Fail "doorstop_config.ini has enabled=false" }
    else { Pass "doorstop enabled" }
} elseif (Test-Path $v) {
    Pass ".doorstop_version present (newer Doorstop layout)"
} else {
    Warn "no doorstop_config.ini and no .doorstop_version"
}

Write-Host "`n3. The bundled .NET runtime (BepInEx 6 IL2CPP ships its own; without it nothing runs)"
if (Test-Path (Join-Path $GameDir 'dotnet\coreclr.dll')) { Pass "dotnet\ runtime present" }
elseif (Test-Path (Join-Path $GameDir 'dotnet'))         { Warn "dotnet\ exists but no coreclr.dll" }
else {
    Fail "no dotnet\ folder -- you likely grabbed the Unity MONO build, not IL2CPP"
    Note "Get BepInEx-Unity.IL2CPP-win-x64 from https://builds.bepinex.dev/projects/bepinex_be"
}

Write-Host "`n4. BepInEx folder"
$b = Join-Path $GameDir 'BepInEx'
if (Test-Path $b) {
    Pass "BepInEx\ present"
    foreach ($d in 'core','plugins','config','interop') {
        $p = Join-Path $b $d
        if (Test-Path $p) {
            $n = @(Get-ChildItem $p -File -ErrorAction SilentlyContinue).Count
            Pass "BepInEx\$d ($n files)"
        } else {
            if ($d -eq 'core') { Fail "BepInEx\core missing -- extraction was incomplete" }
            else { Warn "BepInEx\$d not created yet (expected if the loader has never run)" }
        }
    }
    if (Test-Path (Join-Path $b 'core\BepInEx.Unity.IL2CPP.dll')) { Pass "IL2CPP core assembly present" }
    elseif (Test-Path (Join-Path $b 'core\BepInEx.Unity.Mono.dll')) { Fail "core is the MONO build. Wrong variant." }
} else {
    Fail "no BepInEx\ folder"
}

Write-Host "`n5. Log"
$log = Join-Path $b 'LogOutput.log'
if (Test-Path $log) {
    Pass "LogOutput.log exists -> Doorstop DID inject at least once"
    Note "last lines:"
    Get-Content $log -Tail 12 | ForEach-Object { Write-Host "           $_" -ForegroundColor DarkGray }
    if (Select-String -Path $log -Pattern 'HOOK CONFIRMED' -Quiet) { Pass "our plugin's hooks fired" }
    elseif (Select-String -Path $log -Pattern 'IronstrikeServers' -Quiet) { Warn "plugin loaded but hooks have not fired" }
    else { Warn "no trace of IronstrikeServers in the log" }
} else {
    Fail "no BepInEx\LogOutput.log -- the loader has never run"
}

Write-Host "`n6. Antivirus / Mark-of-the-Web"
if (Test-Path $wh) {
    $z = Get-Item $wh -Stream Zone.Identifier -ErrorAction SilentlyContinue
    if ($z) {
        Fail "winhttp.dll is marked as downloaded-from-internet; Windows may refuse to load it"
        Note "Fix:  Unblock-File -Path '$wh'"
        Note "Also unblock recursively:  Get-ChildItem '$GameDir' -Recurse | Unblock-File"
    } else { Pass "winhttp.dll not web-marked" }
}
try {
    $ex = (Get-MpPreference -ErrorAction Stop).ExclusionPath
    if ($ex -and ($ex | Where-Object { $GameDir -like "$_*" })) { Pass "game dir is a Defender exclusion" }
    else { Warn "game dir is NOT a Defender exclusion; Defender quietly eats Doorstop DLLs" ; Note "Add-MpPreference -ExclusionPath '$GameDir'" }
} catch { Note "could not read Defender config (needs admin); skipping" }

Write-Host "`n7. Steam launch options"
Note "IL2CPP on Windows needs NO launch options. If you set WINEDLLOVERRIDES (a Linux/Proton"
Note "thing) remove it. Also make sure Steam launches Ironstrike.exe directly and not a"
Note "VR wrapper that re-execs something else."

Write-Host ""
if ($fail -eq 0) { Write-Host "No blocking problems found." -ForegroundColor Green }
else { Write-Host "$fail blocking problem(s) above." -ForegroundColor Red }
Write-Host ""
