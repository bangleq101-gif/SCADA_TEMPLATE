
# Architecture Decisions

## D65 — Bounded Online Tag Monitor delivery remains an App concern

Online Tag Monitor filters only static `RuntimeOptions.Tags` metadata and
owns subscriptions only for its current visible page: 250 by default and at
most 500. It subscribes before seeding from central `TagCache`, deduplicates
logical TagIds, disposes the set on deactivation/disposal and uses generation
guards both before enqueueing and inside the one coalesced latest-state WPF
Dispatcher callback. Page/filter changes use set-diff subscription ownership;
there is no monitor timer, task or PLC read per tag. The Dispatcher abstraction
and all presentation state remain in `Scada.App`; Runtime and TagCache contracts
remain unchanged.

## D64 — Tag engineering conversion is canonical Runtime work

`TagDefinition.SourceDataType` describes the raw value requested from a driver;
`DataType` describes the canonical value visible to TagCache consumers.
`TagValueTransformer` is a pure Core contract and `TagEngine` is the sole
Runtime conversion point: it validates declared raw shapes, applies finite
Scale/Offset only to numeric values and publishes canonical Good values once.
Boolean and String configurations require matching types and identity
Scale/Offset. An invalid Good transform publishes Bad and delegates
last-known-value/timestamp behavior to D-019; no consumer rescales, no PLC is
reread and no `Min`/`Max` clamp is inferred. Project schema v6 → v7 preserves
legacy identity semantics in memory by setting `SourceDataType` to the former
`DataType`; only explicit Save writes v7.

## D63 — Engineering device metadata is separate from Runtime polling

Milestone 13 adds `IDriverEngineeringProvider` in `Scada.Core` as a neutral
engineering contract because App needs provider metadata/validation and
concrete Drivers need to supply it without making Runtime know a concrete
driver. The contract exposes typed connection-option definitions, structured
validation and cancellation-aware read-only address candidates; it is not an
`IPlcDriver` replacement and has no PLC polling or write operation. The
`engineering.devices` workspace is composed in `Scada.App`, while
`ProjectEditSession` remains the sole project persistence/dirty authority.
`DeviceDefinition` continues to contain static configuration only and the
existing project schema v6 / `ConnectionOptions` container is reused. The
Simulator provider and deterministic fault scenarios remain under
`Scada.Drivers/Simulator`; `Scada.Runtime` still references `Scada.Core` only.

## D62 — M12 read-only operational health boundary

Milestone 12 uses exactly one Runtime-owned `RuntimeHealthService` with one production 1-second sampler, one timer and one immutable `RuntimeHealthSnapshot` publication per tick. It samples existing DeviceManager, Historian, optional provider diagnostics, MQTT, Alarm, TagCache counts and process telemetry; raw PLC scans, TagCache updates and individual service callbacks do not publish directly. The Runtime health model remains WPF-independent and sanitizes error text before App publication. Normal health precedence is `Faulted` > `Degraded` > `Starting` > `Unknown` > `Healthy`; `Stopping` is a separate shutdown override. App owns one shared presentation subscription, active-workspace generation guards and latest-state Dispatcher coalescing. `engineering.system`, `engineering.diagnostics`, Operation and the Shell status bar are read-only: they do not read PLCs, write project/runtime configuration, reconnect services or issue PLC/MQTT commands. Disabled TagCache counters are reported as unavailable, process health is Unknown when all process telemetry is unavailable, missing enabled-device snapshots are not Healthy, and M12 does not introduce thresholds, notifications, persistence, hot reload or M13 scope.

## D60 — M11 Alarm fan-out and snapshot/UI publication boundaries

