<#
.SYNOPSIS
    Validates LLM file sizes against the repo's line limits.

.DESCRIPTION
    Delegates to the typed-python implementation (tooling~/scripts/
    lint_skill_sizes.py, issue #220 stage 1): the implementation and its
    tests live in python under uv; this file stays only so the entry
    points keep their names and shape (pre-commit hook, the LLM
    Instructions Lint workflow, and this path in the skills docs). The
    runner prefers uv (pinned interpreter via tooling~/pyproject.toml)
    and falls back to any system python 3.11+ (the manifest floor) - the script is standard
    library only.

    Thresholds: >300 lines ERROR, 270-300 CRITICAL warning, <=269 OK.
    The generated .llm/skills/index.md is exempt.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/lint-skill-sizes.ps1
    pwsh -NoProfile -File tooling~/scripts/lint-skill-sizes.ps1 -VerboseOutput
#>
Param(
    [switch]$VerboseOutput,
    [switch]$FailOnCritical,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptsRoot = $PSScriptRoot
$toolingRoot = (Get-Item $scriptsRoot).Parent.FullName
$pythonScript = Join-Path $scriptsRoot 'lint_skill_sizes.py'

$pythonArgs = @()
if ($VerboseOutput) { $pythonArgs += '--verbose-output' }
if ($FailOnCritical) { $pythonArgs += '--fail-on-critical' }
if ($RepoRoot) { $pythonArgs += @('--repo-root', $RepoRoot) }

. (Join-Path $scriptsRoot 'python-runner.ps1')
exit (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments (@($pythonScript) + $pythonArgs) -ErrorPrefix 'skill-sizes')
