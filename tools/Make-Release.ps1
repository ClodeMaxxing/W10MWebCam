# Packs the last build (out\W10MWebCam_*_ARM_Test) into release\W10MWebCam_<version>_ARM.zip with its three runtime
# packages, the test certificate and INSTALL.txt, and writes release\SHA256SUMS.txt.
#   powershell -ExecutionPolicy Bypass -File tools\Make-Release.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$appx = Get-ChildItem (Join-Path $root 'out') -Recurse -Filter 'W10MWebCam_*_ARM.appx' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $appx) { throw 'No build found in out\ - run tools\Build-And-Deploy.cmd first' }
$ver = ($appx.BaseName -replace '^W10MWebCam_', '' -replace '_ARM$', '')
$short = ($ver -split '\.')[0..1] -join '.'
$dep = Join-Path $appx.DirectoryName 'Dependencies\arm'
$rel = Join-Path $root 'release'
$stage = Join-Path $rel "W10MWebCam_${short}_ARM"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage 'Dependencies') | Out-Null
Copy-Item $appx.FullName $stage
Get-ChildItem $dep -Filter *.appx | Copy-Item -Destination (Join-Path $stage 'Dependencies')
Copy-Item (Join-Path $root 'W10MWebCam.cer') $stage
Copy-Item (Join-Path $PSScriptRoot 'INSTALL.txt') $stage
$zip = Join-Path $rel "W10MWebCam_${short}_ARM.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
$sums = Get-ChildItem $rel -Filter *.zip | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower() + '  ' + $_.Name }
$sums | Out-File (Join-Path $rel 'SHA256SUMS.txt') -Encoding ascii
"Release: $zip (package $ver)"
$sums
