<#
.SYNOPSIS
    Red-green tests for the .llm line-limit linter.

.DESCRIPTION
    Delegates to the typed-python suite (tooling~/scripts/tests/
    test_lint_skill_sizes.py, issue #220 stage 1); this file stays only
    so the CI step and the test-runner entry points keep their names.
    The runner prefers uv (pinned interpreter via tooling~/pyproject.toml)
    and falls back to any system python 3.9+.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/tests/test-lint-skill-sizes.ps1
#>
Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$testsRoot = $PSScriptRoot
$toolingRoot = (Get-Item $testsRoot).Parent.Parent.FullName
$pythonTests = Join-Path $testsRoot 'test_lint_skill_sizes.py'

$pythonArgs = @($pythonTests)
if ($VerboseOutput) { $pythonArgs += '-v' }

$runnerCommand = $null
$runnerPrefix = @()
foreach ($candidate in @(@('uv', 'run', '--project', $toolingRoot, 'python'), @('python'), @('python3'))) {
    if (Get-Command $candidate[0] -ErrorAction SilentlyContinue) {
        $runnerCommand = $candidate[0]
        if ($candidate.Count -gt 1) { $runnerPrefix = $candidate[1..($candidate.Count - 1)] }
        break
    }
}
if ($null -eq $runnerCommand) {
    Write-Host "[skill-sizes] ERROR: no runner found (need uv, python, or python3 on PATH)" -ForegroundColor Red
    exit 1
}

& $runnerCommand @runnerPrefix @pythonArgs
exit $LASTEXITCODE
