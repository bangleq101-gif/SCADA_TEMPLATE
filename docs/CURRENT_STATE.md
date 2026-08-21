
# Current State

Architecture V1 approved.

Current implementation milestone:

Milestone 1 — Foundation

Status:

Milestone 1 implemented — foundation complete; later milestones not started.

- Project goals defined.
- Architecture V1 approved.
- Git repository created.
- GitHub repository created.
- AGENTS.md established.
- docs/SCADA_ARCHITECTURE_V1.md established.
- Copy-folder portability defined as a core requirement.

The planned solution contains:

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

V1 uses:

```text
1 Runtime
n PLC
~10,000 tags target
```

Runtime must support:

- Batch Read
- Scan Groups
- central TagCache
- asynchronous device isolation
- subscription-based WPF updates

Currently there is no production SCADA source code.

The following have not yet been implemented:

- Solution/projects — implemented
- Runtime foundation — implemented
- TagCache — implemented
- Simulator driver foundation — implemented
- Real PLC drivers
- Tag Manager
- Historian
- MQTT
- Alarm
- Trend
- reusable HMI controls
- Machine Settings foundation placeholder
- deployment tooling

Milestone 1 verification:

- `dotnet restore Scada.sln` completed using the available package cache; vulnerability metadata could not be queried because the environment could not reach NuGet.
- `dotnet build Scada.sln --configuration Release` passed with zero errors.
- `dotnet test Scada.sln --configuration Release` passed: 9 tests, 0 failures.
- Runtime has no reference to WPF, `Scada.App` or `Scada.Drivers`.
- Simulator-specific code is contained in `Scada.Drivers/Simulator`.
- Configuration resolves from `AppContext.BaseDirectory` and `appsettings.json` is copied to output.
- No `Scada.App.Tests` project was created in Milestone 1.
- Runtime shutdown disconnects successfully connected devices through the driver contract.
- TagCache subscriber callback failures are isolated from cache updates and other subscribers.
- Foundation UI runtime/driver summary is read from `RuntimeOptions` rather than demo-only hard-coded values.

Final stabilization verification:

- `dotnet restore Scada.sln --ignore-failed-sources` passed.
- `dotnet build Scada.sln -c Release --no-restore` passed with zero warnings and zero errors.
- `dotnet test Scada.sln -c Release --no-build` passed: 11 tests, 0 failures.
- NU1900 may appear when NuGet vulnerability metadata is unavailable; security checks were not disabled.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. Do not jump ahead to MQTT, InfluxDB or other later milestones without explicit approval.
