[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Destination,

    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
)

$ErrorActionPreference = 'Stop'

if (-not [System.IO.Path]::IsPathFullyQualified($Destination)) {
    throw 'Destination must be an absolute path.'
}

$resolvedDestination = [System.IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $resolvedDestination) {
    if (Get-ChildItem -LiteralPath $resolvedDestination -Force | Select-Object -First 1) {
        throw "Destination must be empty: $resolvedDestination"
    }
} else {
    New-Item -ItemType Directory -Path $resolvedDestination | Out-Null
}

$resolvedRepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$assetsFiles = Get-ChildItem -LiteralPath $resolvedRepositoryRoot -Filter 'project.assets.json' -Recurse -File |
    Where-Object { $_.FullName -match '[\\/]obj[\\/]project\.assets\.json$' }

if (-not $assetsFiles) {
    throw 'No project.assets.json files were found. Run an online trusted restore before exporting.'
}

$packageMap = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::OrdinalIgnoreCase)
$globalPackages = $null

foreach ($assetsFile in $assetsFiles) {
    $assets = Get-Content -LiteralPath $assetsFile.FullName -Raw | ConvertFrom-Json
    if ($null -eq $globalPackages -and $null -ne $assets.packageFolders) {
        $globalPackages = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    }

    foreach ($libraryProperty in $assets.libraries.PSObject.Properties) {
        if ($libraryProperty.Value.type -ne 'package') {
            continue
        }

        $separator = $libraryProperty.Name.LastIndexOf('/')
        if ($separator -le 0 -or $separator -eq $libraryProperty.Name.Length - 1) {
            throw "Unexpected package identity in $($assetsFile.FullName): $($libraryProperty.Name)"
        }

        $id = $libraryProperty.Name.Substring(0, $separator)
        $version = $libraryProperty.Name.Substring($separator + 1)
        $key = "$id/$version"
        if (-not $packageMap.ContainsKey($key)) {
            $packageMap.Add($key, [pscustomobject]@{ Id = $id; Version = $version })
        }
    }
}

if ([string]::IsNullOrWhiteSpace($globalPackages)) {
    $globalPackages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
}
$globalPackages = [System.IO.Path]::GetFullPath($globalPackages.Trim())

$manifestPackages = [System.Collections.Generic.List[object]]::new()
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($package in ($packageMap.Values | Sort-Object Id, Version)) {
    $idLower = $package.Id.ToLowerInvariant()
    $archive = Join-Path $globalPackages "$idLower\$($package.Version)\$idLower.$($package.Version).nupkg"
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        $missing.Add("$($package.Id)/$($package.Version)")
        continue
    }

    $destinationFile = Join-Path $resolvedDestination ([System.IO.Path]::GetFileName($archive))
    Copy-Item -LiteralPath $archive -Destination $destinationFile
    $hash = (Get-FileHash -LiteralPath $destinationFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $manifestPackages.Add([ordered]@{
        Id = $package.Id
        Version = $package.Version
        File = [System.IO.Path]::GetFileName($destinationFile)
        Sha256 = $hash
    })
}

if ($missing.Count -gt 0) {
    throw "The trusted global package folder is missing archives for: $($missing -join ', ')"
}

$manifest = [ordered]@{
    FormatVersion = 1
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    PackageCount = $manifestPackages.Count
    Packages = $manifestPackages
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $resolvedDestination 'package-manifest.json') -Encoding utf8

Write-Host "Exported $($manifestPackages.Count) packages to: $resolvedDestination"
