# SCADA V1 Project Structure

## Production dependency graph

```text
Scada.Core
   ▲       ▲              ▲
   │       │              │
Runtime  Drivers  Infrastructure
   ▲       ▲              ▲
   └───────┴──────────────┴── Scada.App
```

References:

```text
Scada.Core            → none
Scada.Runtime         → Scada.Core
Scada.Drivers         → Scada.Core
Scada.Infrastructure  → Scada.Core
Scada.App             → Scada.Core, Scada.Runtime, Scada.Drivers, Scada.Infrastructure
```

Milestone 5 keeps the product-project dependency graph unchanged. Historian orchestration is in `Scada.Runtime` and depends on Core contracts only; SQLite storage is composed by `Scada.App` through the `IHistoryStore` contract and implemented in `Scada.Infrastructure`.

`Scada.Runtime` is driver-neutral. It consumes `IPlcDriver` from `Scada.Core` and owns only the runtime-local resolver, lease, manager and polling workers. Concrete drivers are composed by `Scada.App` from `Scada.Drivers`.

## Main folders

```text
Scada.Core/
├── Common
├── Configuration
├── Devices
├── Drivers
├── History
└── Tags

Scada.Runtime/
├── Devices
├── Drivers
├── Engine
├── Historian
├── Polling
└── Tags

Scada.Drivers/
└── Simulator

Scada.Infrastructure/
├── Configuration
├── History
└── Persistence

Scada.App/
├── Controls
├── Resources
├── Services
├── ViewModels
└── Views

scripts/
└── run-scada.ps1
```

The App layer owns the hierarchical route model and workspace lifecycle. `NavigationService.CurrentRouteKey` is the authoritative active route; `ShellViewModel` derives tree selection from it. Navigation destination ViewModels implement the minimal `IWorkspaceLifecycle` contract. Monitoring owns TagCache subscriptions only while its workspace is active and rejects callbacks from older activation generations.

## Runtime polling components

- `IPlcDriverResolver` and `IPlcDriverLease` hide shared/per-device driver lifetime from orchestration.
- `DriverResolver` selects a registration by `DeviceDefinition.DriverType`.
- `DeviceManager` owns enabled-device worker lifecycle and immutable snapshot access.
- `DevicePollingWorker` owns one device lease, one scan scheduler and one device isolation boundary.
- `DevicePollingPlan` groups enabled tags into logical device + scan-group batches.
- `PollingRuntimeService` integrates `DeviceManager` with the host lifecycle.
- `DeviceRuntimeState` is mutable internal state; consumers receive `DeviceRuntimeSnapshot`.

## Test projects

```text
tests/Scada.Core.Tests
tests/Scada.Runtime.Tests
tests/Scada.Drivers.Tests
tests/Scada.Infrastructure.Tests
tests/Scada.App.Tests
```

`Scada.App.Tests` contains deterministic ViewModel/navigation/lifecycle and History Settings tests. There is no UI automation test in this milestone.

## Runtime data flow

```text
PLC or Simulator
      ↓
IPlcDriverResolver → IPlcDriverLease → IPlcDriver
      ↓
DeviceManager
      ↓
one async DevicePollingWorker / enabled device
      ↓
one scheduler / device
      ↓
device + Scan Group logical batch read
      ↓
TagEngine
      ↓
TagCache
      ↓
WPF subscriptions / Online Tag Monitor
```

## Milestone 5 historian flow

```text
TagCache subscription
        ↓
HistoryProfileEvaluator
        ↓
HistorySample normalization
        ↓
bounded HistorianQueue
        ↓
single background batch writer
        ↓
IHistoryStore
        ↓
SqliteHistoryStore → project-relative Data/history.db
```

The Historian never reads a PLC and never performs SQLite I/O in a TagCache callback. The callback performs evaluation and non-blocking `TryWrite` only. Source timestamps come from `TagValue.Timestamp`, wall-clock `RecordedAtUtc` comes from the historian `TimeProvider`, and monotonic timestamps are runtime-only for throttling and periodic scheduling. Queue overflow, invalid samples, abandoned samples and committed writes remain separate diagnostics.

TagCache remains the central runtime source. A disconnected device publishes `TagQuality.Disconnected`; a last-known value keeps its original PLC timestamp, while a tag without a valid value gets the failure transition timestamp.

## Milestone 4 Tag Manager flow

```text
explicit --project-file path
        ↓
ProjectPathResolver → ProjectConfigurationStore
        ↓
ProjectEditSession (startup/saved/working snapshots)
        ↓
TagManagerViewModel → virtualized TagManagerView
        ↓
save/revert, validation, TSV clipboard and CSV interchange
        ↓
selected-row TagCache quality observation only
```

The Tag Manager owns project editing in `Scada.App`; import data is prepared and conflict-checked before a single candidate mutation, and bulk edits apply only explicit field states. It does not read PLCs, change Runtime polling or provide live runtime reconfiguration. Runtime-affecting edits are marked restart-required. BuildRows seeds a quality snapshot with one `TryGet` per tag and creates no subscriptions; only the selected persisted tag may own one live subscription.

## Portable configuration

`Scada.App/appsettings.json` is copied to application output. The application sets its content root to `AppContext.BaseDirectory`, while project persistence uses an explicit absolute `--project-file` path. Historian SQLite is resolved relative to that canonical project document directory and is never created when Historian is disabled. `scripts/run-scada.ps1` resolves its project path from `$PSScriptRoot`, so running a copied template folder does not depend on the original repository or current working directory.
