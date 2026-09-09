<#
.SYNOPSIS
    Red-green tests for tooling~/scripts/generate-skills-index.ps1.

.DESCRIPTION
    Asserts the generator's contract: cross-run byte determinism, ordinal
    (culture-invariant) sorting, category section ordering, frontmatter value
    parsing (quoted strings), and graceful skipping of malformed skills.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/tests/test-generate-skills-index.ps1
#>
Param(
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'test-helpers.ps1')

$generator = (Get-Item (Join-Path $PSScriptRoot '../generate-skills-index.ps1')).FullName

function Invoke-Generator {
    param([Parameter(Mandatory = $true)][string]$Root, [string]$OutputPath)
    $args = @('-RepoRoot', $Root)
    if ($OutputPath) { $args += @('-OutputPath', $OutputPath) }
    return Invoke-PwshScript -Path $generator -Arguments $args
}

function Get-FixtureIndex {
    param([Parameter(Mandatory = $true)][string]$Root)
    return [System.IO.File]::ReadAllText((Join-Path $Root '.llm/skills/index.md'))
}

Start-TestRun 'generate-skills-index tests'

Invoke-TestCase 'two runs are byte-identical (determinism)' {
    $root = New-FixtureRepo -Skills @{ 'alpha-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# T`n" -NoIndex
    try {
        $tempA = [System.IO.Path]::GetTempFileName()
        $tempB = [System.IO.Path]::GetTempFileName()
        try {
            Invoke-Generator -Root $root -OutputPath $tempA | Out-Null
            Invoke-Generator -Root $root -OutputPath $tempB | Out-Null
            $bytesA = [System.IO.File]::ReadAllBytes($tempA)
            $bytesB = [System.IO.File]::ReadAllBytes($tempB)
            Assert-Equal $bytesA.Length $bytesB.Length 'run lengths must match'
            for ($i = 0; $i -lt $bytesA.Length; $i++) {
                Assert-Equal $bytesA[$i] $bytesB[$i] "byte $i must match"
            }
        }
        finally { Remove-Item $tempA, $tempB -Force -ErrorAction SilentlyContinue }
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'skills are sorted ORDINALLY (skill-10 before skill-2)' {
    $fixture = Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw
    $root = New-FixtureRepo -Skills @{
        'skill-2'  = ($fixture -replace 'name: valid-skill', 'name: skill-2')
        'skill-10' = ($fixture -replace 'name: valid-skill', 'name: skill-10')
    } -ContextContent "# T`n" -NoIndex
    try {
        Invoke-Generator -Root $root | Out-Null
        $index = Get-FixtureIndex $root
        $pos10 = $index.IndexOf('skill-10')
        $pos2 = $index.IndexOf('skill-2')
        Assert-True ($pos10 -ge 0 -and $pos2 -ge 0) 'both skills must appear'
        Assert-True ($pos10 -lt $pos2) "ordinal sort must place skill-10 ($pos10) before skill-2 ($pos2); culture sorts invert this"
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'sections are grouped Core, Performance, Feature' {
    $fixture = Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw
    $root = New-FixtureRepo -Skills @{
        'feat-one' = ($fixture -replace 'name: valid-skill', 'name: feat-one')
        'perf-one' = (($fixture -replace 'name: valid-skill', 'name: perf-one') -replace 'category: Feature', 'category: Performance')
        'core-one' = (($fixture -replace 'name: valid-skill', 'name: core-one') -replace 'category: Feature', 'category: Core')
    } -ContextContent "# T`n" -NoIndex
    try {
        Invoke-Generator -Root $root | Out-Null
        $index = Get-FixtureIndex $root
        $posCore = $index.IndexOf('Core Skills')
        $posPerf = $index.IndexOf('Performance Skills')
        $posFeat = $index.IndexOf('Feature Skills')
        Assert-True ($posCore -lt $posPerf -and $posPerf -lt $posFeat) "section order must be Core ($posCore) < Performance ($posPerf) < Feature ($posFeat)"
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'quoted description values are unquoted' {
    $fixture = Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw
    $root = New-FixtureRepo -Skills @{
        'quoted-skill' = (($fixture -replace 'name: valid-skill', 'name: quoted-skill') -replace '(?m)^description: .*$', 'description: "Quoted description here."')
    } -ContextContent "# T`n" -NoIndex
    try {
        Invoke-Generator -Root $root | Out-Null
        $index = Get-FixtureIndex $root
        Assert-True ($index -match 'Quoted description here\.') 'quoted value must be parsed'
        Assert-True ($index -notmatch '"Quoted') 'quotes must be stripped'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'skills without description are skipped' {
    $fixture = Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw
    $root = New-FixtureRepo -Skills @{
        'good-skill'  = ($fixture -replace 'name: valid-skill', 'name: good-skill')
        'no-desc'     = (($fixture -replace 'name: valid-skill', 'name: no-desc') -replace '(?m)^description: .*\r?\n', '')
    } -ContextContent "# T`n" -NoIndex
    try {
        Invoke-Generator -Root $root | Out-Null
        $index = Get-FixtureIndex $root
        Assert-True ($index -match 'good-skill') 'valid skill must be indexed'
        Assert-True ($index -notmatch 'no-desc') 'description-less skill must be skipped'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'output is UTF-8 without BOM and LF-only' {
    $root = New-FixtureRepo -Skills @{ 'valid-skill' = (Get-Content (Join-Path $PSScriptRoot 'fixtures/valid-skill.md') -Raw) } -ContextContent "# T`n" -NoIndex
    try {
        Invoke-Generator -Root $root | Out-Null
        $bytes = [System.IO.File]::ReadAllBytes((Join-Path $root '.llm/skills/index.md'))
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        Assert-True (-not $hasBom) 'no BOM allowed'
        Assert-True (-not ($bytes -contains 0x0D)) 'no CR allowed (LF only)'
    }
    finally { Remove-FixtureRepo $root }
}

Invoke-TestCase 'missing skills directory exits 1' {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) ("dxllm-gen-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    try {
        $result = Invoke-Generator -Root $root
        Assert-Equal 1 $result.ExitCode 'missing skills dir must fail'
        Assert-True ($result.Output -match 'Skills directory not found') 'must report the missing directory'
    }
    finally { Remove-FixtureRepo $root }
}

Complete-TestRun