AlarmRuntimeService retains a case-insensitive `TagId → matching MutableAlarm[]` index and a precomputed definition order after startup. TagCache callbacks evaluate only the matching array; they do not scan all configured definitions or sort/materialize the complete alarm list for an unchanged raw sample. When no semantic or diagnostic change occurs, the callback exits before snapshot materialization, full-snapshot comparison, publication or subscriber fan-out. Runtime-owned source sequence/timestamp state is updated for diagnostics, while public snapshot publication is driven by lifecycle, pending, availability/quality and diagnostic changes rather than one notification per raw sample. `AlarmSnapshot.LastSourceSequence` and `LastSourceTimestampUtc` therefore describe the latest materialized public snapshot; mutable runtime state may be newer after suppressed raw-only updates. `StaleTagUpdates` and `SubscriberExceptions` participate in the diagnostic comparison when a new snapshot is materialized. WPF Operation and Monitoring replace a pending snapshot with the latest snapshot and keep at most one Dispatcher work item per active generation; deactivation/disposal invalidates stale queued callbacks. This is a bounded implementation of the existing central-TagCache/snapshot invariant and does not add a Runtime→WPF dependency.

## D61 — M11 Alarm event source quality and SQLite schema v2

Alarm event quality is store-neutral nullable `TagQuality? SourceQuality`. Live transition and acknowledgement events carry the current known source/evaluation quality; an event with no known source sample remains null rather than fabricating `Good`. AlarmEvents schema v2 adds nullable `SourceQuality`; initialization performs an explicit v1-to-v2 column migration, preserves old rows as null, rejects newer schemas and keeps the project-relative SQLite boundary. The project schema v5-to-v6 migration remains separate and in-memory until explicit project Save.

## D59 — M11 deterministic verification is separate from the M10 benchmark

M11 acceptance requires controllable-TimeProvider tests for monotonic activation deadlines and wall-clock jumps; complete Alarm lifecycle and exact-instance ACK coverage; one-subscription-per-distinct-TagId, subscribe-before-seed, quality/sequence and zero-PLC-read checks; fail-closed startup-marker, trusted/untrusted recovery, definition-fingerprint reconciliation and SQLite path/schema coverage; and bounded lifecycle/shutdown isolation. An M11-specific bounded Alarm scale sanity may detect per-Alarm tasks/timers, per-definition subscriptions, unbounded queues/snapshot publication and severe contention, but it does not replace or redefine M10 qualification. SHA `402ee9d46f41489fee8912bbed57dc1388550658` remains the authoritative M10 benchmark.

## D58 — M11 PLC-read-only Alarm and trusted-checkpoint recovery

Milestone 11 consumes central TagCache values through one subscription per distinct logical TagId and performs no additional PLC reads or PLC/MQTT writes. Exact-instance acknowledgement mutates SCADA Alarm state only. Activation delays use monotonic `TimeProvider` timestamps while observable transition and journal timestamps use UTC wall time. Alarm SQLite storage defaults to `Data/alarms.db` resolved beneath the canonical `ProjectPath.DirectoryPath`; absolute paths, missing project paths and traversal outside that directory are rejected. Project migration v5 → v6 defaults `AlarmOptions.Enabled` to `false`. Persisted open instances are authoritative only after a gap-free clean drain and atomic trusted checkpoint. When persistence is enabled, durably and atomically marking every new session recovery-untrusted is a hard startup precondition: marker failure leaves Alarm Degraded/Faulted and forbids TagCache subscription, evaluation, deadlines, live-state mutation and memory-only fallback, while PLC polling may continue. Any queue gap, rejection, abandonment, write failure, crash or drain timeout prevents trusted restoration. Recovery additionally requires a compatible material Alarm-definition fingerprint. Untrusted, deleted, disabled or materially changed instances remain historical/orphaned and are never silently restored or given fabricated PLC Return/Closed events. Quality availability is exposed in snapshots and diagnostics without journaling every quality flap; automatic communication alarms remain deferred.

## D57 — M10 qualified baseline and no speculative optimization

