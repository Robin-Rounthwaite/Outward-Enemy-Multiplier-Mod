# Prepares a new Thunderstore release of More Enemies in one step.
#
# Usage (from anywhere):
#   powershell -ExecutionPolicy Bypass -File thunderstore\release.ps1 -Version 1.2.4 -Note "Fixed something."
#
#   -Version  The new version number (Major.Minor.Patch). Must be higher than the current one.
#   -Note     Optional. Changelog line(s) for this version. Use ";" to separate several lines.
#             If left out, CHANGELOG.md must already contain a "## <version>" entry you wrote yourself.
#
# What it does:
#   1. Checks the version number and the changelog entry (nothing is changed if a check fails).
#   2. Sets the version in thunderstore\manifest.json, src\Plugin.cs and src\AssemblyInfo.cs.
#   3. Adds the changelog entry (if -Note was given).
#   4. Runs pack.ps1: builds the mod and writes thunderstore\dist\<name>-<version>.zip.
#
# Reminder: if this release changes which enemies get copied, or how (IDs, UIDs, order),
# also bump PROTOCOL in src\NetSync.cs so co-op players on mixed versions are warned.

param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Note
)

$ErrorActionPreference = 'Stop'
$here    = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Split-Path -Parent $here
$utf8    = New-Object System.Text.UTF8Encoding($false)   # UTF-8 without BOM (a BOM can break manifest.json)

function Read-Text($path)        { [IO.File]::ReadAllText($path) }
function Write-Text($path, $txt) { [IO.File]::WriteAllText($path, $txt, $utf8) }

$manifestPath  = Join-Path $here 'manifest.json'
$changelogPath = Join-Path $here 'CHANGELOG.md'
$pluginPath    = Join-Path $project 'src\Plugin.cs'
$asmInfoPath   = Join-Path $project 'src\AssemblyInfo.cs'

# ---- 1. Checks (before changing anything) ---------------------------------------------------

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 1.2.4 (Major.Minor.Patch). Got '$Version'."
}

$manifestText = Read-Text $manifestPath
if ($manifestText -notmatch '"version_number"\s*:\s*"([^"]+)"') { throw 'Could not find version_number in manifest.json' }
$current = $Matches[1]
if ([version]$Version -le [version]$current) {
    throw "New version $Version must be higher than the current version $current (Thunderstore rejects re-used versions)."
}

$changelog = Read-Text $changelogPath
$nl = if ($changelog.Contains("`r`n")) { "`r`n" } else { "`n" }
$hasEntry = $changelog -match ('(?m)^##\s+' + [regex]::Escape($Version) + '\s*$')
if (-not $hasEntry -and [string]::IsNullOrWhiteSpace($Note)) {
    throw "CHANGELOG.md has no '## $Version' entry. Add one, or run again with -Note `"What changed.`""
}
if ($hasEntry -and -not [string]::IsNullOrWhiteSpace($Note)) {
    Write-Host "CHANGELOG.md already has a '## $Version' entry; ignoring -Note." -ForegroundColor Yellow
}

# ---- 2. Set the version everywhere ----------------------------------------------------------

Write-Text $manifestPath ($manifestText -replace '"version_number"\s*:\s*"[^"]+"', "`"version_number`": `"$Version`"")

$plugin = Read-Text $pluginPath
if ($plugin -notmatch 'VERSION\s*=\s*"[^"]+"') { throw 'Could not find VERSION in src\Plugin.cs' }
Write-Text $pluginPath ($plugin -replace 'VERSION\s*=\s*"[^"]+"', "VERSION = `"$Version`"")

$asm = Read-Text $asmInfoPath
Write-Text $asmInfoPath ($asm -replace '\d+\.\d+\.\d+\.\d+', "$Version.0")

Write-Host "Version set to $Version (was $current)." -ForegroundColor Cyan

# ---- 3. Changelog ---------------------------------------------------------------------------

if (-not $hasEntry) {
    $lines = ($Note -split ';') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object { "- $_" }
    $entry = "## $Version$nl" + ($lines -join $nl) + "$nl$nl"
    # Insert after the "# Changelog" title (and the blank line after it), or at the top.
    $m = [regex]::Match($changelog, '(?m)^# [^\r\n]*\r?\n(\r?\n)?')
    if ($m.Success) { $changelog = $changelog.Insert($m.Index + $m.Length, $entry) }
    else            { $changelog = $entry + $changelog }
    Write-Text $changelogPath $changelog
    Write-Host "Added changelog entry for $Version." -ForegroundColor Cyan
}

# ---- 4. Build and package -------------------------------------------------------------------

& (Join-Path $here 'pack.ps1')
if (-not $?) { throw 'pack.ps1 failed.' }

Write-Host ''
Write-Host "Release $Version is ready. Upload the zip in thunderstore\dist on Thunderstore." -ForegroundColor Green
Write-Host 'Remember to commit the changed files to git.' -ForegroundColor Green
