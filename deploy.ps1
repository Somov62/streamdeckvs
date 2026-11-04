<#
.SYNOPSIS
    Deploys the built plugin into fifine Control Deck (MiraBox StreamDock).

.DESCRIPTION
    1. Closes fifine Control Deck (graceful CloseMainWindow, then Stop-Process after a timeout).
    2. Kills the plugin process com.nicollasr.streamdeckvs if still running.
    3. Backs up the current plugin folder to <repo>\..\backups\<timestamp>\ (outside the
       plugins dir, so Control Deck does not load the backup as a second plugin).
    4. Replaces the plugin folder contents with bin\<Configuration>\net472\* (no *.pdb),
       keeping existing *.log files.
    5. Starts fifine Control Deck again.

    Button settings are stored by Control Deck itself, not in the plugin folder.
#>
param(
    [string]$Configuration = 'Release',
    [int]$CloseTimeoutSec = 10,
    [switch]$NoRestart
)

$ErrorActionPreference = 'Stop'

$pluginName   = 'com.nicollasr.streamdeckvs'
$pluginDir    = Join-Path $env:APPDATA "HotSpot\StreamDock\plugins\$pluginName.sdPlugin"
$buildDir     = Join-Path $PSScriptRoot "StreamDeckVS\bin\$Configuration\net472"
$backupRoot   = Join-Path (Split-Path $PSScriptRoot -Parent) 'backups'
$deckProcName = 'fifine Control Deck'
$deckExeDefault = "${env:ProgramFiles(x86)}\fifine Control Deck\fifine Control Deck.exe"

if (-not (Test-Path (Join-Path $buildDir "$pluginName.exe"))) {
    throw "Build output not found: $buildDir. Run: dotnet build StreamDeckVS\StreamDeckVS.csproj -c $Configuration"
}

# --- 1. Close Control Deck -------------------------------------------------
$deckExe = $deckExeDefault
$deck = Get-Process -Name $deckProcName -ErrorAction SilentlyContinue
if ($deck) {
    $p = $deck | Where-Object { $_.Path } | Select-Object -First 1
    if ($p) { $deckExe = $p.Path }

    Write-Host "Closing '$deckProcName' (PID $($deck.Id -join ', '))..."
    foreach ($proc in $deck) { [void]$proc.CloseMainWindow() }   # no-op if it's only in the tray
    $exited = @($deck | ForEach-Object { $_.WaitForExit($CloseTimeoutSec * 1000) })
    if ($exited -notcontains $false) {
        Write-Host 'Closed gracefully.'
    } else {
        Write-Host "Still running after ${CloseTimeoutSec}s, stopping forcibly."
        $deck | Where-Object { -not $_.HasExited } | Stop-Process -Force
        $deck | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
    }
} else {
    Write-Host "'$deckProcName' is not running."
}

# --- 2. Kill the plugin process -------------------------------------------
$plugin = Get-Process -Name $pluginName -ErrorAction SilentlyContinue
if ($plugin) {
    Write-Host "Stopping plugin process (PID $($plugin.Id -join ', '))..."
    $plugin | Stop-Process -Force
    $plugin | ForEach-Object { $_.WaitForExit(5000) | Out-Null }
}

# --- 3. Backup -------------------------------------------------------------
if (Test-Path $pluginDir) {
    $backupDir = Join-Path $backupRoot ("{0}.sdPlugin_{1:yyyyMMdd_HHmmss}" -f $pluginName, (Get-Date))
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -Path (Join-Path $pluginDir '*') -Destination $backupDir -Recurse -Force
    Write-Host "Backup: $backupDir"

    # Clean old files (except logs) so stale DLLs from the previous version don't linger.
    Get-ChildItem -Path $pluginDir -Force | Where-Object { $_.Extension -ne '.log' } |
        Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
}

# --- 4. Copy build output --------------------------------------------------
Get-ChildItem -Path $buildDir -Recurse -File | Where-Object { $_.Extension -ne '.pdb' } | ForEach-Object {
    $rel  = $_.FullName.Substring($buildDir.Length).TrimStart('\')
    $dest = Join-Path $pluginDir $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item -Path $_.FullName -Destination $dest -Force
}
Write-Host "Deployed $buildDir -> $pluginDir"

# --- 5. Restart Control Deck ----------------------------------------------
if (-not $NoRestart) {
    if (Test-Path $deckExe) {
        Start-Process -FilePath $deckExe -WorkingDirectory (Split-Path $deckExe -Parent)
        Write-Host "Started $deckExe"
    } else {
        Write-Warning "Control Deck executable not found: $deckExe. Start it manually."
    }
}
