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

    . (Join-Path $PSScriptRoot 'python-runner.ps1')
    return (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments (@($pythonScript) + $pythonArgs) -ErrorPrefix 'skills-index' -RawOutput:$Stdout)
}

if ($MyInvocation.InvocationName -ne '.') {
    exit (New-SkillsIndex -OutputPath:$OutputPath -Stdout:$Stdout -VerboseOutput:$VerboseOutput -RepoRoot:$RepoRoot)
}
