<#
.SYNOPSIS
    Red-green tests for the deterministic skills-index generator.

.DESCRIPTION
    Delegates to the typed-python suite (tooling~/scripts/tests/
    test_generate_skills_index.py, issue #220); this file stays only
    so the CI step and the test-runner entry points keep their names.
    The runner prefers uv (pinned interpreter via tooling~/pyproject.toml)
    and falls back to any system python 3.11+ (the manifest floor).

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/tests/test-generate-skills-index.ps1
#>
Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$testsRoot = $PSScriptRoot
$toolingRoot = (Get-Item $testsRoot).Parent.Parent.FullName
$pythonTests = Join-Path $testsRoot 'test_generate_skills_index.py'

$pythonArgs = @($pythonTests)
if ($VerboseOutput) { $pythonArgs += '-v' }

. (Join-Path (Split-Path $testsRoot -Parent) 'python-runner.ps1')
exit (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments $pythonArgs -ErrorPrefix 'skills-index')
