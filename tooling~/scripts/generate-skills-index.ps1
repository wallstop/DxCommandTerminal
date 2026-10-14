<#
.SYNOPSIS
    Generates the deterministic skills index at .llm/skills/index.md.

.DESCRIPTION
    Delegates to generate_skills_index.py (typed standard-library Python).
    Keeps New-SkillsIndex for dot-sourced linter and fixture callers.
    Prefers uv, then a working Python 3.11+ interpreter on PATH.
#>
Param(
    [string]$OutputPath,
    [switch]$Stdout,
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-SkillsIndex {
    param(
        [string]$OutputPath,
        [switch]$Stdout,
        [switch]$VerboseOutput,
        [string]$RepoRoot
    )

    $toolingRoot = (Get-Item $PSScriptRoot).Parent.FullName
    $pythonScript = Join-Path $PSScriptRoot 'generate_skills_index.py'
    $pythonArgs = @()
    if ($OutputPath) { $pythonArgs += @('--output-path', $OutputPath) }
    if ($Stdout) { $pythonArgs += '--stdout' }
    if ($VerboseOutput) { $pythonArgs += '--verbose-output' }
    if ($RepoRoot) { $pythonArgs += @('--repo-root', $RepoRoot) }

    $runnerCommand = $null
    $runnerPrefix = @()
    foreach ($candidate in @(@('uv', 'run', '--project', $toolingRoot, 'python'), @('python'), @('python3'))) {
        if (-not (Get-Command $candidate[0] -ErrorAction SilentlyContinue)) { continue }
        if ($candidate[0] -ne 'uv') {
            try {
                $LASTEXITCODE = 1
                & $candidate[0] -c 'import sys; sys.exit(0 if sys.version_info >= (3, 11) else 1)' *> $null
                if ($LASTEXITCODE -ne 0) { continue }
            }
            catch { continue }
        }
        $runnerCommand = $candidate[0]
        if ($candidate.Count -gt 1) { $runnerPrefix = $candidate[1..($candidate.Count - 1)] }
        break
    }
    if ($null -eq $runnerCommand) {
        Write-Host '[skills-index] ERROR: no working runner found (need uv or Python 3.11+ on PATH)' -ForegroundColor Red
        return 1
    }

    # Native output must not join the function's integer return value.
    & $runnerCommand @runnerPrefix $pythonScript @pythonArgs | ForEach-Object {
        if ($Stdout) { [Console]::Out.Write("$_`n") }
        else { Write-Host $_ }
    }
    return $LASTEXITCODE
}

if ($MyInvocation.InvocationName -ne '.') {
    exit (New-SkillsIndex -OutputPath:$OutputPath -Stdout:$Stdout -VerboseOutput:$VerboseOutput -RepoRoot:$RepoRoot)
}
