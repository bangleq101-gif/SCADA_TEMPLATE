# SCADA V1 Project Structure

## Milestone 16 deployment and offline portability

```text
Deployment/
├── Publish-Scada.ps1
├── Start-Scada.ps1
├── Test-ScadaEnvironment.ps1
├── Verify-Deployment.ps1
├── README.md
└── offline/
    ├── Export-OfflinePackages.ps1
    ├── Restore-Offline.ps1
    ├── NuGet.config.template
    └── README.md
```

The publish result separates `app/` binaries from the customer project. Both
source and published launchers require an explicit absolute `project.json` path,
so runtime data remains relative to the canonical project directory. Offline
package feeds and caches are generated outside Git from the exact restored
dependency graph and carry a SHA-256 manifest.

## HMI controls and faceplates

`Scada.App/Hmi` contains the App-layer logical equipment contexts and faceplate host state. `Scada.App/Controls/Hmi` contains passive WPF controls; `Scada.App/Resources/Hmi` contains their copy-folder-contained styles and vendor-neutral fallback visuals. These controls consume TagCache only through their screen-owned context and do not read PLC data directly.

`Scada.Core/MachineSettings` contains persisted page/parameter definitions, canonical text conversion and pure validation. `Scada.App/ViewModels/MachineSettingsViewModel.cs` owns page drafts, transactional Apply and active-page logical TagCache observation; it does not own project persistence or PLC commands.

## Milestone 15 screen metadata and composition

```text
Scada.App/Screens
├── ScreenDescriptor
├── ScreenHierarchyPath
└── ScreenCatalog
```

`ScreenCatalog` is an App-layer, compile-time catalog for the current WPF
application. It validates unique screen/route identities, required display
metadata and contiguous Module → Line → Machine path segments, then builds a
deterministically ordered `NavigationItem` tree. A catalog can filter entries
against routes actually registered by `NavigationService`, so optional
workspaces do not create dead menu items.

`NavigationItem.RouteKey` and `NavigationService.CurrentRouteKey` remain the
navigation/lifecycle authority. The optional `NavigationItem.Screen` metadata
is immutable display information only; it does not add a second route state,
permission system or runtime dependency. New screen ViewModels and XAML remain
statically registered in `Scada.App`; dynamic discovery and persisted screen
editing are intentionally deferred.

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

## Milestone 11 Alarm ownership

```text
Scada.Core/Alarms
├── Alarm configuration and lifecycle domain types
├── Alarm event/checkpoint records
├── deterministic definition fingerprint contract
└── IAlarmEventStore

Scada.Runtime/Alarms
├── AlarmRuntimeService
├── AlarmRuntimeState / AlarmRuntimeSnapshot / AlarmSnapshot
├── AlarmEvaluator
├── retained TagId → matching alarm index and stable definition order
├── one shared monotonic deadline coordinator
└── one bounded persistence coordinator with meaningful snapshot publication

Scada.Infrastructure/Alarms
├── AlarmDatabasePathResolver
└── SqliteAlarmEventStore

Scada.App
├── engineering.alarms
├── monitoring.alarms
├── Operation Alarm summary
└── latest-state Dispatcher coalescing for Alarm snapshots
```

Runtime-only snapshots, health state, evaluators and coordinators stay in `Scada.Runtime`; they are not duplicated in Core. Core contains only configuration, persisted/store-neutral domain records and the store abstraction needed by Runtime. Infrastructure implements SQLite behind that abstraction, and App composes the concrete store and WPF workspaces. No new product project or dependency direction is introduced.

M11 flow:

```text
PLC / Simulator
      ↓
existing Driver and Polling
      ↓
central TagCache
      ↓ one IDisposable subscription / distinct logical TagId
AlarmRuntimeService
├── immutable runtime snapshots → latest-state coalescing → Operation / Monitoring
└── bounded persistence coordinator → IAlarmEventStore
                                      ↓
                               SqliteAlarmEventStore
                                      ↓
                   ProjectPath.DirectoryPath/Data/alarms.db
```

Alarm never rereads a PLC. ACK targets an exact Alarm instance and mutates only SCADA Alarm/journal state. Activation-delay scheduling uses monotonic `TimeProvider` elapsed time; UTC wall time is reserved for observable transition and journal timestamps.

The Runtime alarm hot path resolves the callback TagId through the retained case-insensitive index and evaluates only matching definitions. Definition order is precomputed once; unchanged raw source sequence/timestamp updates remain available in runtime-owned mutable state but do not trigger full alarm-list materialization or one snapshot notification per raw sample. A semantic lifecycle/quality/availability/diagnostic change creates a new immutable snapshot. App-layer Operation and Monitoring views replace pending snapshots with the latest value and keep at most one Dispatcher work item per active generation; stale queued work is ignored after deactivation or disposal.