Milestone 10 Phase A is qualified at SHA `402ee9d46f41489fee8912bbed57dc1388550658` under measurement contract `m10-phase-a-v3`, with 15/15 compatible runs across the five approved profiles. The evidence found no justified optimization candidate: correctness gates passed, throughput was stable and no bounded production hotspot was established. No speculative optimization is permitted on this evidence. Future performance changes must compare against a compatible workload and environment using the recorded baseline contract.

## D56 — M10 uses a non-product measurement harness and evidence gates

Stress qualification is implemented by the non-product `tools/Scada.Stress` harness. Product projects never reference the tool. The harness generates deterministic Simulator workloads, separates scan cadence from value-change intensity, consumes existing snapshots first and uses bounded aggregate measurements without TagId/EquipmentId metric dimensions. Raw results remain ignored artifacts. Same-environment fingerprints are required for automatic provisional regression verdicts; cross-machine results are observational. Phase A captures evidence only, and any optimization requires a later explicit gate.

## D55 — Machine Settings uses project candidates and read-only live observation

Machine Settings definitions are persisted as a `RuntimeOptions.MachineSettings` project-document container only; Runtime, polling, drivers, Historian and MQTT do not consume it. Parameter values are canonical text using the Core codec, with draft edit text owned by the App. Page Apply validates every draft before mutating the single `ProjectEditSession.WorkingProject` authority and Project Save/Revert retain their existing atomic/restart-required behavior. Optional `LiveTagId` values are logical TagCache identifiers only; active pages own deduplicated subscriptions and no Machine Settings path performs PLC writes. `IsReadOnly` is configuration safety, not authorization.

## D54 — App-layer HMI contexts and non-owning faceplates

Reusable HMI controls remain passive WPF controls in `Scada.App` and bind a logical `HmiEquipmentContext`, never PLC addresses or `ITagCache`. A screen-owned context deduplicates logical TagId subscriptions within its equipment instance, seeds from TagCache and owns lifecycle disposal. Faceplate hosts borrow the already-active context and never deactivate or dispose it. M8 is read-only and uses vendor-neutral packaged XAML fallbacks; external assets remain optional graphic sources subject to separate license review.

## D53 — MQTT publisher uses TagCache latest-state coalescing

Milestone 7 publishes selected tags from the central TagCache only. MQTT is publisher-only and uses one latest pending value per tag while a broker is unavailable; it is not a durable event historian. MQTTnet is isolated in Infrastructure behind Core transport contracts, while Runtime owns profile evaluation, coalescing and reconnect orchestration. MQTT Write, command subscriptions and PLC-write paths remain out of scope.

## D-001 — Copy-folder portability

`SCADA_TEMPLATE` is copied as a complete folder to create a new SCADA project. The copied repository must build and run independently. Project references are relative, and no internal SCADA source dependency may exist outside the repository.

## D-002 — V1 project boundaries

V1 uses the following five primary projects:

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

The architecture should not be split into excessive projects unless a future requirement justifies it.

## D-003 — Single runtime and target scale

V1 uses one Runtime and targets dozens of PLCs and approximately 10,000 tags. Distributed multi-runtime is intentionally deferred.

`Scada.Runtime` must not depend on WPF so it can potentially be separated later.

## D-004 — Central TagCache data flow

PLC data flows through:

```text
PLC → Driver → Polling → TagCache
```

UI, Alarm, Historian and MQTT consume TagCache values. They must not independently reread PLC data.

## D-005 — Polling architecture

Runtime uses Batch Read, Scan Groups, asynchronous device isolation and UI subscriptions. A disconnected PLC must not block other PLCs.

## D-006 — Configuration and historian profiles

Normal engineering uses the WPF Tag Manager rather than manually editing tag JSON. Historian configuration uses Digital, Analog, Fast Analog and Custom profiles, with advanced options hidden by default. SQLite and InfluxDB are supported storage targets.

## D-007 — Optional external services

SQLite is local application storage. MQTT Broker and InfluxDB Server are optional external services. Their failure must not stop PLC polling or normal SCADA operation. Historian writes use a queue/background writer.

