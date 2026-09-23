<#
.SYNOPSIS
    Red-green tests for tooling~/scripts/lint-skill-sizes.ps1.

.DESCRIPTION
    Builds isolated fixture repos and asserts boundary behavior of the line
    limits (269 OK / 270 critical / 300 at-limit / 301 error), the index.md
    exemption, recursive coverage, and the -FailOnCritical switch.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/tests/test-lint-skill-sizes.ps1
#>
Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

# In-process linter: dot-sourcing skips the main guard, avoiding a pwsh
# spawn per case (~0.5-1s each).
if (-not (Get-Command Invoke-SkillSizesLint -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot '../lint-skill-sizes.ps1')
}

function Invoke-Linter {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$FailOnCritical,
        [switch]$VerboseLinter
    )
    return Invoke-InProcessEntry {
        param($Root, $FailOnCritical, $VerboseLinter)
        Invoke-SkillSizesLint -RepoRoot $Root `
            -FailOnCritical:([bool]$FailOnCritical) `
            -VerboseOutput:([bool]$VerboseLinter)
    } -Parameters @{
        Root          = $Root
        FailOnCritical = [bool]$FailOnCritical
        VerboseLinter  = [bool]$VerboseLinter
    }
}

function New-SizedFile {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][int]$Lines
    )
    $target = Join-Path $Root $RelativePath
    $parent = Split-Path -Parent $target
    if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $content = (1..$Lines | ForEach-Object { "line $_" }) -join "`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($target, $content, $utf8NoBom)
}

Start-TestRun 'lint-skill-sizes boundary tests'

Invoke-TestCase 'files at or under 269 lines pass' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/valid-skill/references/deep.md' -Lines 269
        $result = Invoke-Linter -Root $root -VerboseLinter
        Assert-Equal 0 $result.ExitCode '269-line file must pass'
        Assert-True ($result.Output -match 'All files within size limits') 'summary must report success'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'file at exactly 270 lines is CRITICAL but exits 0' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/valid-skill/references/big.md' -Lines 270
        $result = Invoke-Linter -Root $root
        Assert-Equal 0 $result.ExitCode '270-line file must warn, not fail'
        Assert-True ($result.Output -match '\[skill-sizes\] CRITICAL: .*big\.md: 270 lines') 'critical warning must name the file and count'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'file at exactly 300 lines is CRITICAL (at limit) and exits 0' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/valid-skill/references/big.md' -Lines 300
        $result = Invoke-Linter -Root $root
        Assert-Equal 0 $result.ExitCode '300-line file must warn, not fail'
        Assert-True ($result.Output -match 'AT the 300 line limit') 'at-limit message expected'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'file over 300 lines FAILS' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/valid-skill/references/big.md' -Lines 301
        $result = Invoke-Linter -Root $root
        Assert-Equal 1 $result.ExitCode '301-line file must fail'
        Assert-True ($result.Output -match '\[skill-sizes\] ERROR: .*big\.md: 301 lines \(max: 300\) - MUST split/reduce') 'error must name the file and limit'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'generated index.md is exempt from limits' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/index.md' -Lines 500
        $result = Invoke-Linter -Root $root
        Assert-Equal 0 $result.ExitCode 'index.md must be exempt'
        Assert-True ($result.Output -notmatch 'index\.md') 'index.md must not appear in output'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'over-limit context.md fails' {
    $longContext = "# Title`n`n" + ((1..305 | ForEach-Object { "ctx line $_" }) -join "`n") + "`n"
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent $longContext
    try {
        $result = Invoke-Linter -Root $root
        Assert-Equal 1 $result.ExitCode 'over-limit context.md must fail'
        Assert-True ($result.Output -match 'context\.md: 307 lines \(max: 300\)') 'error must name context.md'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase '-FailOnCritical promotes the critical band to a failure' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n"
    try {
        New-SizedFile -Root $root -RelativePath '.llm/skills/valid-skill/references/big.md' -Lines 285
        $result = Invoke-Linter -Root $root -FailOnCritical
        Assert-Equal 1 $result.ExitCode '-FailOnCritical must fail the run'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'missing .llm directory fails fast' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("dxllm-empty-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $result = Invoke-Linter -Root $root
        Assert-Equal 1 $result.ExitCode 'missing .llm dir must fail'
        Assert-True ($result.Output -match 'LLM directory not found') 'must report the missing directory'
    }
    finally { Remove-FixtureRepo $root }
}

Complete-TestRun