When Alarm persistence is enabled, a new session must be durably and atomically marked recovery-untrusted before any TagCache subscription, seed reconciliation, activation deadline or live evaluation. This marker is a hard startup precondition. If it cannot be committed, Alarm enters Degraded/Faulted without subscribing, evaluating, creating or mutating live Alarm state, and without memory-only fallback; PLC polling may continue. Only a gap-free clean drain plus a complete open-instance checkpoint and atomic continuity/session metadata commit can become trusted for the next startup. Crash, queue gap/drop/rejection, abandonment, write failure or drain timeout permanently disqualifies the session. Incompatible or untrusted persisted instances remain historical/orphaned.

## Milestone 12 operational health ownership

```text
Scada.Runtime/Health
├── RuntimeHealthService (one sampler/timer and immutable snapshot publication)
├── RuntimeHealthAggregator (PLC, TagCache, Historian, provider, MQTT, Alarm and process mapping)
├── RuntimeHealthSnapshot / RuntimeHealthState
├── ProcessTelemetry (CPU, working set and monotonic uptime)
└── RuntimeHealthSanitizer

Scada.App
├── RuntimeHealthPresentationService (one shared Runtime subscription)
├── SystemServicesViewModel / SystemServicesView
├── EngineeringDiagnosticsViewModel / EngineeringDiagnosticsView
├── Operation and Shell read-only health summaries
└── generation-guarded latest-state Dispatcher projection
```

M12 health is observational and read-only. It consumes existing immutable runtime/store snapshots and TagCache counts, never reads a PLC, changes polling, writes project/runtime configuration or issues PLC/MQTT commands. Normal state precedence is `Faulted` > `Degraded` > `Starting` > `Unknown` > `Healthy`; `Stopping` is a shutdown override. The App status bar renders compact PLC/History/MQTT/Runtime glyph-and-text indicators with accessibility names. `Scada.Runtime` has no WPF or App dependency; Infrastructure remains the owner of concrete storage diagnostics.

M12 flow:

```text
DeviceManager.DeviceSnapshots ─┐
Historian/MQTT/Alarm snapshots ├─→ RuntimeHealthService (one 1-second sampler)
optional store diagnostics ────┤             ↓
TagCache counts ───────────────┤       RuntimeHealthSnapshot
process telemetry ─────────────┘             ↓
                                  App shared presentation source
                                  ├─→ Operation / Shell status bar
                                  ├─→ engineering.system
                                  └─→ engineering.diagnostics
```

Only one health snapshot is materialized and published per sampler tick. TagCache callbacks and raw PLC scans do not publish health directly. Active App workspaces own at most one subscription and coalesce the latest snapshot through one Dispatcher work item per active generation.

## Milestone 13 engineering device ownership

```text
Scada.Core/Drivers
├── IDriverEngineeringProvider
├── DriverOptionDefinition / DriverOptionValueType
├── AddressBrowseCandidate
└── driver-neutral engineering validation contract

Scada.Drivers/Simulator
├── SimulatorEngineeringProvider
├── SimulatorFaultOptions / SimulatorFaultMode
└── deterministic read-only address candidates

Scada.App
├── EngineeringDevicesViewModel
├── DeviceEditorRowViewModel / DriverOptionEditorViewModel
├── EngineeringDevicesView
└── engineering.devices route
```

M13 reuses the existing persisted `DeviceDefinition.ConnectionOptions` and
project schema v6. `ProjectEditSession` remains the single working/saved/dirty
authority. The Core provider contract describes engineering metadata and
read-only browsing; it does not replace `IPlcDriver`, poll tags or write PLC
values. Simulator fault scenarios stay under `Scada.Drivers/Simulator` and
`Scada.Runtime` remains dependent on `Scada.Core` only. The App device editor
validates through the existing configuration boundary and never accesses a PLC
or `TagCache` directly.

## Milestone 14 tag engineering and monitoring ownership

```text
Scada.Core/Tags
├── TagDefinition: SourceDataType, canonical DataType, Scale and Offset
└── TagValueTransformer: pure declared-shape and engineering conversion contract

Scada.Runtime
├── DevicePollingPlan: requests SourceDataType from IPlcDriver
└── TagEngine: transforms Good raw values once before TagCache

Scada.App
├── TagManager: edits/imports/exports engineering metadata through ProjectEditSession
└── MonitoringViewModel: static metadata filter/page and active-page TagCache ownership
```

The driver returns a declared raw source value. `TagEngine` converts it to the
canonical engineering value exactly once before it enters `TagCache`; UI, HMI,
Historian, MQTT and Alarm therefore consume the same canonical value and never
perform a PLC reread or their own scale/offset transform. A transform failure
becomes `TagQuality.Bad` and central TagCache D-019 preserves a prior canonical
Good value and its source timestamp.