## D-008 — MQTT behavior

Selected TagCache values may be published through MQTT. MQTT must not cause extra PLC reads, topics should normally be generated automatically, and MQTT Write is disabled by default.

## D-009 — WPF engineering and machine UI

Machine/process UI is designed manually in WPF/Visual Studio. The template supplies reusable Controls and Faceplates. Operation, Machine Settings, Monitoring and Engineering are distinct concepts; configuration UI must remain compact.

## D-010 — External symbols

External symbol libraries such as Symbol Factory are graphic sources only and must not become core architecture dependencies.

## D-011 — Versioned project knowledge

Architecture, current state, roadmap and decisions live inside the Git repository and are versioned together with the code.

## D-012 — Runtime-neutral polling

`Scada.Runtime` contains the generic `PollingRuntimeService`, `DeviceManager`, per-device polling workers and the runtime-local driver resolver. It depends only on `Scada.Core` contracts and contains no Simulator-specific type or logic. Simulator behavior is implemented exclusively in `Scada.Drivers/Simulator` and composed by `Scada.App`.

## D-013 — Static device configuration versus runtime state

`DeviceDefinition` contains static configuration only. Connection state, last error, last successful read and read statistics are represented by `Scada.Runtime.Devices.DeviceRuntimeState`.

## D-014 — Central build and package settings

`Directory.Build.props` provides shared compiler/framework settings. `Directory.Packages.props` centrally pins the small set of NuGet dependencies used by the solution. `coverlet.collector` is not included until coverage is explicitly used.

## D-015 — Portable runtime paths

Runtime configuration is resolved from `AppContext.BaseDirectory`; no working-directory or machine-specific absolute path is required. `Scada.App/appsettings.json` is copied to the application output.

## D-016 — Runtime shutdown and subscriber isolation

`PollingRuntimeService` delegates hosted lifecycle to `DeviceManager`. Device workers call the driver’s asynchronous `DisconnectAsync` during bounded shutdown. `TagCache` updates its state before notifying subscribers and isolates exceptions from individual callbacks so one subscriber cannot stop other notifications or the polling loop.

## D-017 — Runtime-local driver resolution and lease lifetime

`IPlcDriverResolver` belongs to `Scada.Runtime` because selecting and owning a driver instance is runtime orchestration, not a Core domain contract. `IPlcDriverLease` hides whether a registration returns a shared driver or creates a per-device driver. Workers reuse the acquired instance across reconnects and dispose only leases that own a per-device instance.

## D-018 — Per-device asynchronous polling isolation

`DeviceManager` creates one naturally asynchronous `DevicePollingWorker` per enabled device. Each worker owns one scan scheduler and groups tags into device + scan-group logical batches. There is no dedicated OS thread and no scheduler task per scan group.

## D-019 — Disconnect value and timestamp semantics

`TagQuality.Good` is the only result quality that advances a tag's canonical PLC value and PLC timestamp in TagCache. When a tag has a previous Good value, Bad, Uncertain and Disconnected transitions keep that last-good value and timestamp while updating quality. Before any Good value exists, non-Good results publish `null`; a disconnect uses its failure transition timestamp. Failure timing remains in DeviceRuntimeState/Snapshot and is not represented as a new PLC sample timestamp for a last-good value.

## D-020 — Cooperative cancellation and bounded shutdown

Runtime cancellation is cooperative; it cannot force-kill a non-cooperative I/O operation. Concrete drivers must honor cancellation and provide transport-level timeouts. A worker awaits `DisconnectAsync` directly and treats cancellation as complete only when the driver task has completed. DeviceManager bounds both normal shutdown and startup rollback, logs work that exceeds the budget, and does not dispose a lease while a non-cooperative operation remains in flight.

## D-021 — App-layer hierarchical navigation state

