<#
.SYNOPSIS
    Verifies that Runtime/Analyzers ships the exact binaries the Generator~
    sources build.

.DESCRIPTION
    Byte-compares each shipped analyzer payload against a fresh rebuild into
    a scratch directory (compare via the get-file-hash cmdlet), so the
    committed binaries can never drift from the sources beside them. Two
    payloads are verified: the source generator (shipped to consumers) and
    the repo-internal analyzers DLL (excluded from every distributed
    artifact, but still loaded by Unity inside this repository).

    With -TwoCheckout, additionally rebuilds the payloads from two extracted
    copies of Generator~ at distinct filesystem paths and byte-compares all
    artifacts, pinning that the shipped DLLs are reproducible across
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

$payloads = @(
    @{
        Project = "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators/WallstopStudios.DxCommandTerminal.SourceGenerators.csproj"
        Dll = "WallstopStudios.DxCommandTerminal.SourceGenerators.dll"
        Label = "generator"
    },
    @{
        Project = "Generator~/WallstopStudios.DxCommandTerminal.Analyzers/WallstopStudios.DxCommandTerminal.Analyzers.csproj"
        Dll = "WallstopStudios.DxCommandTerminal.Analyzers.dll"
        Label = "internal analyzers"
    }
)

foreach ($payload in $payloads) {
    $projectPath = Join-Path $RepoRoot $payload.Project
    if (-not (Test-Path $projectPath)) {
        Write-ErrorMsg "payload project not found: $projectPath"
        exit 1
    }
    $shippedPath = Join-Path $RepoRoot "Runtime/Analyzers/$($payload.Dll)"
    if (-not (Test-Path $shippedPath)) {
        Write-ErrorMsg "shipped $($payload.Label) payload not found: $shippedPath"
        exit 1
    }
    $payload.ShippedPath = $shippedPath
    $payload.ProjectPath = $projectPath
}

function Get-PayloadHash($path) {
    return (Get-FileHash $path -Algorithm SHA256).Hash
}

function Build-PayloadInto($projectPath, $outputDirectory) {
    dotnet build $projectPath -c Release --nologo /p:AnalyzerPayloadOutputDir=$outputDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-ErrorMsg "rebuilding $projectPath into $outputDirectory failed"
        exit 1
    }

    $builtPayload = Join-Path $outputDirectory (Split-Path $projectPath -Leaf)
    $builtPayload = $builtPayload -replace '\.csproj$', '.dll'
    if (-not (Test-Path $builtPayload)) {
        Write-ErrorMsg "rebuild did not produce a payload at $builtPayload"
        exit 1
    }

    return $builtPayload
}

function Compare-ShippedToRebuild($payload, $builtPayload) {
    $shippedHash = Get-PayloadHash $payload.ShippedPath
    $builtHash = Get-PayloadHash $builtPayload
    if ($shippedHash -ne $builtHash) {
        Write-ErrorMsg "shipped $($payload.Label) payload has drifted from its sources."
        Write-ErrorMsg "shipped: $shippedHash"
        Write-ErrorMsg "rebuilt: $builtHash"
        Write-ErrorMsg "Rebuild the payload: dotnet build $($payload.Project) -c Release"
        exit 1
    }

    Write-Host "[analyzer-payload] OK: shipped $($payload.Label) payload matches its sources" -ForegroundColor Green
}

$scratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "dxcommandterminal-analyzer-$(Get-Random)"
New-Item -ItemType Directory -Path $scratchDirectory | Out-Null
try {
    foreach ($payload in $payloads) {
        $builtPayload = Build-PayloadInto $payload.ProjectPath $scratchDirectory
        Compare-ShippedToRebuild $payload $builtPayload
    }

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

            $checkoutHashes = @{}
            foreach ($checkoutName in @("checkout-a", "checkout-b")) {
                $checkoutDirectory = Join-Path $twoCheckoutDirectory $checkoutName
                New-Item -ItemType Directory -Path $checkoutDirectory | Out-Null
                tar -xf $archivePath -C $checkoutDirectory
                if ($LASTEXITCODE -ne 0) {
                    Write-ErrorMsg "extracting Generator~ into $checkoutDirectory failed"
                    exit 1
                }

                foreach ($payload in $payloads) {
                    $checkoutProject = Join-Path $checkoutDirectory $payload.Project
                    if (-not (Test-Path $checkoutProject)) {
                        Write-ErrorMsg "extracted checkout is missing a payload project: $checkoutProject"
                        exit 1
                    }

                    $checkoutBuildDirectory = Join-Path $checkoutDirectory "build"
                    New-Item -ItemType Directory -Path $checkoutBuildDirectory | Out-Null
                    $checkoutPayload = Build-PayloadInto $checkoutProject $checkoutBuildDirectory
                    $key = "$($checkoutName)|$($payload.Dll)"
                    $checkoutHashes[$key] = Get-PayloadHash $checkoutPayload
                }
            }

            foreach ($payload in $payloads) {
                $first = $checkoutHashes["checkout-a|$($payload.Dll)"]
                $second = $checkoutHashes["checkout-b|$($payload.Dll)"]
                if ($first -ne $second) {
                    Write-ErrorMsg "the $($payload.Label) payload is not reproducible across checkout directories."
                    Write-ErrorMsg "checkout-a: $first"
                    Write-ErrorMsg "checkout-b: $second"
                    exit 1
                }

                $shippedHash = Get-PayloadHash $payload.ShippedPath
                if ($first -ne $shippedHash) {
                    Write-ErrorMsg "the checkout-directory rebuild does not match the shipped $($payload.Label) payload."
                    Write-ErrorMsg "shipped: $shippedHash"
                    Write-ErrorMsg "checkouts: $first"
                    exit 1
                }

                Write-Host "[analyzer-payload] OK: $($payload.Label) payload is byte-identical from two checkout directories" -ForegroundColor Green
            }
        }
        finally {
            Remove-Item -Recurse -Force $twoCheckoutDirectory -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Remove-Item -Recurse -Force $scratchDirectory -ErrorAction SilentlyContinue
}
