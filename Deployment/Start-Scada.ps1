[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProjectFile,

    [string]$BundleRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathFullyQualified($ProjectFile)) {
    throw 'ProjectFile must be an absolute path to project.json.'
}

$resolvedProjectFile = [System.IO.Path]::GetFullPath($ProjectFile)
if (-not (Test-Path -LiteralPath $resolvedProjectFile -PathType Leaf)) {
    throw "Project file was not found: $resolvedProjectFile"
}

$appDirectory = Join-Path ([System.IO.Path]::GetFullPath($BundleRoot)) 'app'
$executable = Join-Path $appDirectory 'Scada.App.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published SCADA executable was not found: $executable"
}

& $executable '--project-file' $resolvedProjectFile
exit $LASTEXITCODE