Milestone 3 keeps navigation in `Scada.App`. `NavigationService.CurrentRouteKey` and `CurrentViewModel` are the authoritative active navigation state. `NavigationItem` represents non-navigable groups and canonical navigable leaves; `ShellViewModel` derives selection from the current route rather than maintaining a second selected-route state.

## D-022 — Workspace lifecycle and active Monitoring ownership

Workspace ViewModels implement a minimal App-layer `IWorkspaceLifecycle` contract. Navigation owns deactivate/activate transitions. `MonitoringViewModel` owns TagCache subscriptions only while active, seeds rows from the cache on activation, disposes subscriptions on deactivation and uses an activation generation check to reject stale callbacks queued through the WPF Dispatcher.

## D-023 — Reusable WPF workspace layout

Milestone 3 uses a small `WorkspaceLayout` ContentControl with `Title` and `Description` dependency properties. It reuses inherited `Content` and a ResourceDictionary template; no external UI framework or separate styling project is introduced.

## D-024 — Whole project document authority

Milestone 4 treats the versioned `project.json` document as the authoritative project configuration for engineering edits. The Tag Manager loads and saves the complete `RuntimeOptions` document through `IProjectConfigurationStore`; it does not overlay partial JSON onto unrelated startup defaults.

## D-025 — Explicit portable project path

Project persistence requires an explicit absolute project path resolved by `ProjectPathResolver`. `Scada.App` accepts `--project-file`, and `scripts/run-scada.ps1` passes the canonical path explicitly. The application must not search parent folders, infer a source-tree path or silently fall back to an output-directory project file.

## D-026 — Project edit session and restart boundary

`ProjectEditSession` owns deep-cloned startup, saved and working snapshots. Save is atomic and updates the saved snapshot without mutating the startup snapshot. Runtime-affecting changes are marked restart-required; hot reload and live runtime reconfiguration are deferred.

## D-027 — Tag identity and metadata scope

Tag IDs and logical names are globally unique within a project. `AccessMode`, limits, units, history metadata and MQTT metadata are configuration fields only in Milestone 4. Tag scaling/offset semantics, live write commands and production history/MQTT services remain outside this milestone.

## D-028 — Deterministic configuration validation

Core owns the pure `RuntimeOptionsValidation` rules so Infrastructure persistence and App editing use the same validation source. Blocking issues reject load/save; unknown non-empty history or MQTT profiles are preserved with warnings, while enabled features with empty profiles are blocking errors.

## D-029 — Selected-only runtime quality observation

The Tag Manager observes runtime quality only for the currently selected persisted tag rows through one disposable TagCache subscription. It does not create one subscription per row, fan out subscriptions to the whole table or read PLCs directly. Subscription ownership ends on deactivation/disposal.

## D-030 — CSV/TSV interchange boundary

Tag interchange uses a shared deterministic table codec. TSV is the clipboard format and CSV is the file format; both preserve the complete supported `TagDefinition` metadata set, handle quoted/multiline fields and reject malformed input without silently truncating data.

## D-031 — Selected-tag subscription generations

Every logical selected-tag subscription lifetime receives a new generation when selection changes, the workspace is activated/deactivated or rows are rebuilt. Callback guards check both before Dispatcher enqueue and inside the queued callback, so an old A selection cannot update a later A selection after an A → B → A transition.

## D-032 — Editor options versus filter options

Configured device and scan-group collections used by editors never contain the `All` filter sentinel. Editable ComboBoxes preserve unknown existing references as text so validation can expose repair work without silently substituting or creating configuration entries. DataType editing uses an enum ComboBox.

## D-033 — Warning presentation boundary

`TagEditorRowViewModel` exposes blocking issues through `INotifyDataErrorInfo`; non-blocking warnings remain in a separate warning collection and summary. Errors-only filtering therefore excludes warning-only rows while the UI can display warning text independently.

## D-034 — Transactional import conflict policy

