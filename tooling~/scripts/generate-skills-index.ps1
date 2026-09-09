<#
.SYNOPSIS
    Generates the deterministic skills index at .llm/skills/index.md.

.DESCRIPTION
    Scans .llm/skills/*/SKILL.md (Agent Skills format) for frontmatter:

        ---
        name: skill-name
        description: Single-line ASCII description.
        metadata:
          category: Core
        ---

    and writes a stable, cross-OS-identical markdown index grouped by category
    (Core / Performance / Feature). Output is byte-for-byte deterministic on
    every OS:

      - Entries are sorted ORDINALLY (System.StringComparer::Ordinal), never with
        the culture-sensitive Sort-Object default.
      - No timestamp is emitted.
      - The file is written as UTF-8 WITHOUT a BOM and with LF line endings via
        [System.IO.File]::WriteAllText.

    Descriptions MUST be single-line and ASCII; tooling~/scripts/lint-llm-instructions.ps1
    enforces this so stray em-dashes / smart quotes can never reintroduce drift.

.PARAMETER OutputPath
    Destination file. Defaults to <repo>/.llm/skills/index.md. The linter passes
    a temp path here and compares the result to the committed file.

.PARAMETER Stdout
    Write the generated content to stdout instead of a file (for inspection).

.PARAMETER VerboseOutput
    Emit progress messages.

.EXAMPLE
    pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1
    pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1 -Stdout
#>
Param(
    [string]$OutputPath,
    [switch]$Stdout,
    [switch]$VerboseOutput,
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
}
$skillsDir = Join-Path -Path $RepoRoot -ChildPath '.llm/skills'
$indexFileName = 'index.md'

function Write-Info($msg) {
    if ($VerboseOutput) { Write-Host "[skills-index] $msg" -ForegroundColor Cyan }
}

# Authored skills: every SKILL.md directly under .llm/skills/<name>/.
function Get-SkillSourceFiles {
    param([Parameter(Mandatory = $true)][string]$Dir)
    return Get-ChildItem -LiteralPath $Dir -Directory | Sort-Object Name |
        ForEach-Object {
            $skillFile = Join-Path -Path $_.FullName -ChildPath 'SKILL.md'
            if (Test-Path -LiteralPath $skillFile) {
                [PSCustomObject]@{ Name = $_.Name; Path = $skillFile }
            }
        }
}

# Minimal YAML frontmatter reader for the subset this repo uses: top-level
# 'key: value' pairs plus one nested 'metadata:' block with string values.
# Returns $null when the file has no well-formed frontmatter block.
function Get-SkillFrontmatter {
    param([Parameter(Mandatory = $true)][string]$Path)

    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -lt 3 -or $lines[0] -ne '---') {
        return $null
    }

    $endIndex = -1
    for ($i = 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -eq '---') { $endIndex = $i; break }
    }
    if ($endIndex -lt 0) { return $null }

    $frontmatter = @{}
    $inMetadata = $false
    for ($i = 1; $i -lt $endIndex; $i++) {
        $line = $lines[$i]
        if ($line -match '^\S') {
            $inMetadata = $false
            if ($line -match '^([A-Za-z0-9_-]+):\s*(.*)$') {
                $key = $Matches[1]
                $value = $Matches[2].Trim()
                if ($value -eq '') {
                    # Block mapping (e.g. 'metadata:') - children follow indented.
                    $frontmatter[$key] = @{}
                    $inMetadata = $true
                }
                else {
                    $frontmatter[$key] = $value -replace '^(["''])(.*)\1$', '$2'
                }
            }
        }
        elseif ($inMetadata -and $line -match '^\s+([A-Za-z0-9_-]+):\s*(.*)$') {
            $key = $Matches[1]
            $value = ($Matches[2].Trim()) -replace '^(["''])(.*)\1$', '$2'
            if ($frontmatter['metadata'] -is [hashtable]) {
                $frontmatter['metadata'][$key] = $value
            }
        }
    }
    return $frontmatter
}

function Get-SkillIndexEntries {
    param([Parameter(Mandatory = $true)][string]$Dir)

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($skill in Get-SkillSourceFiles -Dir $Dir) {
        $frontmatter = Get-SkillFrontmatter -Path $skill.Path
        if ($null -eq $frontmatter) {
            Write-Info "No frontmatter in $($skill.Name)/SKILL.md - skipping"
            continue
        }

        $description = $null
        if ($frontmatter.ContainsKey('description') -and $frontmatter['description'] -is [string]) {
            $description = [string]$frontmatter['description']
        }

        $category = 'Feature'
        if ($frontmatter.ContainsKey('metadata') -and $frontmatter['metadata'] -is [hashtable] -and
            $frontmatter['metadata'].ContainsKey('category') -and $frontmatter['metadata']['category'] -is [string]) {
            $category = [string]$frontmatter['metadata']['category']
        }

        if ([string]::IsNullOrWhiteSpace($description)) {
            Write-Info "No description in $($skill.Name)/SKILL.md - skipping"
            continue
        }

        $entries.Add(
            [PSCustomObject]@{
                Name        = $skill.Name
                Description = $description
                Category    = $category
            }
        )
    }
    return , $entries.ToArray()
}

# Ordinal sort by skill name - culture-sensitive sorts reorder hyphens/digits per locale.
function Sort-SkillEntries {
    param([object[]]$Entries)

    $items = @($Entries)
    if ($items.Count -le 1) {
        return , $items
    }
    $names = [string[]]@($items | ForEach-Object { $_.Name })
    [Array]::Sort($names, [System.StringComparer]::Ordinal)
    $byName = @{}
    foreach ($entry in $items) { $byName[$entry.Name] = $entry }
    return , @($names | ForEach-Object { $byName[$_] })
}

function Get-SkillsIndexContent {
    param([Parameter(Mandatory = $true)][string]$Dir)

    $entries = Get-SkillIndexEntries -Dir $Dir

    $lf = "`n"
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append("<!-- DO NOT EDIT - generated by tooling~/scripts/generate-skills-index.ps1. -->$lf")
    [void]$sb.Append("<!-- Regenerate: pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1 -->$lf")
    [void]$sb.Append($lf)
    [void]$sb.Append("# Skills Index$lf")
    [void]$sb.Append($lf)
    [void]$sb.Append("Agent Skills ([SKILL.md format](https://agentskills.io)) for specific tasks. Invoke a skill by reading its SKILL.md when the task matches.$lf")

    $sections = @(
        [PSCustomObject]@{ Title = 'Core Skills (Always Consider)'; Category = 'Core' }
        [PSCustomObject]@{ Title = 'Performance Skills'; Category = 'Performance' }
        [PSCustomObject]@{ Title = 'Feature Skills'; Category = 'Feature' }
    )

    foreach ($section in $sections) {
        $items = Sort-SkillEntries (@($entries | Where-Object { $_.Category -eq $section.Category }))
        if ($items.Count -eq 0) { continue }

        [void]$sb.Append($lf)
        [void]$sb.Append("## $($section.Title)$lf")
        [void]$sb.Append($lf)
        [void]$sb.Append("| Skill | When to Use |$lf")
        [void]$sb.Append("| --- | --- |$lf")
        foreach ($item in $items) {
            [void]$sb.Append("| [$($item.Name)](./$($item.Name)/SKILL.md) | $($item.Description) |$lf")
        }
    }

    return $sb.ToString()
}

if (-not (Test-Path -LiteralPath $skillsDir)) {
    Write-Host "[skills-index] ERROR: Skills directory not found at: $skillsDir" -ForegroundColor Red
    exit 1
}

if (-not $OutputPath) {
    $OutputPath = Join-Path -Path $skillsDir -ChildPath $indexFileName
}

$content = Get-SkillsIndexContent -Dir $skillsDir

if ($Stdout) {
    [Console]::Out.Write($content)
}
else {
    # UTF-8 WITHOUT BOM + LF: identical bytes on every OS / PowerShell edition.
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($OutputPath, $content, $utf8NoBom)
    Write-Info "Wrote $OutputPath"
}
