# Builds the Thunderstore upload zip for Increased Enemy Count.
#
# Usage (from anywhere):   powershell -ExecutionPolicy Bypass -File thunderstore\pack.ps1
#
# For a new release:
#   1. Bump "version_number" in thunderstore\manifest.json
#   2. Bump VERSION in src\Plugin.cs (and AssemblyInfo.cs) to the same number
#   3. Add an entry to thunderstore\CHANGELOG.md
#   4. Run this script, then upload thunderstore\dist\<name>-<version>.zip (name from manifest.json)

$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Split-Path -Parent $here

$manifest = Get-Content (Join-Path $here 'manifest.json') -Raw | ConvertFrom-Json
$version  = $manifest.version_number

# Make sure the plugin reports the same version as the package.
$plugin = Get-Content (Join-Path $project 'src\Plugin.cs') -Raw
if ($plugin -notmatch 'VERSION\s*=\s*"([^"]+)"') { throw 'Could not find VERSION in src\Plugin.cs' }
if ($Matches[1] -ne $version) { throw "manifest.json says $version but Plugin.cs says $($Matches[1]). Make them match first." }

# Build.
Push-Location $project
try { dotnet build -c Release | Out-Host; if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }
finally { Pop-Location }

$dll = Join-Path $project 'bin\Release\IncreasedEnemyCount.dll'
if (-not (Test-Path $dll)) { throw "Built DLL not found at $dll" }

# Check the icon.
Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile((Join-Path $here 'icon.png'))
$size = "$($img.Width)x$($img.Height)"; $img.Dispose()
if ($size -ne '256x256') { throw "icon.png must be 256x256 (it is $size)" }

# Stage and zip (all files at the top level of the zip).
$stage = Join-Path $here 'dist\_stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null
Copy-Item $dll, (Join-Path $here 'manifest.json'), (Join-Path $here 'README.md'), (Join-Path $here 'CHANGELOG.md'), (Join-Path $here 'icon.png') $stage

$zip = Join-Path $here "dist\$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host "Created $zip" -ForegroundColor Green
