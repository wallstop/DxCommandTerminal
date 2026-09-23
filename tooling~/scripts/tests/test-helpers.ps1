<#
.SYNOPSIS
    Shared helpers for the pwsh test scripts under tooling~/scripts/tests/.

.DESCRIPTION
    Minimal test harness: named test cases with automatic pass/fail tracking,
    TAP-style output, and a fixture builder that materializes an isolated
    repository root (with .llm/ + pointer files) so linters can be exercised
    against arbitrary valid/invalid states without touching the real repo.
#>

Set-StrictMode -Version Latest

$script:TestPassCount = 0
$script:TestFailCount = 0
$script:TestFailures = @()
$script:TestCurrentCase = ''

function Start-TestRun {
    param([Parameter(Mandatory = $true)][string]$Name)
    Write-Host ""
    Write-Host "Running: $Name" -ForegroundColor Cyan
    $script:TestPassCount = 0
    $script:TestFailCount = 0
    $script:TestFailures = @()
}

function Invoke-TestCase {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Body
    )
    $script:TestCurrentCase = $Name
    try {
        & $Body
        $script:TestPassCount++
        Write-Host "ok - $Name" -ForegroundColor Green
    }
    catch {
        $script:TestFailCount++
        $script:TestFailures += $Name
        Write-Host "not ok - $Name" -ForegroundColor Red
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
    }
    finally {
        $script:TestCurrentCase = ''
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not $Condition) {
        throw "Assertion failed: $Message (case: $($script:TestCurrentCase))"
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)][AllowNull()]$Expected,
        [Parameter(Mandatory = $true)][AllowNull()]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if (-not [object]::Equals($Expected, $Actual)) {
        throw "Assertion failed: $Message. Expected '$Expected', actual '$Actual' (case: $($script:TestCurrentCase))"
    }
}

function Assert-ExitCode {
    param(
        [Parameter(Mandatory = $true)][int]$Expected,
        [Parameter(Mandatory = $true)][string]$ActualOutput,
        [Parameter(Mandatory = $true)][string]$Message
    )
    if ($Expected -eq 0) {
        Assert-True ($null -ne $ActualOutput -and $ActualOutput -notmatch '\[llm-lint\] ERROR|\[skill-sizes\] ERROR') "$Message expected success, got errors: $ActualOutput"
    }
}

# Runs a pwsh script and returns @{ ExitCode; Output }.
function Invoke-PwshScript {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string[]]$Arguments = @()
    )
    $output = & pwsh -NoProfile -File $Path @Arguments 2>&1 | ForEach-Object { "$_" }
    $exitCode = $LASTEXITCODE
    return @{ ExitCode = $exitCode; Output = ($output -join "`n") }
}

# Materializes an isolated fixture repo root and returns its path.
function New-FixtureRepo {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Skills,
        [Parameter(Mandatory = $true)][string]$ContextContent,
        [hashtable]$IndexContent,
        [hashtable]$PointerOverrides,
        [switch]$NoIndex,
        [switch]$SkipPointers
    )
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("dxllm-fixture-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $root '.llm/skills') -Force | Out-Null

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $root '.llm/context.md'), $ContextContent, $utf8NoBom)

    foreach ($skillName in $Skills.Keys) {
        $skillDir = Join-Path $root ".llm/skills/$skillName"
        New-Item -ItemType Directory -Path $skillDir -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $skillDir 'SKILL.md'), $Skills[$skillName], $utf8NoBom)
    }

    if (-not $NoIndex) {
        # In-process generation: dot-sourcing skips the generator's main guard,
        # so fixture setup avoids a pwsh spawn per fixture.
        if (-not (Get-Command New-SkillsIndex -ErrorAction SilentlyContinue)) {
            . (Join-Path $PSScriptRoot '../generate-skills-index.ps1')
        }
        if ((New-SkillsIndex -RepoRoot $root) -ne 0) { throw "fixture index generation failed" }
        if ($IndexContent) {
            [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/index.md'), $IndexContent.Content, $utf8NoBom)
        }
    }

    if (-not $SkipPointers) {
        $pointers = @{
            'AGENTS.md'                        = "# Guidelines`n`nSee the [AI Agent Guidelines](./.llm/context.md).`n"
            'CLAUDE.md'                        = "# Guidelines`n`nSee the [AI Agent Guidelines](./.llm/context.md).`n"
            '.cursorrules'                     = "# Guidelines`n`nSee the [AI Agent Guidelines](./.llm/context.md).`n"
            '.github/copilot-instructions.md'  = "# Guidelines`n`nSee the [AI Agent Guidelines](../.llm/context.md).`n"
        }
        foreach ($relative in $pointers.Keys) {
            $content = $pointers[$relative]
            if ($PointerOverrides -and $PointerOverrides.ContainsKey($relative)) {
                $content = $PointerOverrides[$relative]
            }
            $target = Join-Path $root $relative
            $parent = Split-Path -Parent $target
            if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            [System.IO.File]::WriteAllText($target, $content, $utf8NoBom)
        }
    }

    return $root
}

function Remove-FixtureRepo {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ($Path -and (Test-Path -LiteralPath $Path)) {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Complete-TestRun {
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "Results: $($script:TestPassCount) passed, $($script:TestFailCount) failed" -ForegroundColor $(if ($script:TestFailCount -eq 0) { 'Green' } else { 'Red' })
    if ($script:TestFailCount -gt 0) {
        foreach ($failure in $script:TestFailures) {
            Write-Host "  FAILED: $failure" -ForegroundColor Red
        }
    }
    Write-Host ("=" * 60)
    exit $(if ($script:TestFailCount -gt 0) { 1 } else { 0 })
}
