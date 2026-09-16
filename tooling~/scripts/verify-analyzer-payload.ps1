<#
.SYNOPSIS
    Verifies that Runtime/Analyzers ships the exact binary the generator
    sources build.

.DESCRIPTION
    Byte-compares the shipped analyzer payload against a fresh rebuild into
    a scratch directory (compare via the get-file-hash cmdlet), so the
    committed binary can never drift from the sources beside it. Inspired by
    the unity-helpers' verify-analyzer-payload script.

    With -TwoCheckout, additionally rebuilds the payload from two extracted
    copies of Generator~ at distinct filesystem paths and byte-compares all
    three artifacts, pinning that the shipped DLL is reproducible across
    checkout directories (ContinuousIntegrationBuild + Deterministic make
    the compiler's path embedding path-independent).

.EXAMPLE
    ./tooling~/scripts/verify-analyzer-payload.ps1
.EXAMPLE
    ./tooling~/scripts/verify-analyzer-payload.ps1 -TwoCheckout
#>
Param(
    [string]$RepoRoot,
    [switch]$TwoCheckout
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

function Get-PayloadHash($path) {
    return (Get-FileHash $path -Algorithm SHA256).Hash
}

function Build-PayloadInto($outputDirectory) {
    dotnet build $generatorProject -c Release --nologo /p:AnalyzerPayloadOutputDir=$outputDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "rebuilding the generator into $outputDirectory failed"
        exit 1
    }

    $builtPayload = Join-Path $outputDirectory "WallstopStudios.DxCommandTerminal.SourceGenerators.dll"
    if (-not (Test-Path $builtPayload)) {
        Write-ErrorMsg "rebuild did not produce a payload at $builtPayload"
        exit 1
    }

    return $builtPayload
}

$scratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "dxcommandterminal-analyzer-$(Get-Random)"
New-Item -ItemType Directory -Path $scratchDirectory | Out-Null
try {
    $builtPayload = Build-PayloadInto $scratchDirectory

    $shippedHash = Get-PayloadHash $shippedPayload
    $builtHash = Get-PayloadHash $builtPayload
    if ($shippedHash -ne $builtHash) {
        Write-ErrorMsg "shipped analyzer payload has drifted from the generator sources."
        Write-ErrorMsg "shipped: $shippedHash"
        Write-ErrorMsg "rebuilt: $builtHash"
        Write-ErrorMsg "Rebuild the payload: dotnet build Generator~/.../SourceGenerators.csproj -c Release"
        exit 1
    }

    Write-Host "[analyzer-payload] OK: shipped payload matches the generator sources" -ForegroundColor Green

    if ($TwoCheckout) {
        $twoCheckoutDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "dxcommandterminal-two-checkout-$(Get-Random)"
        New-Item -ItemType Directory -Path $twoCheckoutDirectory | Out-Null
        try {
            $archivePath = Join-Path $twoCheckoutDirectory "generator.tar"
            git archive --format=tar --output=$archivePath HEAD "Generator~"
            if ($LASTEXITCODE -ne 0) {
                Write-ErrorMsg "git archive of Generator~ failed"
                exit 1
            }

            $checkoutHashes = @()
            foreach ($checkoutName in @("checkout-a", "checkout-b")) {
                $checkoutDirectory = Join-Path $twoCheckoutDirectory $checkoutName
                New-Item -ItemType Directory -Path $checkoutDirectory | Out-Null
                tar -xf $archivePath -C $checkoutDirectory
                if ($LASTEXITCODE -ne 0) {
                    Write-ErrorMsg "extracting Generator~ into $checkoutDirectory failed"
                    exit 1
                }

                $checkoutProject = Join-Path $checkoutDirectory "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/WallstopStudios.DxCommandTerminal.SourceGenerators.csproj"
                if (-not (Test-Path $checkoutProject)) {
                    Write-ErrorMsg "extracted checkout is missing the generator project: $checkoutProject"
                    exit 1
                }

                $checkoutBuildDirectory = Join-Path $checkoutDirectory "build"
                New-Item -ItemType Directory -Path $checkoutBuildDirectory | Out-Null
                $savedProject = $generatorProject
                $generatorProject = $checkoutProject
                try {
                    $checkoutPayload = Build-PayloadInto $checkoutBuildDirectory
                }
                finally {
                    $generatorProject = $savedProject
                }

                $checkoutHashes += Get-PayloadHash $checkoutPayload
            }

            if ($checkoutHashes[0] -ne $checkoutHashes[1]) {
                Write-ErrorMsg "the generator payload is not reproducible across checkout directories."
                Write-ErrorMsg "checkout-a: $($checkoutHashes[0])"
                Write-ErrorMsg "checkout-b: $($checkoutHashes[1])"
                exit 1
            }

            if ($checkoutHashes[0] -ne $shippedHash) {
                Write-ErrorMsg "the checkout-directory rebuild does not match the shipped payload."
                Write-ErrorMsg "shipped: $shippedHash"
                Write-ErrorMsg "checkouts: $($checkoutHashes[0])"
                exit 1
            }

            Write-Host "[analyzer-payload] OK: payload is byte-identical from two checkout directories" -ForegroundColor Green
        }
        finally {
            Remove-Item -Recurse -Force $twoCheckoutDirectory -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Remove-Item -Recurse -Force $scratchDirectory -ErrorAction SilentlyContinue
}