Online Tag Monitor has App-only presentation ownership. It builds a static
metadata-filtered page, owns only the page's distinct subscriptions while
active, subscribes before seeding and coalesces pending updates into one WPF
Dispatcher callback for the current generation. Its default page is 250 tags
and it never exceeds 500; no per-tag timer, task or UI-side polling exists.

## Main folders

```text
Scada.Core/
├── Alarms
├── Common
├── Configuration
├── Devices
├── Drivers
├── History
└── Tags

Scada.Runtime/
├── Alarms
├── Health
├── Devices
├── Drivers
├── Engine
├── Historian
├── Polling
└── Tags

Scada.Drivers/
└── Simulator

Scada.Infrastructure/
├── Alarms
├── Configuration
├── History
└── Persistence

Scada.App/
├── Controls
├── Resources
├── Screens
├── Services
├── ViewModels
└── Views

scripts/
├── run-scada.ps1
└── run-stress.ps1

Deployment/
├── publish, launch and environment verification scripts
└── offline package export/restore workflow

tools/
└── Scada.Stress
```

`tools/Scada.Stress` is a non-product Release stress harness. It may compose all five product projects to exercise the real runtime and WPF paths, but no product project references it. Generated evidence is written only beneath ignored `artifacts/stress`.

The App layer owns the hierarchical route model and workspace lifecycle. `NavigationService.CurrentRouteKey` is the authoritative active route; `ShellViewModel` derives tree selection from it. Navigation destination ViewModels implement the minimal `IWorkspaceLifecycle` contract. Monitoring owns only the active visible-page TagCache subscriptions, coalesces latest values through App Dispatcher ownership and rejects callbacks from older activation/page generations.

M15 adds an App-only screen catalog in front of this existing route model:

```text
static ScreenDescriptor registrations
        ↓
ScreenCatalog validation/order/hierarchy builder
        ↓
NavigationItem tree projection
        ↓
ShellViewModel / NavigationService
```

The catalog introduces no Core/Runtime/Drivers/Infrastructure references and no
per-screen timers, polling workers or TagCache subscriptions.

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

`Scada.App.Tests` contains deterministic ViewModel/navigation/lifecycle, WPF resource/render, History Settings and Runtime Health workspace tests. Full UI automation remains out of scope.

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
TagEngine (SourceDataType → canonical DataType, Scale, Offset)
      ↓
TagCache
      ↓
WPF subscriptions / Online Tag Monitor
```

Runtime health is a separate read-only observation flow from existing snapshots:

```text
DeviceManager / Historian / Influx diagnostics / MQTT / Alarm / TagCache counts / Process
                                      ↓
                         RuntimeHealthService (one sampler)
                                      ↓
                         immutable RuntimeHealthSnapshot
                                      ↓
                      App projection → WPF health surfaces
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

## Milestone 14 Tag engineering flow

```text
TagDefinition (SourceDataType, DataType, Scale, Offset)
    │
    ▼
DevicePollingPlan → DriverReadRequest(SourceDataType)
    │
    ▼
IPlcDriver raw result
    │
    ▼
TagEngine / TagValueTransformer
    │ canonical TagValue
    ▼
TagCache → UI / Alarm / Historian / MQTT / HMI
```

No product consumer applies a second engineering conversion. `Min` and `Max`
remain validation/presentation metadata in this milestone; they do not clamp a
canonical runtime value.

## Portable configuration

`Scada.App/appsettings.json` is copied to application output. The application sets its content root to `AppContext.BaseDirectory`, while project persistence uses an explicit absolute `--project-file` path. Historian SQLite is resolved relative to that canonical project document directory and is never created when Historian is disabled. `scripts/run-scada.ps1` and the published `Start-Scada.ps1` require that canonical absolute project path, so running a copied template or deployment bundle does not depend on the original repository or current working directory. The Influx outbox is likewise resolved beneath the canonical project directory as `Data/influx-buffer.db`. Influx credentials are environment-variable references such as `env:SCADA_INFLUX_TOKEN`; the token value is not stored in project JSON, logged or shown in the UI.

Milestone 11 Alarm persistence follows the same canonical project boundary: the default `Data/alarms.db` is resolved beneath `ProjectPath.DirectoryPath`, never beneath `AppContext.BaseDirectory`. Alarm persistence requires a canonical project path when enabled and rejects empty, rooted or out-of-project traversal paths. The AlarmEvents store uses SQLite schema v2 with nullable `SourceQuality`; initialization explicitly upgrades a v1 table with `ALTER TABLE`, preserves legacy rows as `NULL`, and rejects newer schema versions. Project schema v5 → v6 migration is in memory and defaults Alarm runtime enablement to false until an explicit project Save and later runtime restart.
