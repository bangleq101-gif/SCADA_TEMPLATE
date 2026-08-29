param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild,
    [Parameter(Mandatory)]
    [string]$ProjectFile
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'Scada.App\Scada.App.csproj'

if (-not [System.IO.Path]::IsPathFullyQualified($ProjectFile)) {
    throw 'ProjectFile must be an absolute path to project.json.'
}

$resolvedProjectFile = [System.IO.Path]::GetFullPath($ProjectFile)
if (-not (Test-Path -LiteralPath $resolvedProjectFile -PathType Leaf)) {
    throw "Project file was not found: $resolvedProjectFile"
}

$runArguments = @('--project', $appProject, '--configuration', $Configuration)
if ($NoBuild) {
    $runArguments += '--no-build'
}

$runArguments += '--'
$runArguments += '--project-file'
$runArguments += $resolvedProjectFile

dotnet run @runArguments
exit $LASTEXITCODE