CSV and TSV import/paste parse into prepared candidates before any working-project mutation. A supplied unique Id is preserved, a missing Id receives a deterministic generated Id, and conflicting Ids/names are reported without suffixing or overwriting. The M4 UI explicitly confirms conflict-free apply or chooses append-non-conflicting/cancel for conflicted imports.

## D-035 — Explicit-state bulk editing

Bulk edits use `Unchanged`, `Mixed` and `Explicit` states rather than a null sentinel. Only explicit fields are applied to a cloned candidate project; the candidate is validated once before it replaces the working snapshot, preserving unrelated selected-tag fields.

## D-036 — Quality snapshot and destructive action boundaries

Tag Manager row construction seeds each tag from one central `TagCache.TryGet` snapshot without per-row subscriptions. A selected persisted tag owns at most one live subscription. Delete requires an App-layer confirmation adapter; cancellation cannot mutate the working project and no Runtime/TagCache mutation is performed.

## D-037 — Historian project schema migration

Milestone 5 introduces `ProjectDocumentSchema.CurrentVersion = 2`. A v1 project is migrated in memory by adding default `HistorianOptions` while preserving all existing runtime, tag and M4 metadata. Loading does not rewrite the source file; only an explicit Save writes v2. Newer, non-positive and malformed documents remain errors.

## D-038 — Historian profile catalog and configuration authority

The required Digital, Analog, FastAnalog and Custom profiles are centralized in Core and resolved through a case-insensitive registry. When `Scada:Historian:Profiles` is absent, programmatic defaults remain. When the section is present, the bound collection is authoritative, including an empty collection; defaults are cleared before binding and validation reports missing built-ins or other blocking issues.

## D-039 — Historian clocks and sample semantics

Historian samples preserve the normalized `TagValue.Timestamp` as `SourceTimestampUtc` and use a separate `RecordedAtUtc` wall clock. Monotonic `TimeProvider` timestamps are runtime-only for deadband minimum intervals, periodic due times and retry scheduling. TagCache remains authoritative for last-good value/timestamp behavior; quality transitions do not fabricate a new PLC timestamp.

## D-040 — Bounded historian queue and background writer

Runtime historian callbacks perform evaluation and non-blocking bounded `TryWrite` only. A single background consumer batches samples into one SQLite transaction. Invalid, full-queue, abandoned and committed samples use separate counters. Recoverable write failures have finite retries; an exhausted batch is abandoned while later queue items may continue. Permanent storage faults stop intake and transition Historian to Faulted without stopping polling.

## D-041 — SQLite history storage boundary

SQLite is a local Infrastructure concern behind the Core `IHistoryStore` contract. The store resolves `Data/history.db` under the canonical project document directory, rejects absolute/traversal paths, uses `PRAGMA user_version=1`, typed value columns, no `AUTOINCREMENT`, deterministic query ordering and per-batch connections/transactions. Disabled Historian does not create a database, and malformed/newer schemas are permanent runtime faults.

## D-042 — Historian lifecycle and status UI

`HistorianRuntimeService` is one singleton registered as an `IHostedService` before polling. It subscribes before seeding, starts background storage work without delaying polling, stops intake before shutdown drain and exposes an immutable runtime snapshot. History Settings edits the cloned project session, protects built-in names/deletion, marks saved changes restart-required and never hot-reloads runtime configuration.

## D-043 — Historian deadline wakeup and queue-drop semantics

Milestone 5 keeps one monotonic `HistorianCoordinator` for all tags. A single bounded schedule-change signal wakes the coordinator when a new or rescheduled deadline can become the earliest deadline; no timer or task is created per tag. Evaluator acceptance and its next periodic deadline are historian state decisions, so the deadline is scheduled before the bounded queue write. A full queue increments `DroppedSamples` without deleting the accepted periodic schedule.

## D-044 — Per-tag evaluation state and bounded preflight

