[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackageSource,

    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),

    [string]$PackagesDirectory
)

$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathFullyQualified($PackageSource)) {
    throw 'PackageSource must be an absolute path to an offline NuGet feed.'
}

$resolvedPackageSource = [System.IO.Path]::GetFullPath($PackageSource)
if (-not (Test-Path -LiteralPath $resolvedPackageSource -PathType Container)) {
    throw "Offline package source was not found: $resolvedPackageSource"
}

$packageFiles = Get-ChildItem -LiteralPath $resolvedPackageSource -Filter '*.nupkg' -Recurse -File | Select-Object -First 1
if ($null -eq $packageFiles) {
    throw "Offline package source does not contain any .nupkg files: $resolvedPackageSource"
}

$manifestPath = Join-Path $resolvedPackageSource 'package-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Offline package source is missing package-manifest.json: $resolvedPackageSource"
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.FormatVersion -ne 1 -or $null -eq $manifest.Packages) {
    throw 'Offline package manifest format is unsupported or incomplete.'
}

$expectedFiles = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($package in $manifest.Packages) {
    if ([string]::IsNullOrWhiteSpace($package.File) -or [string]::IsNullOrWhiteSpace($package.Sha256)) {
        throw 'Offline package manifest contains an incomplete package entry.'
    }

    if (-not $expectedFiles.Add([string]$package.File)) {
        throw "Offline package manifest contains a duplicate file: $($package.File)"
    }

    $archivePath = Join-Path $resolvedPackageSource ([string]$package.File)
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        throw "Offline package archive listed in the manifest is missing: $($package.File)"
    }

    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, [string]$package.Sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Offline package hash mismatch: $($package.File)"
    }
}

$actualPackageFiles = Get-ChildItem -LiteralPath $resolvedPackageSource -Filter '*.nupkg' -Recurse -File
if ($manifest.PackageCount -ne $expectedFiles.Count -or $actualPackageFiles.Count -ne $expectedFiles.Count) {
    throw 'Offline package feed contents do not match the approved manifest count.'
}

foreach ($actualPackageFile in $actualPackageFiles) {
    if (-not $expectedFiles.Contains($actualPackageFile.Name)) {
        throw "Offline package feed contains an unapproved archive: $($actualPackageFile.Name)"
    }
}

$resolvedRepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$solution = Join-Path $resolvedRepositoryRoot 'Scada.sln'
if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Scada.sln was not found under RepositoryRoot: $resolvedRepositoryRoot"
}

if ([string]::IsNullOrWhiteSpace($PackagesDirectory)) {
    $PackagesDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'scada-offline-packages'
}
$resolvedPackagesDirectory = [System.IO.Path]::GetFullPath($PackagesDirectory)
New-Item -ItemType Directory -Path $resolvedPackagesDirectory -Force | Out-Null

$templatePath = Join-Path $PSScriptRoot 'NuGet.config.template'
$temporaryConfig = Join-Path ([System.IO.Path]::GetTempPath()) ('scada-offline-' + [Guid]::NewGuid().ToString('N') + '.config')
try {
    $escapedPackageSource = [System.Security.SecurityElement]::Escape($resolvedPackageSource)
    $config = (Get-Content -LiteralPath $templatePath -Raw).Replace('__SCADA_OFFLINE_SOURCE__', $escapedPackageSource)
    Set-Content -LiteralPath $temporaryConfig -Value $config -Encoding utf8

    dotnet restore $solution --configfile $temporaryConfig --packages $resolvedPackagesDirectory --no-http-cache
    if ($LASTEXITCODE -ne 0) {
        throw "Offline restore failed with exit code $LASTEXITCODE."
    }
} finally {
    Remove-Item -LiteralPath $temporaryConfig -Force -ErrorAction SilentlyContinue
}

Write-Host "Offline restore passed using feed: $resolvedPackageSource"
Write-Host "Packages directory: $resolvedPackagesDirectory"
