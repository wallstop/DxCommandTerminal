<#
.SYNOPSIS
    Verifies that Runtime/Analyzers ships the exact binary the generator
    sources build.

.DESCRIPTION
    Byte-compares the shipped analyzer payload against a fresh rebuild into
    a scratch directory (compare via the get-file-hash cmdlet), so the
    committed binary can never drift from the sources beside it. Inspired by
    the unity-helpers' verify-analyzer-payload script.

.EXAMPLE
    ./tooling~/scripts/verify-analyzer-payload.ps1
#>
Param(
    [string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-ErrorMsg($msg) {
    Write-Host "[analyzer-payload] ERROR: $msg" -ForegroundColor Red
}

if (-not $RepoRoot) {
    $RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName
}

$generatorProject = Join-Path $RepoRoot "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/WallstopStudios.DxCommandTerminal.SourceGenerators.csproj"
$shippedPayload = Join-Path $RepoRoot "Runtime/Analyzers/WallstopStudios.DxCommandTerminal.SourceGenerators.dll"

if (-not (Test-Path $generatorProject)) {
    Write-ErrorMsg "generator project not found: $generatorProject"
    exit 1
}
if (-not (Test-Path $shippedPayload)) {
    Write-ErrorMsg "shipped analyzer payload not found: $shippedPayload"
    exit 1
}

$scratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "dxcommandterminal-analyzer-$(Get-Random)"
New-Item -ItemType Directory -Path $scratchDirectory | Out-Null
try {
    dotnet build $generatorProject -c Release --nologo /p:AnalyzerPayloadOutputDir=$scratchDirectory
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "rebuilding the generator into the scratch directory failed"
        exit 1
    }

    $builtPayload = Join-Path $scratchDirectory "WallstopStudios.DxCommandTerminal.SourceGenerators.dll"
    if (-not (Test-Path $builtPayload)) {
        Write-ErrorMsg "rebuild did not produce a payload at $builtPayload"
        exit 1
    }

    $shippedHash = (Get-FileHash $shippedPayload -Algorithm SHA256).Hash
    $builtHash = (Get-FileHash $builtPayload -Algorithm SHA256).Hash
    if ($shippedHash -ne $builtHash) {
        Write-ErrorMsg "shipped analyzer payload has drifted from the generator sources."
        Write-ErrorMsg "shipped: $shippedHash"
        Write-ErrorMsg "rebuilt: $builtHash"
        Write-ErrorMsg "Rebuild the payload: dotnet build Generator~/.../SourceGenerators.csproj -c Release"
        exit 1
    }

    Write-Host "[analyzer-payload] OK: shipped payload matches the generator sources" -ForegroundColor Green
}
finally {
    Remove-Item -Recurse -Force $scratchDirectory -ErrorAction SilentlyContinue
}
