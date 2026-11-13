<#
.SYNOPSIS
  Builds VEIL and assembles an installable package in dist\.

.DESCRIPTION
  1. Runs the unit tests (skip with -SkipTests).
  2. Publishes the console tool (veil.exe) and the windowless agent (veil-agent.exe) self-contained for win-x64, so the
     target machine needs no .NET installed (use -FrameworkDependent for a small package that needs the .NET 10 runtime).
  3. Merges both into one app folder, adds the install scripts, writes SHA256SUMS.txt and a zip.

  Result:  dist\VEIL-<version>-win-x64\     (run Install-VEIL.cmd or Install-VEIL-Trial.cmd from here)
           dist\VEIL-<version>-win-x64.zip  (the same, for copying to another PC)

.PARAMETER Configuration        Build configuration (default Release).
.PARAMETER Runtime              Runtime identifier (default win-x64).
.PARAMETER SkipTests            Do not run the unit tests first.
.PARAMETER FrameworkDependent   Do not bundle the .NET runtime.
.PARAMETER NoZip                Do not create the zip.
.PARAMETER Clean                Delete dist\ before building.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests,
    [switch]$FrameworkDependent,
    [switch]$NoZip,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$dist = Join-Path $repo 'dist'
$project = Join-Path $repo 'src\VEIL.App\VEIL.App.csproj'

function Write-Step($t) { Write-Host "==> $t" -ForegroundColor Cyan }
function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# ---- preflight ---------------------------------------------------------------------------------------------------------
Write-Step 'Preflight'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'The .NET SDK was not found. Install the .NET 10 SDK (see global.json).' }
$sdk = (& dotnet --version).Trim()
Write-Host "    .NET SDK $sdk"
$propsText = Get-Content (Join-Path $repo 'Directory.Build.props') -Raw
$version = if ($propsText -match '<Version>([^<]+)</Version>') { $Matches[1] } else { '0.0.0' }
$name = "VEIL-$version-$Runtime"
$package = Join-Path $dist $name
$stage = Join-Path $dist '_stage'
Write-Host "    version $version, runtime $Runtime, $(if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' })"

if ($Clean -and (Test-Path $dist)) { Write-Step 'Cleaning dist'; Remove-Item $dist -Recurse -Force }
foreach ($d in $stage, $package) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force -Path $stage, $package | Out-Null

# ---- tests --------------------------------------------------------------------------------------------------------------
if ($SkipTests) {
    Write-Step 'Unit tests skipped'
} else {
    Write-Step 'Unit tests'
    Invoke-Dotnet @('test', (Join-Path $repo 'tests\VEIL.UnitTests\VEIL.UnitTests.csproj'), '-c', $Configuration, '-nologo', '-v', 'q')
}

# ---- publish ------------------------------------------------------------------------------------------------------------
$common = @('-c', $Configuration, '-r', $Runtime, '--self-contained', $(if ($FrameworkDependent) { 'false' } else { 'true' }),
            '-p:DebugType=none', '-p:DebugSymbols=false', '-nologo', '-v', 'q')

# The two flavours differ only in AssemblyName/OutputType, so start each from a clean App project (publish has no --no-incremental).
function Reset-AppBuild { foreach ($d in 'obj', 'bin') { $p = Join-Path $repo "src\VEIL.App\$d"; if (Test-Path $p) { Remove-Item $p -Recurse -Force } } }

Write-Step 'Publishing veil.exe (console tool)'
Reset-AppBuild
Invoke-Dotnet (@('publish', $project, '-o', (Join-Path $stage 'cli')) + $common)

Write-Step 'Publishing veil-agent.exe (windowless agent that starts at logon)'
Reset-AppBuild
Invoke-Dotnet (@('publish', $project, '-o', (Join-Path $stage 'gui'), '-p:VeilGui=true') + $common)

# ---- assemble -----------------------------------------------------------------------------------------------------------
Write-Step 'Assembling package'
$app = Join-Path $package 'app'
New-Item -ItemType Directory -Force -Path $app | Out-Null
Copy-Item (Join-Path $stage 'cli\*') $app -Recurse -Force
Copy-Item (Join-Path $stage 'gui\veil-agent.*') $app -Force      # agent exe, dll, deps and runtimeconfig; everything else is shared
foreach ($required in 'veil.exe', 'veil-agent.exe', 'veil-agent.dll', 'veil-agent.runtimeconfig.json') {
    if (-not (Test-Path (Join-Path $app $required))) { throw "Package is incomplete: $required is missing." }
}
Set-Content -Path (Join-Path $app 'VERSION.txt') -Value $version -Encoding ASCII

foreach ($f in 'install.ps1', 'uninstall.ps1', 'Install-VEIL.cmd', 'Install-VEIL-Trial.cmd', 'Uninstall-VEIL.cmd') {
    Copy-Item (Join-Path $PSScriptRoot $f) $package -Force
}
Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $package 'README.txt') -Force

# ---- smoke test the built exe (self-contained: it must run without a system .NET) -------------------------------------------
Write-Step 'Smoke test'
$help = & (Join-Path $app 'veil.exe') help 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $help -notlike '*veil run*') { throw "veil.exe did not start correctly:`n$help" }
Write-Host '    veil.exe starts and lists its commands'

# ---- checksums and zip --------------------------------------------------------------------------------------------------
Write-Step 'Checksums'
$sums = Get-ChildItem $package -Recurse -File | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($package.Length + 1).Replace('\', '/')
}
Set-Content -Path (Join-Path $package 'SHA256SUMS.txt') -Value $sums -Encoding ASCII
Write-Host "    $($sums.Count) files hashed"

if (-not $NoZip) {
    Write-Step 'Zip'
    $zip = Join-Path $dist "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path $package -DestinationPath $zip -CompressionLevel Optimal
    $zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -Path "$zip.sha256" -Value "$zipHash  $name.zip" -Encoding ASCII
    Write-Host ("    {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
}
Remove-Item $stage -Recurse -Force
Reset-AppBuild   # leave the dev tree buildable as veil.exe again

$sizeMb = ((Get-ChildItem $package -Recurse -File | Measure-Object Length -Sum).Sum / 1MB)
Write-Host ''
Write-Host ("Built {0}  ({1:N0} MB, {2} files)" -f $package, $sizeMb, (Get-ChildItem $package -Recurse -File).Count) -ForegroundColor Green
Write-Host "Install:  $package\Install-VEIL-Trial.cmd   (no administrator rights, this user only)"
Write-Host "          $package\Install-VEIL.cmd         (machine-wide, asks for administrator rights)"
