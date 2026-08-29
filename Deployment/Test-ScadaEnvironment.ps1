[CmdletBinding()]
param(
    [string]$BundleRoot = $PSScriptRoot,

    [Parameter(Mandatory)]
    [string]$ProjectFile,

    [switch]$RequireSecrets
)

$ErrorActionPreference = 'Stop'
$failures = [System.Collections.Generic.List[string]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()

function Write-CheckResult {
    param([string]$State, [string]$Message)
    Write-Host "[$State] $Message"
}

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    $failures.Add('SCADA deployment requires Windows.')
} else {
    Write-CheckResult 'PASS' 'Windows environment detected.'
}

$resolvedBundleRoot = [System.IO.Path]::GetFullPath($BundleRoot)
$appDirectory = Join-Path $resolvedBundleRoot 'app'
$requiredFiles = @(
    'Scada.App.exe',
    'Scada.App.dll',
    'Scada.App.deps.json',
    'Scada.App.runtimeconfig.json',
    'appsettings.json'
)

foreach ($requiredFile in $requiredFiles) {
    $candidate = Join-Path $appDirectory $requiredFile
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        Write-CheckResult 'PASS' "Found app/$requiredFile."
    } else {
        $failures.Add("Missing app/$requiredFile.")
    }
}

$runtimeConfigPath = Join-Path $appDirectory 'Scada.App.runtimeconfig.json'
if (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf) {
    try {
        $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
        $frameworks = @()
        if ($null -ne $runtimeConfig.runtimeOptions.framework) {
            $frameworks += $runtimeConfig.runtimeOptions.framework
        }
        if ($null -ne $runtimeConfig.runtimeOptions.frameworks) {
            $frameworks += $runtimeConfig.runtimeOptions.frameworks
        }

        $desktopFramework = $frameworks | Where-Object { $_.name -eq 'Microsoft.WindowsDesktop.App' } | Select-Object -First 1
        if ($null -ne $desktopFramework) {
            $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
            if ($null -eq $dotnetCommand) {
                $failures.Add(".NET Desktop Runtime $($desktopFramework.version) is required but dotnet was not found.")
            } else {
                $requiredVersion = [Version]$desktopFramework.version
                $runtimeLines = & dotnet --list-runtimes
                $compatibleRuntime = $runtimeLines | Where-Object {
                    if ($_ -notmatch '^Microsoft\.WindowsDesktop\.App\s+([^\s]+)') { return $false }
                    $installedVersion = [Version]$Matches[1]
                    return $installedVersion.Major -eq $requiredVersion.Major -and $installedVersion.Minor -ge $requiredVersion.Minor
                } | Select-Object -First 1

                if ($null -eq $compatibleRuntime) {
                    $failures.Add("Compatible Microsoft.WindowsDesktop.App $($requiredVersion.Major).$($requiredVersion.Minor) runtime was not found.")
                } else {
                    Write-CheckResult 'PASS' "Compatible .NET Desktop Runtime found: $compatibleRuntime"
                }
            }
        } else {
            Write-CheckResult 'PASS' 'Bundle is self-contained or does not declare a shared Windows Desktop framework.'
        }
    } catch {
        $failures.Add("Invalid app runtime configuration: $($_.Exception.Message)")
    }
}

if (-not [System.IO.Path]::IsPathFullyQualified($ProjectFile)) {
    $failures.Add('ProjectFile must be an absolute path.')
    $resolvedProjectFile = $null
} else {
    $resolvedProjectFile = [System.IO.Path]::GetFullPath($ProjectFile)
}

$projectText = $null
if ($null -ne $resolvedProjectFile -and (Test-Path -LiteralPath $resolvedProjectFile -PathType Leaf)) {
    try {
        $projectText = Get-Content -LiteralPath $resolvedProjectFile -Raw
        $projectDocument = $projectText | ConvertFrom-Json
        if ($null -eq $projectDocument.SchemaVersion -or $null -eq $projectDocument.Scada) {
            $failures.Add('Project document must contain SchemaVersion and Scada properties.')
        } else {
            Write-CheckResult 'PASS' "Project JSON is readable (schema $($projectDocument.SchemaVersion))."
        }
    } catch {
        $failures.Add("Project JSON is invalid: $($_.Exception.Message)")
    }

    $projectDirectory = Split-Path -Parent $resolvedProjectFile
    $probePath = Join-Path $projectDirectory ('.scada-write-probe-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [System.IO.File]::WriteAllText($probePath, 'SCADA deployment write probe')
        Remove-Item -LiteralPath $probePath -Force
        Write-CheckResult 'PASS' 'Project directory is writable for local Data storage.'
    } catch {
        if (Test-Path -LiteralPath $probePath) {
            Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
        }
        $failures.Add("Project directory is not writable: $projectDirectory")
    }
} elseif ($null -ne $resolvedProjectFile) {
    $failures.Add("Project file was not found: $resolvedProjectFile")
}

if ($null -ne $projectText) {
    $secretNames = [regex]::Matches($projectText, 'env:([A-Za-z_][A-Za-z0-9_]*)') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique

    foreach ($secretName in $secretNames) {
        $secretPresent = -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($secretName))
        if ($secretPresent) {
            Write-CheckResult 'PASS' "Secret environment reference is available: $secretName"
        } elseif ($RequireSecrets) {
            $failures.Add("Required secret environment variable is missing: $secretName")
        } else {
            $warnings.Add("Secret environment variable is not set: $secretName")
        }
    }
}

foreach ($warning in $warnings) {
    Write-CheckResult 'WARN' $warning
}

foreach ($failure in $failures) {
    Write-CheckResult 'FAIL' $failure
}

if ($failures.Count -gt 0) {
    Write-Host "Environment verification failed with $($failures.Count) blocking issue(s)."
    exit 1
}

Write-Host "Environment verification passed with $($warnings.Count) warning(s)."
exit 0
