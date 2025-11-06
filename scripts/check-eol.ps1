param(
    [string] $Root = '.',
    [switch] $VerboseOutput,
    [switch] $Fix
)

$ErrorActionPreference = 'Stop'

function Write-VerboseLine($msg) {
    if ($VerboseOutput) { Write-Host $msg }
}

# File extensions to check for EOL (CRLF) and no BOM
$extensions = @('md','markdown','json','asmdef','asmref','yml','yaml')

$badBom = New-Object System.Collections.Generic.List[string]
$badEol = New-Object System.Collections.Generic.List[string]
$fixedFiles = New-Object System.Collections.Generic.List[string]

$files = Get-ChildItem -Path $Root -Recurse -File |
    Where-Object {
        $ext = $_.Extension.TrimStart('.')
        $extensions -contains $ext
    } |
    Where-Object {
        # Skip typical vendor/build dirs
        $_.FullName -notmatch "(?:\\|/)(node_modules|.git)(?:\\|/)"
    }

foreach ($f in $files) {
    Write-VerboseLine "Checking: $($f.FullName)"
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)

    $hasBom = $false
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $hasBom = $true
    }

    # Verify all LF (0x0A) are preceded by CR (0x0D)
    $hasLfWithoutCr = $false
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        if ($bytes[$i] -eq 0x0A) {
            if ($i -eq 0 -or $bytes[$i - 1] -ne 0x0D) {
                $hasLfWithoutCr = $true
                break
            }
        }
    }

    if ($Fix -and ($hasBom -or $hasLfWithoutCr)) {
        # Decode as UTF-8, skip BOM bytes if present
        $startIndex = if ($hasBom) { 3 } else { 0 }
        $len = $bytes.Length - $startIndex
        $text = [System.Text.Encoding]::UTF8.GetString($bytes, $startIndex, $len)
        # Normalize line endings to CRLF
        $text = [System.Text.RegularExpressions.Regex]::Replace($text, "\r?\n", "`r`n")
        # Write back without BOM
        $enc = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($f.FullName, $text, $enc)
        $fixedFiles.Add($f.FullName)
        # Recompute to reflect post-fix status
        $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
        $hasBom = $false
        $hasLfWithoutCr = $false
    }

    if ($hasBom) { $badBom.Add($f.FullName) }
    if ($hasLfWithoutCr) { $badEol.Add($f.FullName) }
}

if ($badBom.Count -eq 0 -and $badEol.Count -eq 0) {
    if ($fixedFiles.Count -gt 0) {
        Write-Host "EOL/BOM issues were fixed in the following files:"
        $fixedFiles | ForEach-Object { Write-Host " - $_" }
    }
    Write-Host "EOL/BOM check passed: All checked files use CRLF and no BOM."
    exit 0
}

if ($badBom.Count -gt 0) {
    Write-Host "Files with UTF-8 BOM (should be without BOM):"
    $badBom | ForEach-Object { Write-Host " - $_" }
}

if ($badEol.Count -gt 0) {
    Write-Host "Files with LF-only line endings (should be CRLF):"
    $badEol | ForEach-Object { Write-Host " - $_" }
}

if ($fixedFiles.Count -gt 0) {
    Write-Host "Fixed files:"
    $fixedFiles | ForEach-Object { Write-Host " - $_" }
}

Write-Error "EOL/BOM validation failed. See lists above."
exit 1
