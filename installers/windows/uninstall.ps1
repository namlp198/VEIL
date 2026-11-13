<#
.SYNOPSIS
  Removes VEIL: stops it, removes the start-with-Windows entry, shortcuts and files.

.PARAMETER Scope       Machine or User. Default: read from install.json next to this script, else Machine.
.PARAMETER RemoveData  Also delete the configuration, audit log and agent log. Without it your settings are kept so a
                       later reinstall picks them up.
.PARAMETER Quiet       No confirmation prompt.
.PARAMETER DryRun      Print what would be done and change nothing.
#>
[CmdletBinding()]
param(
    [ValidateSet('Machine', 'User')] [string]$Scope,
    [switch]$RemoveData,
    [switch]$Quiet,
    [switch]$DryRun,
    [switch]$Pause,
    [switch]$FromTemp,
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Do-Action([string]$description, [scriptblock]$action) {
    if ($DryRun) { Write-Host "    [dry-run] $description" -ForegroundColor DarkYellow; return }
    Write-Host "    $description"
    & $action
}

# Where is the installation? Only a folder that really contains the program counts as the install folder: running this
# script from the build package (or anywhere else) must never delete that folder. Otherwise ask the registry.
if (-not $InstallDir) {
    if (Test-Path (Join-Path $PSScriptRoot 'veil-agent.exe')) { $InstallDir = $PSScriptRoot }
    else {
        foreach ($k in 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VEIL', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VEIL') {
            $loc = (Get-ItemProperty -Path $k -Name InstallLocation -ErrorAction SilentlyContinue).InstallLocation
            if ($loc -and (Test-Path (Join-Path $loc 'veil-agent.exe'))) {
                $InstallDir = $loc
                if (-not $Scope) { $Scope = if ($k -like 'HKLM*') { 'Machine' } else { 'User' } }
                break
            }
        }
    }
}
if (-not $InstallDir -or -not (Test-Path (Join-Path $InstallDir 'veil-agent.exe'))) {
    Write-Host 'VEIL does not appear to be installed (no install location found in the registry).' -ForegroundColor Yellow
    if ($Pause) { [void](Read-Host 'Press Enter to close') }
    exit 0
}
if (-not $Scope) {
    $marker = Join-Path $InstallDir 'install.json'
    $Scope = 'Machine'
    if (Test-Path $marker) { try { if ((Get-Content $marker -Raw | ConvertFrom-Json).Mode -eq 'User') { $Scope = 'User' } } catch { } }
}

if ($Scope -eq 'Machine' -and -not (Test-Admin) -and -not $DryRun) {
    Write-Step 'Administrator rights are needed to remove a machine-wide install; asking Windows for them...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Scope', 'Machine', '-Pause', '-Quiet', '-InstallDir', "`"$InstallDir`"")
    if ($RemoveData) { $argList += '-RemoveData' }
    try { $p = Start-Process powershell.exe -ArgumentList $argList -Verb RunAs -Wait -PassThru; exit $p.ExitCode }
    catch { Write-Host 'The elevation request was cancelled or failed.' -ForegroundColor Red; exit 5 }
}

# Re-run from a temp copy so the install folder (which holds this script) can be deleted.
if (-not $FromTemp -and $PSScriptRoot -and ($PSScriptRoot -eq $InstallDir) -and -not $DryRun) {
    $copy = Join-Path $env:TEMP ("veil-uninstall-{0}.ps1" -f ([guid]::NewGuid().ToString('N').Substring(0, 8)))
    Copy-Item $PSCommandPath $copy -Force
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$copy`"", '-Scope', $Scope, '-FromTemp', '-InstallDir', "`"$InstallDir`"")
    foreach ($f in 'RemoveData', 'Quiet', 'Pause') { if ($PSBoundParameters.ContainsKey($f)) { $argList += "-$f" } }
    $p = Start-Process powershell.exe -ArgumentList $argList -Wait -PassThru -NoNewWindow
    Remove-Item $copy -Force -ErrorAction SilentlyContinue
    exit $p.ExitCode
}

$exitCode = 0
try {
    $runKey = if ($Scope -eq 'Machine') { 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
    $uninstallKey = if ($Scope -eq 'Machine') { 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VEIL' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VEIL' }
    $startMenu = if ($Scope -eq 'Machine') { Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\VEIL' } else { Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VEIL' }
    $configDir = if ($Scope -eq 'Machine') { Join-Path $env:ProgramData 'VEIL' } else { Join-Path $env:LOCALAPPDATA 'VEIL\config' }
    $userData = Join-Path $env:LOCALAPPDATA 'VEIL'

    Write-Step "Removing VEIL ($Scope scope)$(if ($DryRun) { ' - DRY RUN' })"
    Write-Host "    Install folder: $InstallDir"
    Write-Host "    Your settings : $(if ($RemoveData) { 'will be deleted' } else { 'kept (use -RemoveData to delete them)' })"
    if (-not $Quiet -and -not $DryRun) {
        $answer = Read-Host 'Continue? (y/N)'
        if ($answer -notmatch '^(y|yes)$') { Write-Host 'Cancelled.'; exit 0 }
    }

    Write-Step 'Stopping VEIL'
    $running = @(Get-Process -Name 'veil-agent', 'veil' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" })
    if ($running.Count -eq 0) { Write-Host '    nothing running' }
    foreach ($proc in $running) {
        Do-Action "stop $($proc.ProcessName) (pid $($proc.Id))" {
            try { [void]$proc.CloseMainWindow() } catch { }
            if (-not $proc.WaitForExit(3000)) { Stop-Process -Id $proc.Id -Force }
        }
    }

    Write-Step 'Start with Windows, shortcuts, Programs and Features entry'
    Do-Action "remove $runKey\VEIL" { Remove-ItemProperty -Path $runKey -Name 'VEIL' -ErrorAction SilentlyContinue }
    Do-Action "remove '$startMenu'" { if (Test-Path $startMenu) { Remove-Item $startMenu -Recurse -Force } }
    Do-Action "remove '$uninstallKey'" { if (Test-Path $uninstallKey) { Remove-Item $uninstallKey -Recurse -Force } }

    Write-Step 'Removing files'
    Do-Action "delete '$InstallDir'" {
        if (Test-Path $InstallDir) {
            $deleted = $false
            for ($i = 0; $i -lt 6 -and -not $deleted; $i++) {
                try { Remove-Item $InstallDir -Recurse -Force -ErrorAction Stop; $deleted = $true } catch { Start-Sleep -Milliseconds 700 }
            }
            if (-not $deleted) { throw "Could not delete '$InstallDir' (a file is still in use). Close VEIL and run the uninstaller again." }
        }
    }
    if ($RemoveData) {
        Do-Action "delete configuration '$configDir' and user data '$userData'" {
            foreach ($d in $configDir, $userData) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } }
        }
    }
    Write-Host ''
    Write-Host 'VEIL was removed.' -ForegroundColor Green
} catch {
    $exitCode = 1
    Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
}
if ($Pause) { Write-Host ''; [void](Read-Host 'Press Enter to close') }
exit $exitCode
