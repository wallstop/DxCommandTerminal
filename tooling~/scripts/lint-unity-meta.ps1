<#
.SYNOPSIS
    Validates Unity .meta file hygiene for every tracked file in the repo.

.DESCRIPTION
    Delegates to the typed-python implementation (tooling~/scripts/
    lint_unity_meta.py, issue #220 stage 2): the implementation and its
    tests live in python under uv; this file stays only so the entry
    points keep their names and shape (pre-commit hook `unity-meta-lint`
    and the Unity Meta Lint workflow). The runner prefers uv (pinned
    interpreter via tooling~/pyproject.toml) and falls back to any system
    python 3.11+ (the manifest floor) - the script is standard library only.

    Rules over `git ls-files` (what a fresh clone contains): every tracked
    .meta has its target tracked (no orphan metas), every Unity-visible
    tracked file/directory has a tracked .meta, and every .meta declares
    exactly one unique 32-hex guid. Messages use the [meta-lint] prefix.

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

$scriptsRoot = $PSScriptRoot
$toolingRoot = (Get-Item $scriptsRoot).Parent.FullName
$pythonScript = Join-Path $scriptsRoot 'lint_unity_meta.py'

$pythonArgs = @()
if ($VerboseOutput) { $pythonArgs += '--verbose-output' }
if ($RepoRoot) { $pythonArgs += @('--repo-root', $RepoRoot) }

$runnerCommand = $null
$runnerPrefix = @()
foreach ($candidate in @(@('uv', 'run', '--project', $toolingRoot, 'python'), @('python'), @('python3'))) {
    if (-not (Get-Command $candidate[0] -ErrorAction SilentlyContinue)) { continue }
    if ($candidate[0] -ne 'uv') {
        # WindowsApps aliases resolve on PATH even when no interpreter is installed.
        try {
            $global:LASTEXITCODE = 1
            & $candidate[0] -c 'import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)' *> $null
            if ($global:LASTEXITCODE -ne 0) { continue }
        }
        catch { continue }
    }
    $runnerCommand = $candidate[0]
    if ($candidate.Count -gt 1) { $runnerPrefix = $candidate[1..($candidate.Count - 1)] }
    break
}
if ($null -eq $runnerCommand) {
    Write-Host "[meta-lint] ERROR: no working runner found (need uv or Python 3.11+ on PATH)" -ForegroundColor Red
    exit 1
}

& $runnerCommand @runnerPrefix $pythonScript @pythonArgs
exit $global:LASTEXITCODE
