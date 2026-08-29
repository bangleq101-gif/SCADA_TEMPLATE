[CmdletBinding()]
param(
    [string]$BundleRoot = $PSScriptRoot,

    [Parameter(Mandatory)]
    [string]$ProjectFile,

    [switch]$StartupSmoke,

    [ValidateRange(1, 30)]
    [int]$SmokeSeconds = 4
)

$ErrorActionPreference = 'Stop'
$resolvedBundleRoot = [System.IO.Path]::GetFullPath($BundleRoot)
$environmentScript = Join-Path $resolvedBundleRoot 'Test-ScadaEnvironment.ps1'
if (-not (Test-Path -LiteralPath $environmentScript -PathType Leaf)) {
    throw "Deployment environment script was not found: $environmentScript"
}

$shell = (Get-Process -Id $PID).Path
& $shell -NoProfile -File $environmentScript -BundleRoot $resolvedBundleRoot -ProjectFile $ProjectFile
if ($LASTEXITCODE -ne 0) {
    throw 'Deployment environment verification failed.'
}

$forbiddenFiles = Get-ChildItem -LiteralPath $resolvedBundleRoot -Recurse -Force -File | Where-Object {
    $_.Extension -in @('.cs', '.csproj', '.sln', '.db', '.log') -or
    $_.Name -eq '.env' -or
    $_.Name -like '*.secrets.json' -or
    $_.FullName -match '[\\/](bin|obj|TestResults)[\\/]'
}

if ($forbiddenFiles) {
    $relativeNames = $forbiddenFiles | ForEach-Object { [System.IO.Path]::GetRelativePath($resolvedBundleRoot, $_.FullName) }
    throw "Deployment contains forbidden source/runtime artifacts: $($relativeNames -join ', ')"
}

$requiredBundleFiles = @('Start-Scada.ps1', 'Test-ScadaEnvironment.ps1', 'Verify-Deployment.ps1', 'README.md', 'deployment-manifest.json')
foreach ($requiredFile in $requiredBundleFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedBundleRoot $requiredFile) -PathType Leaf)) {
        throw "Deployment bundle is incomplete: missing $requiredFile"
    }
}

Write-Host 'Deployment content verification passed.'

if ($StartupSmoke) {
    if (-not [System.IO.Path]::IsPathFullyQualified($ProjectFile)) {
        throw 'ProjectFile must be absolute for startup smoke.'
    }

    $resolvedProjectFile = [System.IO.Path]::GetFullPath($ProjectFile)
    $executable = Join-Path $resolvedBundleRoot 'app\Scada.App.exe'
    $quotedProjectFile = '"' + $resolvedProjectFile + '"'
    $process = Start-Process -FilePath $executable -ArgumentList @('--project-file', $quotedProjectFile) -PassThru -WindowStyle Hidden
    try {
        if ($process.WaitForExit($SmokeSeconds * 1000)) {
            throw "SCADA application exited during startup smoke with code $($process.ExitCode)."
        }
        Write-Host "WPF startup smoke passed ($SmokeSeconds seconds)."
    } finally {
        if (-not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

Write-Host 'Deployment verification passed.'
