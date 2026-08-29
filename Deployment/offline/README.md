# Offline NuGet strategy

The repository intentionally does not commit `.nupkg` files, global NuGet
caches, runtime installers or credentials. An organization preparing an offline
factory installation must create and approve a local folder feed from the exact
package graph used by a verified online build.

## Prepare the feed on a connected staging machine

1. Verify the repository SHA and run the normal restore, Release build, tests
   and vulnerability audit.
2. Export every direct and transitive `.nupkg` already restored from trusted
   NuGet sources or an approved internal mirror:

   ```powershell
   .\Deployment\offline\Export-OfflinePackages.ps1 `
     -Destination 'E:\ApprovedNuGetFeed'
   ```

   The exporter reads all solution `project.assets.json` files, copies the exact
   package ID/version archives from the current global package folder and writes
   `package-manifest.json` with SHA-256 hashes. It refuses a non-empty
   destination so an old and new package set cannot be mixed silently.
3. Virus-scan and approve the package set and any .NET Desktop Runtime or
   self-contained runtime packs under the organization's software-supply policy.
4. Copy only the approved `.nupkg` files to removable media or an internal
   offline feed. Never place tokens or `NuGet.Config` credentials in the repo.

The current project uses central package versions in
`Directory.Packages.props`. The feed must also include transitive dependencies;
copying only those direct package names is insufficient.

## Restore without network sources

```powershell
.\Deployment\offline\Restore-Offline.ps1 `
  -PackageSource 'E:\ApprovedNuGetFeed' `
  -PackagesDirectory 'E:\SCADA-NuGetCache'
```

The script first requires `package-manifest.json`, verifies every SHA-256 hash
and rejects missing, duplicate or unapproved package archives. It then creates a
temporary NuGet configuration containing only the supplied folder feed
(`<clear />` removes configured online sources), restores `Scada.sln` and
deletes the temporary configuration. The package source must be absolute so the
procedure is explicit; no absolute path is persisted in repository files.

After restore, run the normal Release build and tests with network access
disabled. Publish either a framework-dependent bundle plus an approved matching
.NET Desktop Runtime installer, or a verified self-contained bundle. This
repository does not grant redistribution rights for Microsoft, vendor or symbol
library assets; licensing must be verified separately.

Updating any package, SDK or runtime invalidates the previous offline package
set and requires a new online audit, approval and offline-restore qualification.
