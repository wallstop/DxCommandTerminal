<#
.SYNOPSIS
    Validates LLM file sizes against the repo's line limits.

.DESCRIPTION
    Checks all authored files under .llm/ (context.md, skills/**, references,
    code samples - any .md/.cs/.sh text file) against size thresholds:
    - >300 lines: ERROR (MUST split/reduce - hard limit)
    - 270-300 lines: CRITICAL WARNING (always shown, near limit)
    - <=269 lines: OK

    The generated .llm/skills/index.md is exempt: it is machine-written by
    scripts/generate-skills-index.ps1, not an authored file.

    Skill/context messages use [skill-sizes] / [context-size] prefixes.

.PARAMETER VerboseOutput
    Emit per-file OK status.

.PARAMETER FailOnCritical
    Exit non-zero when a file is in the 270-300 critical band.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh -NoProfile -File scripts/lint-skill-sizes.ps1
    pwsh -NoProfile -File scripts/lint-skill-sizes.ps1 -VerboseOutput
#>
Param(
    [switch]$VerboseOutput,
    [switch]$FailOnCritical,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-ErrorMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix ERROR: $msg" -ForegroundColor Red
}

function Write-CriticalWarningMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix CRITICAL: $msg" -ForegroundColor Magenta
}

function Write-SuccessMsg($msg, $prefix = "[skill-sizes]") {
    Write-Host "$prefix OK: $msg" -ForegroundColor Green
}

if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.FullName
}

$llmDir = Join-Path -Path $RepoRoot -ChildPath '.llm'
$contextFile = Join-Path -Path $llmDir -ChildPath 'context.md'
$maxLines = 300
$criticalLines = 270
$exitCode = 0

if (-not (Test-Path -LiteralPath $llmDir)) {
    Write-ErrorMsg "LLM directory not found at: $llmDir"
    exit 1
}

# Authored files: everything under .llm/ EXCEPT the machine-written index.
$authoredFiles = @(
    Get-ChildItem -Path $llmDir -Recurse -File |
        Where-Object { $_.FullName -ne (Join-Path $llmDir 'skills/index.md') } |
        Sort-Object FullName
)

$errorCount = 0
$criticalCount = 0
$okCount = 0

foreach ($file in $authoredFiles) {
    $lineCount = @([System.IO.File]::ReadAllLines($file.FullName)).Count
    $relativePath = $file.FullName.Substring($llmDir.Length + 1).Replace('\', '/')

    if ($lineCount -gt $maxLines) {
        Write-ErrorMsg "${relativePath}: $lineCount lines (max: $maxLines) - MUST split/reduce"
        $exitCode = 1
        $errorCount++
    }
    elseif ($lineCount -ge $criticalLines) {
        $remaining = $maxLines - $lineCount
        if ($remaining -eq 0) {
            Write-CriticalWarningMsg "${relativePath}: $lineCount lines - AT the $maxLines line limit! Must split/reduce"
        }
        else {
            Write-CriticalWarningMsg "${relativePath}: $lineCount lines - only $remaining lines from the $maxLines limit! Consider splitting now"
        }
        $criticalCount++
        if ($FailOnCritical) {
            $exitCode = 1
        }
    }
    else {
        if ($VerboseOutput) {
            Write-SuccessMsg "${relativePath}: $lineCount lines"
        }
        $okCount++
    }
}

# Summary
Write-Host ""
Write-Host ("=" * 60)
Write-Host "[skill-sizes] Summary: $($authoredFiles.Count) authored files checked (limits: error >$maxLines, critical >=$criticalLines)"
if ($VerboseOutput -or $criticalCount -gt 0 -or $errorCount -gt 0) {
    Write-Host "[skill-sizes]   OK: $okCount, Critical: $criticalCount, Errors: $errorCount"
}

if ($exitCode -eq 0) {
    Write-Host "All files within size limits" -ForegroundColor Green
}
else {
    Write-Host "Some files exceed size limits - see errors above" -ForegroundColor Red
}
Write-Host ("=" * 60)

exit $exitCode
