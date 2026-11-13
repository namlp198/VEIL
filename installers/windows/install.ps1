<#
.SYNOPSIS
  Installs VEIL, registers it to start with Windows, and starts it right away.

.DESCRIPTION
  Two scopes:

    Machine (default)  Program Files + HKLM Run (all users) + protected configuration in ProgramData.
                       Needs administrator rights (the script asks for them). The Super Admin is set up here, as the
                       security design requires an OS-administrator-authorized setup.
    User               Trial install for the current user only: %LOCALAPPDATA%\Programs\VEIL + HKCU Run, no administrator
                       rights. The configuration lives in the user profile and is NOT protected against that user.

  After installing, the windowless agent (veil-agent.exe run) is started for the signed-in user. It registers the global
  hotkeys and a tray icon and does NOT cover the screen: Focus Mode starts only when you press Ctrl+Alt+V.

.PARAMETER Scope               Machine or User (see above).
.PARAMETER InstallDir          Override the install folder.
.PARAMETER Source              Folder holding the built files (default: .\app next to this script).
.PARAMETER SuperAdminAccount   Account to designate as Super Admin (DOMAIN\user or SID). Default: the user who ran the setup.
.PARAMETER NoAutoStart         Do not register start-with-Windows.
.PARAMETER NoLaunch            Do not start VEIL after installing.
.PARAMETER NoShortcuts         Do not create Start Menu shortcuts.
.PARAMETER DryRun              Print what would be done and change nothing.
.PARAMETER Pause               Keep the window open at the end (used when the script re-launches itself elevated).
.PARAMETER OriginalUser        Internal: the user who started the setup before elevation.
#>
[CmdletBinding()]
param(
    [ValidateSet('Machine', 'User')] [string]$Scope = 'Machine',
    [string]$InstallDir,
    [string]$Source,
    [string]$SuperAdminAccount,
    [switch]$NoAutoStart,
    [switch]$NoLaunch,
    [switch]$NoShortcuts,
    [switch]$DryRun,
    [switch]$Pause,
    [string]$OriginalUser
)

$ErrorActionPreference = 'Stop'
# (PowerShell 5.1 has no $PSScriptRoot inside param defaults, so defaults that need it are set here.)
if (-not $Source) { $Source = Join-Path $PSScriptRoot 'app' }
$ProductName = 'VEIL'
$UninstallKeyName = 'VEIL'

