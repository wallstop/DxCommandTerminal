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

    . (Join-Path $PSScriptRoot 'python-runner.ps1')
    return (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments (@($pythonScript) + $pythonArgs) -ErrorPrefix 'llm-lint')
}

if ($MyInvocation.InvocationName -ne '.') {
    exit (Invoke-LlmInstructionsLint -Fix:$Fix -VerboseOutput:$VerboseOutput -RepoRoot:$RepoRoot)
}
