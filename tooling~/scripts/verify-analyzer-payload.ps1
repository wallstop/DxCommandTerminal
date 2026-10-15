<#
.SYNOPSIS
    Verifies shipped analyzer bytes and optional two-checkout reproducibility.
.DESCRIPTION
    Delegates to typed stdlib Python. The shared runner prefers uv and falls
    back to Python 3.11+. Both modes compare working-tree builds first.
#>
Param(
    [string]$RepoRoot,
    [switch]$TwoCheckout
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolingRoot = (Get-Item $PSScriptRoot).Parent.FullName
$pythonScript = Join-Path $PSScriptRoot 'verify_analyzer_payload.py'
$pythonArgs = @()
if ($RepoRoot) { $pythonArgs += @('--repo-root', $RepoRoot) }
if ($TwoCheckout) { $pythonArgs += '--two-checkout' }

. (Join-Path $PSScriptRoot 'python-runner.ps1')
exit (Invoke-ToolingPython -ToolingRoot $toolingRoot -PythonArguments (@($pythonScript) + $pythonArgs) -ErrorPrefix 'analyzer-payload')
