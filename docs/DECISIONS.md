
# Architecture Decisions

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
