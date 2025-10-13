param(
  [string] $Root = '.',
  [switch] $VerboseOutput
)

$ErrorActionPreference = 'Stop'

function Write-VerboseLine($msg) {
  if ($VerboseOutput) { Write-Host $msg }
}

# Lints relative Markdown links. External links are validated by lychee in CI.
# - Flags relative links that point to non-existent files or directories.
# - Ignores mailto:, http(s):, absolute paths, and pure fragment links (#anchor).

$mdFiles = Get-ChildItem -Path $Root -Recurse -File -Include *.md, *.markdown |
  Where-Object { $_.FullName -notmatch '(?:\\|/)(node_modules|.git)(?:\\|/)' }

$broken = New-Object System.Collections.Generic.List[object]

$linkPattern = '\[(?:[^\]]+)\]\((?<target>[^)\s]+)(?:\s+"[^"]*")?\)'

foreach ($file in $mdFiles) {
  $lines = [System.IO.File]::ReadAllLines($file.FullName)
  for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    foreach ($m in [System.Text.RegularExpressions.Regex]::Matches($line, $linkPattern)) {
      $target = $m.Groups['target'].Value

      # Skip external and pure anchors
      if ($target -match '^(?:https?:|mailto:|tel:)' -or $target -match '^#') { continue }

      # Normalize and strip optional fragment
      $basePath = $target.Split('#')[0]
      if ([string]::IsNullOrWhiteSpace($basePath)) { continue }

      # Resolve relative to file directory
      # Only validate file-like links (with a dot in the last segment). Directory links are allowed in docs.
      $lastSeg = [System.IO.Path]::GetFileName($basePath.TrimEnd([char[]]@('/','\')))
      if ($lastSeg -notmatch '\.') { continue }

      $candidate = Join-Path -Path $file.DirectoryName -ChildPath $basePath

      # On case-sensitive CI we also want to catch case-only mismatches
      $exists = Test-Path -LiteralPath $candidate
      if (-not $exists) {
        $broken.Add([pscustomobject]@{
          File = $file.FullName
          Line = $i + 1
          Target = $target
        })
      } else {
        Write-VerboseLine "OK: ${($file.Name)}:$($i + 1) -> $target"
      }
    }
  }
}

if ($broken.Count -eq 0) {
  Write-Host 'Markdown link lint passed: all relative links resolve.'
  exit 0
}

Write-Host 'Broken relative Markdown links found:'
foreach ($b in $broken) {
  Write-Host " - $($b.File):$($b.Line) -> $($b.Target)"
}

Write-Error 'Relative Markdown link validation failed.'
exit 1
