# SCADA V1 Project Structure

## HMI controls and faceplates

`Scada.App/Hmi` contains the App-layer logical equipment contexts and faceplate host state. `Scada.App/Controls/Hmi` contains passive WPF controls; `Scada.App/Resources/Hmi` contains their copy-folder-contained styles and vendor-neutral fallback visuals. These controls consume TagCache only through their screen-owned context and do not read PLC data directly.

`Scada.Core/MachineSettings` contains persisted page/parameter definitions, canonical text conversion and pure validation. `Scada.App/ViewModels/MachineSettingsViewModel.cs` owns page drafts, transactional Apply and active-page logical TagCache observation; it does not own project persistence or PLC commands.

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

Milestone 6 keeps the product-project dependency graph unchanged. Historian orchestration remains in `Scada.Runtime` and depends on Core contracts only; SQLite and InfluxDB storage are composed by `Scada.App` through the `IHistoryStore` contract and implemented in `Scada.Infrastructure`. The official Influx client, transport, durable outbox and provider-specific diagnostics are not visible to Runtime.

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
├── run-scada.ps1
└── run-stress.ps1

tools/
└── Scada.Stress
```

`tools/Scada.Stress` is a non-product Release stress harness. It may compose all five product projects to exercise the real runtime and WPF paths, but no product project references it. Generated evidence is written only beneath ignored `artifacts/stress`.

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

One `HistorianCoordinator` owns all periodic deadlines and uses one schedule-change signal to wake when a newly scheduled deadline is earlier than the current one. Evaluator state is synchronized per tag, not by one global service lock. The next periodic deadline is scheduled from the evaluator result before queue capacity is checked, so an accepted-but-dropped sample does not lose its periodic schedule.

Historian startup uses bounded cancellation-aware `IHistoryStore.PreflightAsync` and continues recoverable storage work in the background. SQLite write connections independently apply `synchronous=NORMAL` and a finite `busy_timeout`; settings from the initialization connection are not reused implicitly.

TagCache remains the central runtime source. A disconnected device publishes `TagQuality.Disconnected`; a last-known value keeps its original PLC timestamp, while a tag without a valid value gets the failure transition timestamp.

## Milestone 6 InfluxDB provider flow

## Milestone 7 MQTT publisher flow

```text
TagCache → MqttRuntimeService → IMqttTransport → MQTTnet → Broker
```

The Runtime service owns bounded latest-state coalescing, profile evaluation and reconnect behavior. MQTTnet is an Infrastructure-only package; broker failures never issue PLC reads or block polling, Historian or WPF.

```text
TagCache
    ↓
HistoryProfileEvaluator → HistorianQueue → HistorianRuntimeService
    ↓
IHistoryStore
    ↓
BufferedInfluxHistoryStore
    ↓
SQLite Data/influx-buffer.db
    ↓
explicit IInfluxTransport
    ↓
InfluxDB 2.x
```

The outbox stores typed values and deterministic sample keys, allocates remote timestamps with per-destination/runtime/tag counters and tracks destination fingerprints without token material. Pending local rows are not merged into remote query results. Remote queries use exact recorded ticks with a widened rollback window and exact signed Influx nanosecond bounds. The local append commit is the write success boundary; a bounded one-bit signal wakes the remote worker, whose synchronization read/write/ack window shares a gate with current-destination clearing. Diagnostics scope counters to the current fingerprint and expose other rows as orphans. Retention is applied through an App-layer candidate service, while current/previous destination clearing remains explicit maintenance. A missing token leaves local buffering operational; a missing canonical project path is a structured store fault at preflight/initialization and does not stop PLC polling. Disabled Influx composition does not initialize the outbox.

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

`Scada.App/appsettings.json` is copied to application output. The application sets its content root to `AppContext.BaseDirectory`, while project persistence uses an explicit absolute `--project-file` path. Historian SQLite is resolved relative to that canonical project document directory and is never created when Historian is disabled. `scripts/run-scada.ps1` resolves its project path from `$PSScriptRoot`, so running a copied template folder does not depend on the original repository or current working directory. The Influx outbox is likewise resolved beneath the canonical project directory as `Data/influx-buffer.db`. Influx credentials are environment-variable references such as `env:SCADA_INFLUX_TOKEN`; the token value is not stored in project JSON, logged or shown in the UI.
