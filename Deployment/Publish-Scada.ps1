[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidatePattern('^win-(x64|x86|arm64)$')]
    [string]$RuntimeIdentifier = 'win-x64',

    [switch]$SelfContained,

    [switch]$IncludeSymbols,

    [string]$OutputDirectory,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repositoryRoot 'Scada.App\Scada.App.csproj'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $deploymentName = if ($SelfContained) { "$RuntimeIdentifier-self-contained" } else { "$RuntimeIdentifier-framework-dependent" }
    $OutputDirectory = Join-Path $repositoryRoot "artifacts\deployment\$deploymentName"
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if ([string]::Equals($outputRoot.TrimEnd('\'), $repositoryRoot.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputDirectory cannot be the repository root.'
}

if (Test-Path -LiteralPath $outputRoot) {
    $existingItem = Get-ChildItem -LiteralPath $outputRoot -Force | Select-Object -First 1
    if ($null -ne $existingItem) {
        throw "OutputDirectory must be empty: $outputRoot"
    }
} else {
    New-Item -ItemType Directory -Path $outputRoot | Out-Null
}

$appOutput = Join-Path $outputRoot 'app'
New-Item -ItemType Directory -Path $appOutput | Out-Null

$publishArguments = @(
    'publish',
    $appProject,
    '--configuration', $Configuration,
    '--runtime', $RuntimeIdentifier,
    '--self-contained', $SelfContained.IsPresent.ToString().ToLowerInvariant(),
    '--output', $appOutput
)

if ($NoRestore) {
    $publishArguments += '--no-restore'
}

dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if (-not $IncludeSymbols) {
    Get-ChildItem -LiteralPath $appOutput -Filter '*.pdb' -File |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }
}

foreach ($file in @('Start-Scada.ps1', 'Test-ScadaEnvironment.ps1', 'Verify-Deployment.ps1', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $outputRoot $file)
}

$manifest = [ordered]@{
    FormatVersion = 1
    Application = 'Scada.App'
    Configuration = $Configuration
    RuntimeIdentifier = $RuntimeIdentifier
    SelfContained = $SelfContained.IsPresent
    IncludesSymbols = $IncludeSymbols.IsPresent
    PublishedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'deployment-manifest.json') -Encoding utf8

Write-Host "SCADA deployment bundle published: $outputRoot"
Write-Host 'Supply an external absolute project.json path when starting the application.'