function Test-Admin {
    ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Info($text) { Write-Host "    $text" }
function Do-Action([string]$description, [scriptblock]$action) {
    if ($DryRun) { Write-Host "    [dry-run] $description" -ForegroundColor DarkYellow; return }
    Write-Info $description
    & $action
}

# ---- elevation for the machine scope --------------------------------------------------------------------------------
if ($Scope -eq 'Machine' -and -not (Test-Admin) -and -not $DryRun) {
    Write-Step 'Administrator rights are needed for a machine-wide install; asking Windows for them...'
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Scope', 'Machine', '-Pause',
                 '-OriginalUser', "`"$env:USERDOMAIN\$env:USERNAME`"", '-Source', "`"$Source`"")
    if ($InstallDir) { $argList += @('-InstallDir', "`"$InstallDir`"") }
    if ($SuperAdminAccount) { $argList += @('-SuperAdminAccount', "`"$SuperAdminAccount`"") }
    foreach ($flag in 'NoAutoStart', 'NoLaunch', 'NoShortcuts') { if ($PSBoundParameters.ContainsKey($flag)) { $argList += "-$flag" } }
    try {
        $p = Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -Wait -PassThru
        exit $p.ExitCode
    } catch {
        Write-Host 'The elevation request was cancelled or failed. Use -Scope User for a trial install without administrator rights.' -ForegroundColor Red
        exit 5
    }
}

$logPath = Join-Path $env:TEMP ("VEIL-install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
try { Start-Transcript -Path $logPath -Force | Out-Null } catch { }
$exitCode = 0
try {
    # ---- preflight ----------------------------------------------------------------------------------------------------
    Write-Step "VEIL setup ($Scope scope)$(if ($DryRun) { ' - DRY RUN' })"
    if (-not (Test-Path (Join-Path $Source 'veil.exe')) -or -not (Test-Path (Join-Path $Source 'veil-agent.exe'))) {
        throw "Build output not found in '$Source' (veil.exe and veil-agent.exe are required). Run build.ps1 first."
    }
    if ([Environment]::OSVersion.Version.Major -lt 10) { throw 'VEIL needs Windows 10 or 11.' }
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'VEIL needs a 64-bit Windows.' }

    $setupUser = if ($OriginalUser) { $OriginalUser } else { "$env:USERDOMAIN\$env:USERNAME" }
    if (-not $InstallDir) {
        $InstallDir = if ($Scope -eq 'Machine') { Join-Path $env:ProgramFiles $ProductName } else { Join-Path $env:LOCALAPPDATA "Programs\$ProductName" }
    }
    $version = '0.0.0'
    $versionFile = Join-Path $Source 'VERSION.txt'
    if (Test-Path $versionFile) { $version = (Get-Content $versionFile -Raw).Trim() }
    $configDir = if ($Scope -eq 'Machine') { Join-Path $env:ProgramData $ProductName } else { Join-Path $env:LOCALAPPDATA "$ProductName\config" }
    $account = if ($SuperAdminAccount) { $SuperAdminAccount } else { $setupUser }
    $agentExe = Join-Path $InstallDir 'veil-agent.exe'
    $cliExe = Join-Path $InstallDir 'veil.exe'
    $runCommand = "`"$agentExe`" run"
    $runKey = if ($Scope -eq 'Machine') { 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run' } else { 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' }
    $uninstallKey = if ($Scope -eq 'Machine') { "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$UninstallKeyName" } else { "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$UninstallKeyName" }
    $startMenu = if ($Scope -eq 'Machine') { Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$ProductName" } else { Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$ProductName" }

    Write-Info "Version        : $version"
    Write-Info "Install folder : $InstallDir"
    Write-Info "Configuration  : $configDir$(if ($Scope -eq 'Machine') { '  (protected: only administrators can change it)' } else { '  (trial: user-writable, not protected)' })"
    Write-Info "Super Admin    : $account"
    Write-Info "Start with Windows: $(if ($NoAutoStart) { 'no' } else { $runKey })"

    # ---- stop a running copy (upgrade / reinstall) -----------------------------------------------------------------------
    Write-Step 'Stopping any running VEIL'
    $running = @(Get-Process -Name 'veil-agent', 'veil' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\*" -or $_.Path -like "$Source\*" -or $_.Path -like '*\VEIL\*' })
    if ($running.Count -eq 0) { Write-Info 'nothing running' }
    foreach ($proc in $running) {
        Do-Action "stop $($proc.ProcessName) (pid $($proc.Id))" {
            try { [void]$proc.CloseMainWindow() } catch { }
            if (-not $proc.WaitForExit(3000)) { Stop-Process -Id $proc.Id -Force }
        }
    }

    # ---- copy files -------------------------------------------------------------------------------------------------------
    Write-Step 'Copying files'
    Do-Action "mirror '$Source' -> '$InstallDir'" {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
        $null = & robocopy.exe "$Source" "$InstallDir" /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:1 /XF install.json
        if ($LASTEXITCODE -ge 8) { throw "File copy failed (robocopy exit code $LASTEXITCODE)." }
        $global:LASTEXITCODE = 0
        Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $InstallDir 'uninstall.ps1') -Force -ErrorAction SilentlyContinue
        $marker = [ordered]@{ Mode = $Scope; ConfigDir = $(if ($Scope -eq 'User') { $configDir } else { $null }); Version = $version
                              InstalledAt = (Get-Date).ToString('o'); InstalledBy = $setupUser }
        $marker | ConvertTo-Json | Set-Content -Path (Join-Path $InstallDir 'install.json') -Encoding UTF8
    }

    # ---- configuration and Super Admin -------------------------------------------------------------------------------------
    Write-Step 'Configuration and Super Admin'
    if ($Scope -eq 'Machine') {
        Do-Action "provision '$account' as the Super Admin through the OS-administrator setup path" {
            $out = & $cliExe admin provision --account $account 2>&1
            $text = ($out | Out-String).Trim()
            if ($LASTEXITCODE -eq 0) { Write-Info $text }
            elseif ($text -like '*already configured*') { Write-Info 'a Super Admin is already configured; kept unchanged' }
            else { throw "Super Admin setup failed: $text" }
            $global:LASTEXITCODE = 0
        }
    } else {
        Do-Action "write a trial configuration naming '$account' as the Super Admin (only if none exists)" {
            New-Item -ItemType Directory -Force -Path $configDir | Out-Null
            $cfgFile = Join-Path $configDir 'config.json'
            if (Test-Path $cfgFile) { Write-Info 'configuration already exists; kept unchanged' }
            else {
                $sid = if ($account -like 'S-1-*') { $account } else { ([Security.Principal.NTAccount]$account).Translate([Security.Principal.SecurityIdentifier]).Value }
                $cfg = [ordered]@{ SchemaVersion = 1; SuperAdmin = [ordered]@{ Platform = 'Windows'; Principal = $sid; DisplayName = $account } }
                [IO.File]::WriteAllText($cfgFile, ($cfg | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
            }
        }
    }

    # ---- start with Windows --------------------------------------------------------------------------------------------------
    Write-Step 'Start with Windows'
    if ($NoAutoStart) {
        Write-Info 'skipped (-NoAutoStart)'
    } else {
        Do-Action "set $runKey\$ProductName = $runCommand" {
            Set-ItemProperty -Path $runKey -Name $ProductName -Value $runCommand -Type String
        }
    }

    # ---- shortcuts and uninstall entry ---------------------------------------------------------------------------------------
    $mainLink = Join-Path $startMenu 'VEIL.lnk'
    if ($NoShortcuts) {
        Write-Step 'Shortcuts'; Write-Info 'skipped (-NoShortcuts)'
    } else {
        Write-Step 'Start Menu shortcuts'
        Do-Action "create shortcuts in '$startMenu'" {
            New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
            $shell = New-Object -ComObject WScript.Shell
            function New-Link($file, $target, $arguments, $description, [switch]$RunAsAdmin) {
                $l = $shell.CreateShortcut($file)
                $l.TargetPath = $target; $l.Arguments = $arguments; $l.WorkingDirectory = $InstallDir
                $l.IconLocation = "$agentExe,0"; $l.Description = $description; $l.Save()
                if ($RunAsAdmin) { $bytes = [IO.File]::ReadAllBytes($file); $bytes[0x15] = $bytes[0x15] -bor 0x20; [IO.File]::WriteAllBytes($file, $bytes) }
            }
            New-Link $mainLink $agentExe 'run' 'Start VEIL (hotkeys and tray icon)'
            New-Link (Join-Path $startMenu 'VEIL Settings.lnk') $agentExe 'settings' 'Open VEIL settings'
            if ($Scope -eq 'Machine') {
                New-Link (Join-Path $startMenu 'VEIL Settings (administrator).lnk') $agentExe 'settings' 'Open VEIL settings with administrator rights (needed to save machine-wide changes)' -RunAsAdmin
            }
            $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
            New-Link (Join-Path $startMenu 'Uninstall VEIL.lnk') $powershell "-NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`" -Scope $Scope" 'Remove VEIL'
        }
    }

    Write-Step 'Programs and Features entry'
    Do-Action "register '$uninstallKey'" {
        New-Item -Path $uninstallKey -Force | Out-Null
        $sizeKb = [int]((Get-ChildItem $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1KB)
        $props = @{
            DisplayName = 'VEIL - Controlled Desktop Environment'; DisplayVersion = $version; Publisher = 'VEIL'
            InstallLocation = $InstallDir; DisplayIcon = "$agentExe,0"; EstimatedSize = $sizeKb
            UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`" -Scope $Scope"
        }
        foreach ($k in $props.Keys) { Set-ItemProperty -Path $uninstallKey -Name $k -Value $props[$k] }
        Set-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -Type DWord
        Set-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -Type DWord
    }

    # ---- start it now -------------------------------------------------------------------------------------------------------
    if ($NoLaunch) {
        Write-Step 'Launch'; Write-Info 'skipped (-NoLaunch)'
    } else {
        Write-Step 'Starting VEIL now'
        $started = Get-Date
        Do-Action 'launch the agent for the signed-in user (not elevated)' {
            if ((Test-Admin) -and -not $NoShortcuts -and (Test-Path $mainLink)) {
                # explorer.exe starts the shortcut with the user's normal rights; an elevated agent would break hotkeys and focus.
                Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$mainLink`""
            } elseif (Test-Admin) {
                Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$agentExe`""
            } else {
                Start-Process -FilePath $agentExe -ArgumentList 'run'
            }
        }
        if (-not $DryRun) {
            $ok = $false
            for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
                Start-Sleep -Milliseconds 500
                $ok = [bool](Get-Process -Name 'veil-agent' -ErrorAction SilentlyContinue)
            }
            if ($ok) { Write-Host '    VEIL is running (look for the VEIL icon in the notification area; it may be under the ^ arrow).' -ForegroundColor Green }
            else { Write-Warning 'The agent did not appear to start. Check %LOCALAPPDATA%\VEIL\logs\agent.log' }
        }
    }

    Write-Host ''
    Write-Host 'Done.' -ForegroundColor Green
    Write-Host '  Focus On  : Ctrl+Alt+V      Focus Off : Ctrl+Alt+U      Emergency Recovery : Ctrl+Alt+Esc'
    Write-Host '  Selector  : Ctrl+Alt+L      Hide All  : Ctrl+Alt+H      Next / Previous window : Ctrl+Alt+Right / Left'
    Write-Host '  VEIL does not cover your screen until you press Ctrl+Alt+V. Overlay Focus is not a secure lock.'
    Write-Host "  Install log: $logPath"
} catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "Setup failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Install log: $logPath"
} finally {
    try { Stop-Transcript | Out-Null } catch { }
}
if ($Pause) { Write-Host ''; [void](Read-Host 'Press Enter to close') }
exit $exitCode
