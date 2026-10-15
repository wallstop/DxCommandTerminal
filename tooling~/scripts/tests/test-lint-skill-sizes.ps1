<#
.SYNOPSIS
    Red-green tests for the .llm line-limit linter.

.DESCRIPTION
    Delegates to the typed-python suite (tooling~/scripts/tests/
    test_lint_skill_sizes.py, issue #220 stage 1); this file stays only
    so the CI step and the test-runner entry points keep their names.
    The runner prefers uv (pinned interpreter via tooling~/pyproject.toml)
    and falls back to any system python 3.11+ (the manifest floor).

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

. (Join-Path (Split-Path $testsRoot -Parent) 'python-runner.ps1')
exit (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments $pythonArgs -ErrorPrefix 'skill-sizes')
