<#
.SYNOPSIS
    Validates Unity .meta file hygiene for every tracked file in the repo.

.DESCRIPTION
    Enforces three rules over `git ls-files`, so the check reflects what a
    fresh clone (the remote) contains rather than local working-tree state:

    1. ORPHAN META: every tracked `*.meta` must have its target tracked too.
       A target is present in the remote when it is a tracked file, or a
       directory that still contains tracked files (git does not track empty
       directories). Metas checked in without their data file produce
       missing-asset warnings for consumers and depend on locally generated
       files that are absent from the remote (e.g. gitignored build outputs).
    2. MISSING META: every tracked file or directory visible to Unity (no
       dot-prefixed path segment, no `~`-suffixed segment) must have a tracked
       sibling `.meta`, so Unity never auto-generates a stub GUID.
    3. GUIDS: every tracked `.meta` must declare exactly one 32-hex `guid:`,
       and GUIDs must be unique across the package.

    Messages use the [meta-lint] prefix.

.PARAMETER VerboseOutput
    Emit per-file OK status.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/lint-unity-meta.ps1
    pwsh -NoProfile -File tooling~/scripts/lint-unity-meta.ps1 -VerboseOutput
#>
Param(
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-ErrorMsg($msg) {
    Write-Host "[meta-lint] ERROR: $msg" -ForegroundColor Red
}

function Write-SuccessMsg($msg) {
    Write-Host "[meta-lint] OK: $msg" -ForegroundColor Green
}

if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
}

Push-Location $RepoRoot
try {
    $tracked = @(
        git -c core.quotepath=false ls-files 2>$null |
            ForEach-Object { $_.Replace('\', '/') }
    )
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "git ls-files failed; is $RepoRoot a git repository?"
        exit 1
    }
}
finally {
    Pop-Location
}

$trackedSet = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal
)
$trackedDirectories = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal
)
foreach ($file in $tracked) {
    [void]$trackedSet.Add($file)

    # A directory "exists in the remote" iff it contains tracked files (git
    # does not track empty directories), so collect every ancestor.
    $segments = $file.Split('/')
    for ($i = 1; $i -lt $segments.Length; $i++) {
        [void]$trackedDirectories.Add(($segments[0..($i - 1)] -join '/'))
    }
}

function Test-UnityIgnored([string]$path) {
    foreach ($segment in $path.Split('/')) {
        if ($segment.StartsWith('.') -or $segment.EndsWith('~')) {
            return $true
        }
    }

    return $false
}

$orphanErrors = 0
$missingErrors = 0
$guidErrors = 0
$guidsByPath = @{}

foreach ($file in $tracked) {
    if (-not $file.EndsWith('.meta', [System.StringComparison]::Ordinal)) {
        continue
    }

    $metaPath = Join-Path $RepoRoot $file
    try {
        $lines = [System.IO.File]::ReadAllLines($metaPath)
    }
    catch {
        Write-ErrorMsg "'$file' could not be read: $($_.Exception.Message)"
        $guidErrors++
        continue
    }

    $guidMatches = @(
        $lines |
            ForEach-Object {
                if ($_ -match '^guid:\s*([0-9a-fA-F]{32})\s*$') {
                    $Matches[1].ToLowerInvariant()
                }
            }
    )

    if ($guidMatches.Count -eq 0) {
        Write-ErrorMsg "'$file' has no parsable 32-hex 'guid:' line"
        $guidErrors++
        continue
    }

    if ($guidMatches.Count -gt 1) {
        Write-ErrorMsg "'$file' declares multiple guid: lines"
        $guidErrors++
        continue
    }

    $guid = $guidMatches[0]
    if ($guidsByPath.ContainsKey($guid)) {
        Write-ErrorMsg "duplicate guid '$guid' in '$file' and '$($guidsByPath[$guid])'"
        $guidErrors++
    }
    else {
        $guidsByPath[$guid] = $file
        if ($VerboseOutput) {
            Write-SuccessMsg "${file}: guid unique"
        }
    }

    # Legacy folder metas (historical Unity output) may omit
    # 'folderAsset: yes' and still work, so the validity rule is simply that
    # the target exists in the remote: a tracked file, or a tracked directory
    # (git does not track empty directories).
    $isFolderAsset = @($lines | Where-Object { $_ -match '^folderAsset:\s*yes\s*$' }).Count -gt 0
    $target = $file.Substring(0, $file.Length - '.meta'.Length)
    if (-not $trackedSet.Contains($target) -and -not $trackedDirectories.Contains($target)) {
        Write-ErrorMsg "orphan meta: '$file' has no tracked target '$target'"
        $orphanErrors++
    }
    elseif ($isFolderAsset -and $trackedSet.Contains($target)) {
        Write-ErrorMsg "folder meta '$file' targets a tracked file, not a directory"
        $orphanErrors++
    }
}

foreach ($file in $tracked) {
    if ($file.EndsWith('.meta', [System.StringComparison]::Ordinal)) {
        continue
    }

    if (Test-UnityIgnored $file) {
        continue
    }

    if (-not $trackedSet.Contains("$file.meta")) {
        Write-ErrorMsg "missing meta: '$file' is visible to Unity but '$file.meta' is not tracked"
        $missingErrors++
    }
    elseif ($VerboseOutput) {
        Write-SuccessMsg "${file}: meta present"
    }
}

foreach ($directory in $trackedDirectories) {
    if (Test-UnityIgnored $directory) {
        continue
    }

    if (-not $trackedSet.Contains("$directory.meta")) {
        Write-ErrorMsg "missing meta: directory '$directory' is visible to Unity but '$directory.meta' is not tracked"
        $missingErrors++
    }
    elseif ($VerboseOutput) {
        Write-SuccessMsg "${directory}: meta present"
    }
}

$totalChecked = $tracked.Count + $trackedDirectories.Count
Write-Host ""
Write-Host ("=" * 60)
Write-Host "[meta-lint] Summary: $totalChecked tracked files/directories checked (orphan metas: $orphanErrors, missing metas: $missingErrors, guid problems: $guidErrors)"

if ($orphanErrors -eq 0 -and $missingErrors -eq 0 -and $guidErrors -eq 0) {
    Write-Host "Unity meta hygiene passed" -ForegroundColor Green
    exit 0
}

Write-Host "Unity meta hygiene failed - see errors above" -ForegroundColor Red
exit 1
