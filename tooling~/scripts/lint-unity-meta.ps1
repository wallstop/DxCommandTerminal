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

. (Join-Path $scriptsRoot 'python-runner.ps1')
exit (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments (@($pythonScript) + $pythonArgs) -ErrorPrefix 'meta-lint')
