<#
.SYNOPSIS
    Red-green tests for scripts/lint-llm-instructions.ps1.

.DESCRIPTION
    Builds isolated fixture repos and asserts both failure paths (red) and the
    happy path (green) for: SKILL.md frontmatter validity, index freshness and
    determinism, index encoding (no BOM / LF), the context.md contract, and
    pointer-file delegation. Also verifies -Fix repairs a stale index.

.EXAMPLE
    pwsh -NoProfile -File scripts/tests/test-llm-instructions-lint.ps1
#>
Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$instructionsLinter = (Get-Item (Join-Path $PSScriptRoot '../lint-llm-instructions.ps1')).FullName
$validSkill = Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw
$validContext = "# Title`n`nSee [index](./skills/index.md).`n"

function New-ValidFixture {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = $validSkill } -ContextContent $validContext
    return $root
}

function Invoke-Linter {
    param([Parameter(Mandatory = $true)][string]$Root, [switch]$Fix)
    $args = @('-RepoRoot', $Root)
    if ($Fix) { $args += '-Fix' }
    return Invoke-PwshScript -Path $instructionsLinter -Arguments $args
}

function Assert-LinterFailed {
    param([Parameter(Mandatory = $true)][hashtable]$Result, [Parameter(Mandatory = $true)][string]$ExpectedPattern, [Parameter(Mandatory = $true)][string]$Case)
    Assert-Equal 1 $Result.ExitCode "$Case must fail"
    Assert-True ($Result.Output -match $ExpectedPattern) "$Case must report '$ExpectedPattern'; output was:`n$($Result.Output)"
}

Start-TestRun 'lint-llm-instructions tests'

Invoke-TestCase 'valid fixture passes' {
    $root = New-ValidFixture
    try {
        $result = Invoke-Linter -Root $root
        Assert-Equal 0 $result.ExitCode "valid fixture must pass; output:`n$($result.Output)"
        Assert-True ($result.Output -match 'LLM instructions validation passed!') 'success banner expected'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'missing pointer file fails' {
    $root = New-ValidFixture
    try {
        Remove-Item -LiteralPath (Join-Path $root 'CLAUDE.md') -Force
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'Claude Code entrypoint not found' 'missing pointer'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'pointer file without delegation link fails' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = $validSkill } -ContextContent $validContext -PointerOverrides @{
        'AGENTS.md' = "# Guidelines`n`nAll guidance lives here, nothing to delegate.`n"
    }
    try {
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'AGENTS\.md \(Codex/OpenCode/nanocoder\) entrypoint must delegate' 'pointer without link'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'SKILL.md without frontmatter fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), "# No Frontmatter`n`nBody.`n", $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result "missing '---' delimited YAML frontmatter" 'no frontmatter'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'name not matching directory fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = $validSkill -replace 'name: valid-skill', 'name: other-name'
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result "must match parent directory name 'valid-skill'" 'name mismatch'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'uppercase name fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = $validSkill -replace 'name: valid-skill', 'name: Valid-Skill'
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'lowercase a-z/0-9 with single interior hyphens' 'uppercase name'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'name over 64 chars fails' {
    $root = New-ValidFixture
    try {
        $tooLong = 'a' * 65
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = $validSkill -replace 'name: valid-skill', "name: $tooLong"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'name is 65 chars \(max 64\)' 'long name'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'missing description fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = ($validSkill -split "`n" | Where-Object { $_ -notmatch '^description:' }) -join "`n"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result "missing required 'description'" 'missing description'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'non-ASCII description fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $emDash = [char]0x2014
        $bad = $validSkill -replace 'Valid fixture skill used', "Valid fixture skill $emDash used"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'description contains non-ASCII character' 'non-ASCII description'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'block-scaler description fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = $validSkill -replace 'description: .*', "description: |"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'single inline line' 'block scaler description'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'description over 1024 chars fails' {
    $root = New-ValidFixture
    try {
        $tooLong = 'x' * 1025
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        # (?m) is required: ^/$ anchor at line boundaries only in multiline mode,
        # and the description is not the first line of the file.
        $bad = $validSkill -replace '(?m)^description: .*$', "description: $tooLong"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'description is 1025 chars \(max 1024\)' 'long description'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'unknown category fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $bad = $validSkill -replace 'category: Feature', 'category: Core Skills'
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result "unknown metadata\.category 'Core Skills'" 'unknown category'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'empty skill body fails' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        # Valid frontmatter, but nothing after the closing '---' -> empty body.
        $bad = "---`nname: valid-skill`ndescription: Valid frontmatter with no body.`n---`n"
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $bad, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'skill body is empty' 'empty body'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'skill directory without SKILL.md fails' {
    $root = New-ValidFixture
    try {
        New-Item -ItemType Directory -Path (Join-Path $root '.llm/skills/empty-skill') -Force | Out-Null
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result "Skill directory 'empty-skill' has no SKILL\.md" 'dir without SKILL.md'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'stale index fails; -Fix repairs it' {
    $root = New-ValidFixture
    try {
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        $edited = $validSkill -replace 'Valid fixture skill used by the automated linter tests\.', 'Edited description that is not yet reflected in the index.'
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/valid-skill/SKILL.md'), $edited, $utf8NoBom)

        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'Skills index index\.md is out of date!' 'stale index must fail'

        $fixed = Invoke-Linter -Root $root -Fix
        Assert-Equal 0 $fixed.ExitCode "-Fix must repair the index; output:`n$($fixed.Output)"
        Assert-True ($fixed.Output -match 'Regenerated index\.md') '-Fix must report regeneration'
        Assert-True (([System.IO.File]::ReadAllText((Join-Path $root '.llm/skills/index.md')) -match 'Edited description')) 'index must contain the new description'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'missing index fails' {
    $root = New-ValidFixture
    try {
        Remove-Item -LiteralPath (Join-Path $root '.llm/skills/index.md') -Force
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'index\.md does not exist' 'missing index'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'index with BOM fails' {
    $root = New-ValidFixture
    try {
        $content = [System.IO.File]::ReadAllText((Join-Path $root '.llm/skills/index.md'))
        [System.IO.File]::WriteAllText((Join-Path $root '.llm/skills/index.md'), $content, (New-Object System.Text.UTF8Encoding($true)))
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'UTF-8 BOM' 'BOM index'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'index with CRLF fails' {
    $root = New-ValidFixture
    try {
        $path = Join-Path $root '.llm/skills/index.md'
        $content = [System.IO.File]::ReadAllText($path) -replace "`n", "`r`n"
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'contains CR \(CRLF\)' 'CRLF index'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'context.md without index link fails' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = $validSkill } -ContextContent "# Title`n`nNo link here.`n"
    try {
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'must link to the generated index' 'context without link'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'context.md with two H1s fails' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = $validSkill } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n`n# Another H1`n"
    try {
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'exactly one H1 \(found 2\)' 'two H1s'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'context.md with stale embedded index marker fails' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = $validSkill } -ContextContent "# Title`n`nSee [index](./skills/index.md).`n`n<!-- BEGIN GENERATED SKILLS INDEX -->`nold table`n<!-- END GENERATED SKILLS INDEX -->`n"
    try {
        $result = Invoke-Linter -Root $root
        Assert-LinterFailed $result 'stale embedded-index marker' 'stale marker'
    }
    finally { Remove-FixtureRepo $root }
}

Complete-TestRun
