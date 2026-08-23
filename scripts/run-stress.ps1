param(
    [ValidateSet('RuntimeBaseline','HistorianHeavy','MqttHeavy','UiActive','CombinedWorstCase')]
    [string]$Profile = 'RuntimeBaseline',
    [int]$Repetitions = 1,
    [switch]$Smoke,
    [switch]$InstrumentationOff
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$deviceCount = if ($Smoke) { 5 } else { 50 }
$tagsPerDevice = 200
$warmup = if ($Smoke) { 2 } elseif ($Profile -eq 'CombinedWorstCase') { 120 } else { 60 }
$measurement = if ($Smoke) { 20 } elseif ($Profile -eq 'CombinedWorstCase') { 600 } else { 300 }
$powerMode = if ((Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue | Where-Object BatteryStatus -eq 2)) { 'AC' } else { 'AC-UNVERIFIED' }

for ($run = 1; $run -le $Repetitions; $run++) {
    $runId = '{0:yyyyMMdd-HHmmss}-{1}-r{2}' -f (Get-Date), $Profile, $run
    $output = Join-Path $repositoryRoot "artifacts\stress\$runId"
    dotnet run --project (Join-Path $repositoryRoot 'tools\Scada.Stress\Scada.Stress.csproj') -c Release --no-build -- `
        --profile $Profile `
        --devices $deviceCount `
        --tags-per-device $tagsPerDevice `
        --warmup-seconds $warmup `
        --measurement-seconds $measurement `
        --output $output `
        --instrumentation (-not $InstrumentationOff).ToString().ToLowerInvariant() `
        --power-mode $powerMode
    if ($LASTEXITCODE -ne 0) { throw "Stress run $run failed with exit code $LASTEXITCODE." }
}
