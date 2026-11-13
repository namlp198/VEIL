<#
.SYNOPSIS
  Generates the VEIL application icon (assets/icons/veil.ico) and the tray image from the design tokens.
.DESCRIPTION
  A violet HUD ring with a lilac core and a cyan tick, transparent background (readable on light and dark taskbars).
  Colours come from assets/theme/veil-tokens.json. Output is committed, so this only needs re-running when the tokens change.
#>
[CmdletBinding()]
param([string]$RepoRoot)

$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
Add-Type -AssemblyName System.Drawing

$tokens = Get-Content (Join-Path $RepoRoot 'assets\theme\veil-tokens.json') -Raw | ConvertFrom-Json
function Tok($name) { [Drawing.ColorTranslator]::FromHtml($tokens.colors.$name) }
$ring = Tok 'accent.primaryHover'; $core = Tok 'accent.lilac'; $tick = Tok 'accent.cyan'; $glow = Tok 'accent.glow'

function New-VeilBitmap([int]$size) {
  $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([Drawing.Color]::Transparent)
  $c = $size / 2.0
  $stroke = [Math]::Max(1.6, $size * 0.11)
  $r = $size * 0.36
  # soft glow
  foreach ($i in 3, 2, 1) {
    $pen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb([int](38 / $i), $glow)), ($stroke + $i * $size * 0.04)
    $g.DrawEllipse($pen, $c - $r, $c - $r, 2 * $r, 2 * $r); $pen.Dispose()
  }
  $pen = New-Object Drawing.Pen $ring, $stroke
  $g.DrawEllipse($pen, $c - $r, $c - $r, 2 * $r, 2 * $r); $pen.Dispose()
  $dot = $size * 0.13
  $brush = New-Object Drawing.SolidBrush $core
  $g.FillEllipse($brush, $c - $dot, $c - $dot, 2 * $dot, 2 * $dot); $brush.Dispose()
  if ($size -ge 24) {   # cyan top tick, dropped at 16 px where it would only add noise
    $pen = New-Object Drawing.Pen $tick, ([Math]::Max(1.5, $size * 0.07))
    $g.DrawLine($pen, $c, $size * 0.02, $c, $size * 0.16); $pen.Dispose()
  }
  $g.Dispose()
  $bmp
}

function Get-PngBytes($bmp) { $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); , ([byte[]]$ms.ToArray()) }

$out = Join-Path $RepoRoot 'assets\icons'
New-Item -ItemType Directory -Force $out | Out-Null

$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$images = foreach ($s in $sizes) { $b = New-VeilBitmap $s; [pscustomobject]@{ Size = $s; Bytes = [byte[]](Get-PngBytes $b) }; $b.Dispose() }

# ICO container with PNG-compressed images (supported since Windows Vista)
$ms = New-Object IO.MemoryStream; $w = New-Object IO.BinaryWriter $ms
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($i in $images) {
  $dim = if ($i.Size -ge 256) { 0 } else { $i.Size }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$i.Bytes.Length); $w.Write([uint32]$offset)
  $offset += $i.Bytes.Length
}
foreach ($i in $images) { $w.Write([byte[]]$i.Bytes) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'veil.ico'), $ms.ToArray())

[IO.File]::WriteAllBytes((Join-Path $out 'veil-tray.png'), ($images | Where-Object Size -eq 64).Bytes)
[IO.File]::WriteAllBytes((Join-Path $out 'veil-256.png'), ($images | Where-Object Size -eq 256).Bytes)
Write-Host "Wrote veil.ico ($($images.Count) sizes), veil-tray.png, veil-256.png to $out"
