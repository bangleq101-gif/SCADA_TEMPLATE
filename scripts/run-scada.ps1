param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoBuild
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'project.json'
$appProject = Join-Path $projectRoot 'Scada.App\Scada.App.csproj'

$runArguments = @('--project', $appProject, '--configuration', $Configuration)
if ($NoBuild) {
    $runArguments += '--no-build'
}

$runArguments += '--'
$runArguments += '--project-file'
$runArguments += [System.IO.Path]::GetFullPath($projectFile)

dotnet run @runArguments
exit $LASTEXITCODE
