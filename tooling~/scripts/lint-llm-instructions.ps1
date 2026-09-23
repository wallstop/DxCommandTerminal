<#
.SYNOPSIS
    Validates LLM instruction files (.llm/) are correct and up-to-date.

.DESCRIPTION
    Validates:
    1. Required paths exist (.llm/skills, .llm/context.md, generator, pointer files).
    2. Every skill (.llm/skills/*/SKILL.md) has valid Agent Skills frontmatter:
       - name: a-z0-9- only, no leading/trailing/double hyphens, max 64 chars,
         and equal to the parent directory name.
       - description: single-line, ASCII-only, 1-1024 chars (non-ASCII is the
         cross-OS index-drift vector this lint exists to prevent).
       - metadata.category (optional): one of Core, Performance, Feature.
    3. The generated skills index .llm/skills/index.md is up-to-date (matches
       the generator output) AND the generator is deterministic (two runs
       produce identical bytes).
    4. The committed index is UTF-8 without BOM and LF-only.
    5. .llm/context.md links to ./skills/index.md, carries no stale embedded
       BEGIN/END index markers, and has exactly one H1.
    6. Every supported agent frontend delegates to .llm/context.md.

.PARAMETER Fix
    Regenerate .llm/skills/index.md to fix an out-of-date index.

.PARAMETER VerboseOutput
    Emit detailed progress.

.PARAMETER RepoRoot
    Repository root. Defaults to the parent of this script's directory.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/lint-llm-instructions.ps1
    pwsh -NoProfile -File tooling~/scripts/lint-llm-instructions.ps1 -Fix
#>
Param(
    [switch]$Fix,
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Capture the CLI-bound params BEFORE the dot-source below: dot-sourcing a
# Param() script binds its params into this scope, so the generator's same-named
# $RepoRoot/$VerboseOutput would otherwise overwrite ours with empty defaults
# and the main guard would silently lint the wrong root.
$script:cliFix = $Fix
$script:cliVerboseOutput = $VerboseOutput
$script:cliRepoRoot = $RepoRoot

# In-process generator: dot-sourcing skips the generator's main guard, so
# index generation below avoids a pwsh spawn (~0.5-1s each; the lint suite
# would otherwise spawn pwsh per fixture case).
. (Join-Path -Path $PSScriptRoot -ChildPath 'generate-skills-index.ps1')

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[llm-lint] $msg" -ForegroundColor Cyan }
}

function Write-ErrorMsg($msg) {
    Write-Host "[llm-lint] ERROR: $msg" -ForegroundColor Red
}

function Write-SuccessMsg($msg) {
    Write-Host "[llm-lint] $msg" -ForegroundColor Green
}

# Read CRLF->LF-normalized text for cross-platform-safe content comparison.
function Get-NormalizedText {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ([System.IO.File]::ReadAllText($Path) -replace "`r`n", "`n")
}

# Runs one generator pass in-process and returns $true on success.
function Invoke-IndexGeneration {
    param(
        [Parameter(Mandatory = $true)][string]$FailureLabel,
        [string]$OutputPath,
        [switch]$Fix,
        [switch]$VerboseOutput,
        [string]$RepoRoot
    )
    $callArgs = @{ RepoRoot = $RepoRoot; VerboseOutput = $VerboseOutput.IsPresent }
    if ($OutputPath) { $callArgs.OutputPath = $OutputPath }
    $generated = New-SkillsIndex @callArgs
    if ($generated -ne 0) {
        Write-ErrorMsg "$FailureLabel (generator exit $generated)."
        return $false
    }
    return $true
}

# Full validation over one repo root; returns 0 (valid) or 1 (invalid).
# Tests dot-source this file and call the function directly; running the file
# as a script reaches it through the main guard below.
function Invoke-LlmInstructionsLint {
    param(
        [switch]$Fix,
        [switch]$VerboseOutput,
        [string]$RepoRoot
    )

    if (-not $RepoRoot) {
        $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
    }

    $skillsDir = Join-Path -Path $RepoRoot -ChildPath '.llm/skills'
    $contextFile = Join-Path -Path $RepoRoot -ChildPath '.llm/context.md'
    $generateScript = Join-Path -Path $PSScriptRoot -ChildPath 'generate-skills-index.ps1'
    $indexFileName = 'index.md'
    $indexFile = Join-Path -Path $skillsDir -ChildPath $indexFileName
    $validCategories = @('Core', 'Performance', 'Feature')

    # Each entrypoint must contain this markdown link (path relative to the file).
    $agentEntrypoints = @(
        @{ Path = (Join-Path $RepoRoot 'AGENTS.md'); Link = '](./.llm/context.md)'; Label = 'AGENTS.md (Codex/OpenCode/nanocoder) entrypoint' }
        @{ Path = (Join-Path $RepoRoot 'CLAUDE.md'); Link = '](./.llm/context.md)'; Label = 'Claude Code entrypoint' }
        @{ Path = (Join-Path $RepoRoot '.cursorrules'); Link = '](./.llm/context.md)'; Label = 'Cursor agent entrypoint' }
        @{ Path = (Join-Path $RepoRoot '.github/copilot-instructions.md'); Link = '](../.llm/context.md)'; Label = 'Copilot agent entrypoint' }
    )

    $exitCode = 0

    # =============================================================================
    # 1. Required paths exist
    # =============================================================================
    Write-Info "Checking required paths..."
    foreach ($required in @(
            @{ Path = $skillsDir; Label = 'Skills directory' }
            @{ Path = $contextFile; Label = 'context.md' }
            @{ Path = $generateScript; Label = 'generate-skills-index.ps1' }
            $agentEntrypoints
        )) {
        if (-not (Test-Path -LiteralPath $required.Path)) {
            Write-ErrorMsg "$($required.Label) not found at: $($required.Path)"
            return 1
        }
    }

    # Authored skill files: every SKILL.md directly under .llm/skills/<name>/.
    $skillEntries = @()
    foreach ($dir in (Get-ChildItem -LiteralPath $skillsDir -Directory | Sort-Object Name)) {
        $skillFile = Join-Path -Path $dir.FullName -ChildPath 'SKILL.md'
        if (Test-Path -LiteralPath $skillFile) {
            $skillEntries += [PSCustomObject]@{ DirName = $dir.Name; Path = $skillFile }
        }
        else {
            Write-ErrorMsg "Skill directory '$($dir.Name)' has no SKILL.md."
            $exitCode = 1
        }
    }

    if ($skillEntries.Count -eq 0) {
        Write-ErrorMsg "No skills found under $skillsDir (expected .llm/skills/<name>/SKILL.md)."
        return 1
    }

    # =============================================================================
    # 2. SKILL.md frontmatter validity
    # =============================================================================
    Write-Host ""
    Write-Host "Validating SKILL.md frontmatter..." -ForegroundColor Blue

    $namePattern = '^[a-z0-9]+(?:-[a-z0-9]+)*$'
    $frontmatterFailures = @()
    $validCount = 0

    foreach ($entry in $skillEntries) {
        $content = [System.IO.File]::ReadAllText($entry.Path)
        $relativeName = "$($entry.DirName)/SKILL.md"

        if ($content -notmatch '(?m)(?s)\A---\r?\n(.*?\r?\n)---\r?\n') {
            $frontmatterFailures += "${relativeName}: missing '---' delimited YAML frontmatter"
            continue
        }
        $frontmatterText = $Matches[1]

        $nameValue = $null
        $descriptionValue = $null
        $categoryValue = $null
        foreach ($line in ($frontmatterText -split "\r?\n")) {
            if ($line -match '^name:\s*(.*)$') { $nameValue = $Matches[1].Trim().Trim('"', "'") }
            elseif ($line -match '^description:\s*(.*)$') { $descriptionValue = $Matches[1].Trim().Trim('"', "'") }
            elseif ($line -match '^\s{2,}category:\s*(.*)$') { $categoryValue = $Matches[1].Trim().Trim('"', "'") }
        }

        if ([string]::IsNullOrEmpty($nameValue)) {
            $frontmatterFailures += "${relativeName}: frontmatter is missing required 'name'"
        }
        elseif ($nameValue.Length -gt 64) {
            $frontmatterFailures += "${relativeName}: name is $($nameValue.Length) chars (max 64)"
        }
        # [regex]::IsMatch is case-SENSITIVE; -notmatch would let 'Valid-Skill' match
        # [a-z0-9-] because PowerShell's -match family ignores case by default.
        elseif (-not [regex]::IsMatch($nameValue, $namePattern)) {
            $frontmatterFailures += "${relativeName}: name '$nameValue' must be lowercase a-z/0-9 with single interior hyphens"
        }
        elseif ($nameValue -ne $entry.DirName) {
            $frontmatterFailures += "${relativeName}: name '$nameValue' must match parent directory name '$($entry.DirName)'"
        }

        if ($null -eq $descriptionValue -or [string]::IsNullOrWhiteSpace($descriptionValue)) {
            $frontmatterFailures += "${relativeName}: frontmatter is missing required 'description'"
        }
        elseif ($descriptionValue -match '^[|>]') {
            $frontmatterFailures += "${relativeName}: description must be a single inline line (block scalers '|'/'>' are not supported)"
        }
        elseif ($descriptionValue.Length -gt 1024) {
            $frontmatterFailures += "${relativeName}: description is $($descriptionValue.Length) chars (max 1024)"
        }
        else {
            $badChars = [regex]::Matches($descriptionValue, '[^\x00-\x7F]')
            if ($badChars.Count -gt 0) {
                $shown = ($badChars | ForEach-Object { $_.Value } | Select-Object -Unique) -join ' '
                $frontmatterFailures += "${relativeName}: description contains non-ASCII character(s) [$shown] (use ASCII: '-' not em-dash, straight quotes)"
            }
        }

        # Case-sensitive: 'core' must NOT pass as 'Core' - the generator groups
        # ordinally by exact string, so a case drift would drop the skill from the index.
        if ($null -ne $categoryValue -and -not ($validCategories -ccontains $categoryValue)) {
            $frontmatterFailures += "${relativeName}: unknown metadata.category '$categoryValue' (expected: $($validCategories -join ', '))"
        }

        $body = $content -replace '(?s)\A---\r?\n.*?\r?\n---\r?\n', ''
        if ([string]::IsNullOrWhiteSpace($body)) {
            $frontmatterFailures += "${relativeName}: skill body is empty"
        }
    }
    foreach ($entry in $skillEntries) {
        $hasFailure = $false
        foreach ($failure in $frontmatterFailures) {
            if ($failure.StartsWith("$($entry.DirName)/")) { $hasFailure = $true; break }
        }
        if (-not $hasFailure) { $validCount++ }
    }

    if ($frontmatterFailures.Count -gt 0) {
        foreach ($failure in $frontmatterFailures) {
            Write-ErrorMsg $failure
        }
        $exitCode = 1
    }
    else {
        Write-SuccessMsg "All $($skillEntries.Count) SKILL.md files have valid frontmatter"
    }

    # =============================================================================
    # 3 & 4. Index is up-to-date + deterministic
    # =============================================================================
    Write-Host ""
    Write-Host "Validating skills index ($indexFileName)..." -ForegroundColor Blue

    # Read CRLF->LF-normalized text for cross-platform-safe content comparison.
    $tempA = [System.IO.Path]::GetTempFileName()
    $tempB = [System.IO.Path]::GetTempFileName()
    try {
        if (-not (Invoke-IndexGeneration -FailureLabel "Generator failed writing expected index" -OutputPath $tempA -VerboseOutput:$VerboseOutput -RepoRoot $RepoRoot)) {
            return 1
        }
        if (-not (Invoke-IndexGeneration -FailureLabel "Generator failed on determinism re-run" -OutputPath $tempB -VerboseOutput:$VerboseOutput -RepoRoot $RepoRoot)) {
            return 1
        }

        # 3. Determinism: two independent runs must be byte-identical.
        $bytesA = [System.IO.File]::ReadAllBytes($tempA)
        $bytesB = [System.IO.File]::ReadAllBytes($tempB)
        $deterministic = ($bytesA.Length -eq $bytesB.Length)
        if ($deterministic) {
            for ($i = 0; $i -lt $bytesA.Length; $i++) {
                if ($bytesA[$i] -ne $bytesB[$i]) { $deterministic = $false; break }
            }
        }
        if (-not $deterministic) {
            Write-ErrorMsg "Generator is NON-deterministic: two runs produced different bytes."
            $exitCode = 1
        }
        else {
            Write-Info "Generator is deterministic (identical bytes across two runs)."
        }

        $expected = Get-NormalizedText -Path $tempA

        if (-not (Test-Path -LiteralPath $indexFile)) {
            if ($Fix) {
                if (-not (Invoke-IndexGeneration -FailureLabel "Generator failed while creating missing $indexFileName" -VerboseOutput:$VerboseOutput -RepoRoot $RepoRoot)) {
                    return 1
                }
                Write-SuccessMsg "Generated missing $indexFileName"
            }
            else {
                Write-ErrorMsg "$indexFileName does not exist. Run: pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1"
                return 1
            }
        }

        $current = Get-NormalizedText -Path $indexFile

        if (-not [string]::Equals($expected, $current, [System.StringComparison]::Ordinal)) {
            if ($Fix) {
                if (-not (Invoke-IndexGeneration -FailureLabel "Generator failed during -Fix" -VerboseOutput:$VerboseOutput -RepoRoot $RepoRoot)) {
                    return 1
                }
                Write-SuccessMsg "Regenerated $indexFileName"
                $current = Get-NormalizedText -Path $indexFile
            }
            else {
                Write-ErrorMsg "Skills index $indexFileName is out of date!"
                $expectedLines = $expected -split "`n"
                $currentLines = $current -split "`n"
                $maxLines = [Math]::Max($expectedLines.Count, $currentLines.Count)
                $shown = 0
                for ($i = 0; $i -lt $maxLines -and $shown -lt 5; $i++) {
                    $exp = if ($i -lt $expectedLines.Count) { $expectedLines[$i] } else { '(missing)' }
                    $cur = if ($i -lt $currentLines.Count) { $currentLines[$i] } else { '(missing)' }
                    if ($exp -ne $cur) {
                        Write-Host "  Line $($i + 1):" -ForegroundColor Yellow
                        Write-Host "    Expected: $exp" -ForegroundColor Green
                        Write-Host "    Current:  $cur" -ForegroundColor Red
                        $shown++
                    }
                }
                Write-Host "Run: pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1 (or this lint with -Fix)" -ForegroundColor Cyan
                $exitCode = 1
            }
        }
        else {
            Write-SuccessMsg "Skills index is up to date"
        }
    }
    finally {
        Remove-Item -LiteralPath $tempA, $tempB -Force -ErrorAction SilentlyContinue
    }

    # =============================================================================
    # 4b. Encoding contract on the committed index: UTF-8 no BOM, LF only
    # =============================================================================
    if (Test-Path -LiteralPath $indexFile) {
        $indexBytes = [System.IO.File]::ReadAllBytes($indexFile)
        $hasBom = $indexBytes.Length -ge 3 -and $indexBytes[0] -eq 0xEF -and $indexBytes[1] -eq 0xBB -and $indexBytes[2] -eq 0xBF
        $hasCr = $indexBytes -contains 0x0D
        if ($hasBom) {
            Write-ErrorMsg "$indexFileName has a UTF-8 BOM; it must be written without a BOM (cross-OS stability)."
            $exitCode = 1
        }
        if ($hasCr) {
            Write-ErrorMsg "$indexFileName contains CR (CRLF); it must use LF line endings."
            $exitCode = 1
        }
        if (-not $hasBom -and -not $hasCr) {
            Write-Info "$indexFileName encoding OK (UTF-8 no BOM, LF)."
        }
    }

    # =============================================================================
    # 5. context.md contract
    # =============================================================================
    Write-Host ""
    Write-Host "Validating context.md..." -ForegroundColor Blue

    $contextContent = [System.IO.File]::ReadAllText($contextFile)

    foreach ($staleMarker in @('<!-- BEGIN GENERATED SKILLS INDEX -->', '<!-- END GENERATED SKILLS INDEX -->')) {
        if ($contextContent.Contains($staleMarker)) {
            Write-ErrorMsg "context.md still contains a stale embedded-index marker: $staleMarker"
            Write-Host "The index lives in .llm/skills/index.md; remove the embedded block." -ForegroundColor Yellow
            $exitCode = 1
        }
    }

    if ($contextContent -notmatch '\]\(\./skills/index\.md\)') {
        Write-ErrorMsg "context.md must link to the generated index (a link to ./skills/index.md)."
        $exitCode = 1
    }

    $contextH1Count = ([regex]::Matches($contextContent, '(?m)^# (?!#)')).Count
    if ($contextH1Count -ne 1) {
        Write-ErrorMsg "context.md must have exactly one H1 (found $contextH1Count)."
        $exitCode = 1
    }

    # =============================================================================
    # 6. Frontend delegation
    # =============================================================================
    foreach ($entrypoint in $agentEntrypoints) {
        $entrypointContent = [System.IO.File]::ReadAllText($entrypoint.Path)
        if (-not $entrypointContent.Contains($entrypoint.Link)) {
            Write-ErrorMsg "$($entrypoint.Label) must delegate to .llm/context.md via $($entrypoint.Link)."
            $exitCode = 1
        }
    }

    # =============================================================================
    # Summary
    # =============================================================================
    Write-Host ""
    Write-Host ("=" * 60)
    if ($exitCode -eq 0) {
        Write-SuccessMsg "LLM instructions validation passed!"
    }
    else {
        Write-ErrorMsg "LLM instructions validation failed!"
    }
    Write-Host ("=" * 60)

    return $exitCode
}

# Main guard: dot-sourcing defines the functions without running (the lint
# tests validate in-process; a pwsh spawn per fixture case dominated the suite).
if ($MyInvocation.InvocationName -ne '.') {
    exit (Invoke-LlmInstructionsLint -Fix:$script:cliFix -VerboseOutput:$script:cliVerboseOutput -RepoRoot:$script:cliRepoRoot)
}