Historian evaluator state is stored in concurrent per-tag entries with a small per-entry lock. Different tags do not serialize through one global service lock, while callback and periodic evaluation of the same tag remain serialized. `IHistoryStore.PreflightAsync(CancellationToken)` is cancellation-aware; Runtime applies a short startup budget and treats timeout/operational failure as recoverable so polling is not held behind storage. Initialization recovery uses capped exponential monotonic delays.

## D-045 — SQLite writer connection settings and package graph

SQLite initialization, batch writes and reads use centralized per-connection configuration. Every writer applies `synchronous=NORMAL` and a finite 250 ms `busy_timeout`; WAL/schema setup remains initialization-only. `Microsoft.Data.Sqlite` is pinned centrally at 10.0.11, resolving the SQLitePCLRaw family to 2.1.12 and removing the previously observed NU1903 warning without suppressing package auditing.

## D-046 — InfluxDB 2.x provider boundary

Milestone 6 supports InfluxDB 2.x through the official `InfluxDB.Client` 5.1.0 package. The package, transport adapter and outbox remain in `Scada.Infrastructure`; `Scada.Runtime` stays provider-neutral. SQLite remains the default provider and provider changes require the normal persisted-project restart boundary. Hot reload is not introduced.

## D-047 — Sequential project schema migration to v3

The project document schema advances to version 3 through explicit sequential v1 → v2 → v3 migrations. Loading migrates only the in-memory model; an explicit Save writes the current version. Unknown, non-positive or malformed schema versions remain errors.

## D-048 — Durable Influx outbox and sample identity

Influx pending samples are durably stored in a project-relative SQLite outbox with typed value columns. A deterministic SHA-256 sample key provides idempotency, capacity is bounded globally across destination fingerprints, and diagnostics/counters survive acknowledgement and buffer clearing. No plaintext token is stored in the outbox.

## D-049 — Destination and timestamp identity

Destination fingerprints include the normalized Influx URL, organization, bucket, measurement and point-schema version, but never the token. Remote nanosecond timestamps derive from `HistorySample.RecordedAtUtc` and are allocated monotonically per destination/runtime/tag. Queries filter exact recorded ticks and widen the remote time window to tolerate timestamp rollback without changing the returned exact range.

## D-050 — Explicit transport, buffering and failure isolation

The provider uses one explicit asynchronous transport/client path and does not enable a hidden SDK write queue. Rows are acknowledged only after an explicit successful write. Offline, configuration, timeout, 429, 5xx and generic 400 failures preserve local rows; only an explicitly point-specific 400 may be bounded-split and terminally reject an isolated poison row while later rows continue.

## D-051 — Secret, retention and maintenance boundaries

Influx tokens are referenced only through environment-variable references and are never logged or displayed. Retention changes are explicit maintenance actions, startup does not mutate remote retention, and current/previous destination buffer clearing is separate and confirmation-protected. History Settings Test Connection performs a candidate-only non-writing probe.

## D-052 — M6 stabilization boundaries

An Influx store may be composed without a canonical project path so a disabled or configuration-only provider does not fail during construction or create a fallback database. The path is required at local preflight/initialization and reports `PROJECT_PATH_REQUIRED`. The production adapter maps the official InfluxDB.Client exception/status model without reflection or response-text parsing; generic HTTP 400 remains retryable backlog, and point-specific isolation is permitted only when an injected transport explicitly confirms that classification. Connection, write and query operations use separate cancellation budgets. A durable SQLite append commit is the local write success boundary; no second cancellable diagnostics await follows it, and the worker signal is a bounded one-bit wake-up. Diagnostics are scoped to the active destination with orphan counts kept separate. Line-protocol-invalid samples are terminally rejected locally, exact Influx nanosecond bounds are used for allocation/query clamping, and current-buffer clearing shares a gate with remote read/write/ack synchronization. History Settings applies retention through a candidate-based App service and cancels/guards asynchronous commands across workspace deactivation.
