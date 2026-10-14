<#
.SYNOPSIS
    Validates skill frontmatter, index freshness, and agent pointer files.

.DESCRIPTION
    Delegates to lint_llm_instructions.py (typed standard-library Python).
    Keeps Invoke-LlmInstructionsLint for dot-sourced fixture callers.
    Prefers uv, then a working Python 3.11+ interpreter on PATH.
#>
Param(
    [switch]$Fix,
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-LlmInstructionsLint {
    param(
        [switch]$Fix,
        [switch]$VerboseOutput,
        [string]$RepoRoot
    )

    $toolingRoot = (Get-Item $PSScriptRoot).Parent.FullName
    $pythonScript = Join-Path $PSScriptRoot 'lint_llm_instructions.py'
    $pythonArgs = @()
    if ($Fix) { $pythonArgs += '--fix' }
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
        $message = '[llm-lint] ERROR: no working runner found (need uv or Python 3.11+ on PATH)'
        Write-Host $message -ForegroundColor Red
        return 1
    }

    # Python emits UTF-8 even when the inherited native-output decoder differs.
    $previousEncoding = [Console]::OutputEncoding
    try {
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        & $runnerCommand @runnerPrefix $pythonScript @pythonArgs | ForEach-Object {
            Write-Host $_
        }
        return $LASTEXITCODE
    }
    finally {
        [Console]::OutputEncoding = $previousEncoding
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    exit (Invoke-LlmInstructionsLint -Fix:$Fix -VerboseOutput:$VerboseOutput -RepoRoot:$RepoRoot)
}
