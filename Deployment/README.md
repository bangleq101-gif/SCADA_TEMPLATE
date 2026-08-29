# SCADA deployment

This folder provides a portable Windows deployment workflow without adding a
separate product project or installer dependency.

## Publish

From the repository root:

```powershell
.\Deployment\Publish-Scada.ps1
```

The default output is a framework-dependent `win-x64` bundle under
`artifacts/deployment/`. The target PC must have the matching .NET Desktop
Runtime. Use `-SelfContained` only when the required runtime packs are already
available to restore/publish. Release symbols are excluded by default to avoid
shipping source-path metadata; use `-IncludeSymbols` only for an explicitly
controlled diagnostic package.

An output directory must be empty. The publish script never deletes an existing
directory. A bundle contains:

```text
bundle/
├── app/
├── Start-Scada.ps1
├── Test-ScadaEnvironment.ps1
├── Verify-Deployment.ps1
├── deployment-manifest.json
└── README.md
```

Project configuration and runtime data are deliberately outside `app/`.
Historian, Alarm and Influx buffer paths continue to resolve relative to the
explicit canonical project document directory.

## Validate a target PC

```powershell
.\Test-ScadaEnvironment.ps1 -ProjectFile 'C:\SCADA\Plant01\project.json'
```

The check validates Windows, required bundle files, the framework-dependent
.NET Desktop Runtime when applicable, valid project JSON, project-directory
writability and referenced `env:VARIABLE_NAME` secrets. Secret values are never
printed. Missing optional secret variables are warnings; add `-RequireSecrets`
to make them blocking.

## Start

```powershell
.\Start-Scada.ps1 -ProjectFile 'C:\SCADA\Plant01\project.json'
```

`ProjectFile` must be an existing absolute path. The launcher never searches
the source tree, current directory or parent folders.

## Verify a copied bundle

```powershell
.\Verify-Deployment.ps1 `
  -BundleRoot 'E:\SCADA-Deploy' `
  -ProjectFile 'E:\SCADA-Project\project.json' `
  -StartupSmoke
```

The verifier checks environment prerequisites, bundle completeness, forbidden
source/runtime artifacts and optionally performs a bounded WPF startup smoke.

This milestone does not include MSI/EXE installers, automatic updates, Windows
Service hosting or redistribution of third-party runtimes and licensed assets.
