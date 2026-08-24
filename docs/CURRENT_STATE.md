# Current State

Architecture V1 is approved.

Current implementation milestone:

Milestone 11 — Alarm System

Status:

Implemented and fully verified on `feature/milestone-11-alarm-system`; pending PR review and merge.

Milestone 7:

Completed and merged to `main` via PR #9.

Current M7 scope is publisher-only: MQTT consumes the central TagCache, applies bounded latest-state coalescing and publishes through an Infrastructure-only MQTTnet transport. MQTT Write, command subscriptions and PLC-write paths are not implemented.

Milestone 8:

Completed and merged to `main` via PR #10. It adds App-layer reusable read-only HMI controls and faceplate foundations without changing TagCache, polling, PLC reads or write paths.

Milestone 9:

Completed and merged to `main` via PR #11. PLC Write, MQTT Write, command/interlock and authorization frameworks are not implemented.

Milestone 10:

Completed and merged to `main` via PR #12 at merge commit `c16d9fdb1f75cb05a74b24143a330b5fc021ce82`.

The authoritative qualified Phase A benchmark remains `402ee9d46f41489fee8912bbed57dc1388550658` under measurement contract `m10-phase-a-v3`.

All 15 compatible qualification runs passed. No optimization was justified by measured evidence. The baseline is not a production SLA.

Milestone 11:

The approved Revision 1 Alarm architecture is implemented on the feature branch. The implementation is PLC-read-only, consumes central TagCache values, uses exact-instance SCADA-state acknowledgement, monotonic activation delays, bounded persistence coordination, project-relative SQLite storage and trusted-checkpoint recovery. Project schema v6 migrates existing v5 projects in memory with `AlarmOptions.Enabled = false`. M11 is not yet merged to `main`.

## Implemented in Milestone 1

- Solution and five product projects:
  - `Scada.Core`
  - `Scada.Runtime`
  - `Scada.Drivers`
  - `Scada.Infrastructure`
  - `Scada.App`
- Core test projects for Core, Runtime, Drivers and Infrastructure.
- `RuntimeId` and portable configuration foundation.
- Dependency Injection and centralized build/package configuration.
- Driver-neutral asynchronous batch-oriented `IPlcDriver` abstraction.
- Deterministic/smooth Simulator foundation under `Scada.Drivers/Simulator`.
- `TagEngine`, central `TagCache` and disposable tag subscriptions.
- Basic WPF Shell, navigation, design resources and page ViewModel registration.
- Online Tag Monitor foundation.
- Copy-folder portability foundation.

## Implemented in Milestone 2

- `RuntimeOptions.Polling` and configurable `ScanGroups`.
- Runtime-local driver resolver, registration and lease lifetime abstraction.
- Shared driver registration and per-device driver registration support.
- `DeviceManager` and one naturally asynchronous polling worker per enabled device.
- Per-device scan scheduler using planned due times.
- Device + scan-group logical batch reads.
- Multi-device isolation without dedicated OS threads per device.
- Connect/read/disconnect operation timeouts and cancellation propagation.
- Bounded reconnect backoff and driver-instance reuse across reconnects.
- `DeviceConnectionState` and immutable `DeviceRuntimeSnapshot` diagnostics.
- Read/failure counters, failure timing, scan timing and missed-cycle count.
- Central TagCache disconnect quality transitions with explicit value/timestamp semantics.
- Cooperative disconnect cancellation that clears in-flight state only after the driver task completes.
- Manager-level bounded shutdown and startup rollback cleanup, including lease disposal.
- Last-good TagCache value/timestamp preservation across Bad, Uncertain and Disconnected transitions.
- Late connect/read completion guards after worker shutdown cancellation.
- Configuration binding that avoids duplicating default scan groups during WPF startup.
- Simulator compatibility through the unchanged driver-neutral contract.

## Implemented in Milestone 3

- `Scada.App.Tests` WPF-compatible unit test project with navigation and lifecycle coverage.
- Minimal App-layer `IWorkspaceLifecycle` contract for deterministic workspace activation and deactivation.
- Hierarchical navigation with canonical route keys and `CurrentRouteKey` as the authoritative route state.
- Operation, Machine Settings, Monitoring and Engineering workspace foundations.
- Active Monitoring TagCache subscription ownership with idempotent activation/deactivation and generation guards for stale callbacks.
- Reusable `WorkspaceLayout` control using inherited `Content` plus `Title` and `Description` dependency properties.
- Compact WPF semantic colors, navigation styles, workspace/card styles and DataGrid foundation.
- Product-facing Shell and workspace text with development milestone terminology removed.

The V1 target remains:

```text
1 Runtime
n PLC
~10,000 tags
```

## Implemented in Milestone 4

- Canonical explicit project-file startup path with no source-tree or working-directory discovery.
- Versioned `project.json` document persistence with whole-document authority, schema validation, ordered tag collections and atomic save.
- Structured configuration validation for duplicate IDs/names, references, enums, ranges and profile metadata.
- Deep-cloned `ProjectEditSession` with dirty tracking, save/revert, validation reporting and restart-required semantics.
- Engineering Tag Manager route and workspace.
- Tag definition editing for identity, device/address, datatype, access mode, limits, units, enabled state, history metadata and MQTT metadata.
- Add, duplicate, delete, search, filter, sort, multi-select, bulk edit, TSV clipboard copy/paste and CSV import/export.
- Selected-row runtime quality refresh through the central `TagCache`; no direct PLC reads and no write command.
- Virtualized editable DataGrid and compact selected-tag detail editor.
- Portable `scripts/run-scada.ps1` launcher that passes an explicit absolute project path.
- Unique generation for every selected-tag subscription lifetime, including selection re-entry and activation races.
- Separate configured editor options from `All`-inclusive filter options; unknown device/scan-group references remain visible for repair.
- Blocking errors separated from non-blocking warnings in row validation and the Errors-only filter.
- Transactional CSV/TSV import preparation with explicit conflict decisions, deterministic generated IDs when the optional Id column is absent, and no silent identity renaming.
- Explicit-state bulk editing for enabled/device/datatype/scan group/access/history/MQTT fields, with one candidate validation pass.
- Delete confirmation abstraction and one-pass per-row quality snapshot seeding without subscription fan-out.

## Implemented in Milestone 5

- Central `HistorianOptions`, approved Digital/Analog/FastAnalog/Custom profile defaults, case-insensitive profile registry and authoritative configuration binding.
- Profile validation for required built-ins, duplicates, invalid intervals/deadbands, unknown tag profiles and incompatible tag data types.
- `ProjectDocumentSchema.CurrentVersion = 2` with explicit v1-to-v2 in-memory migration; v1 documents are not rewritten until an explicit Save.
- Core-neutral `HistorySample`, bounded `HistoryQuery` and `IHistoryStore` contracts.
- Strict history value normalization, non-finite Double rejection, source/recorded/monotonic clock separation and overflow-safe Int64 deadband evaluation.
- Single monotonic periodic coordinator, subscribe-before-seed with TagValue.Sequence deduplication, bounded `Channel<HistorySample>` and separate rejected/dropped/abandoned/written counters.
- One coordinator wake/change signal for earlier deadlines and retained periodic scheduling even when an accepted sample is dropped because the queue is full.
- Per-tag evaluator synchronization instead of one global Historian evaluation lock; same-tag callbacks and periodic evaluations remain serialized.
- `HistorianRuntimeService` with Disabled/Starting/Healthy/Degraded/Faulted/Stopping states, background initialization/writes, bounded retry, clean intake shutdown and queue drain.
- Cancellation-aware bounded storage preflight and capped exponential initialization retry backoff; a recoverable preflight does not delay polling startup.
- `Microsoft.Data.Sqlite` 10.0.11 storage under `Scada.Infrastructure/History` with schema version 1, typed value columns, deterministic query ordering, path traversal protection, per-connection write PRAGMA configuration and malformed/newer-schema fault handling.
- Hosted-service ordering and singleton identity: Historian starts before polling while polling remains independent of historian storage failures.
- Engineering `engineering.history` History Settings workspace with profile editing, protected built-ins, rename validation, shared queue-capacity warnings, save/validation feedback, advanced global settings, runtime snapshot/status and restart-required save semantics.

## Implemented in Milestone 6

- Provider-neutral `HistoryStorageProvider` selection with SQLite remaining the default and validated `InfluxDbOptions` for InfluxDB 2.x.
- Sequential project schema migration v1 → v2 → v3, with migration in memory and explicit Save as the upgrade boundary.
- Deep-cloned and compared Influx configuration in `ProjectEditSession`.
- Official `InfluxDB.Client` 5.1.0 integration isolated to `Scada.Infrastructure`; `Scada.Runtime` and `TagCache` remain unchanged.
- Environment-only token references, portable project-relative `Data/influx-buffer.db` resolution and no token logging or display.
- Durable SQLite-backed Influx outbox with typed sample columns, deterministic SHA-256 sample keys, destination fingerprints, remote timestamp counters and global bounded capacity.
- One explicit asynchronous transport/client path with durable retry, reconnect/backoff, offline/configuration handling, generic 400 backlog preservation and bounded split/isolation only when a transport explicitly confirms a deterministic point-specific rejection; the production InfluxDB.Client adapter never invents point-specific classification.
- Official InfluxDB.Client exception/status mapping without reflection or response-text parsing, with separate connection, write and query operation budgets.
- Nullable project-path composition for disabled/configuration-only Influx startup; the local outbox reports `PROJECT_PATH_REQUIRED` at bounded preflight/initialization rather than resolving a fallback path.
- Durable append commit as the local write success boundary and a coalesced one-bit worker signal that avoids duplicate wake-up accumulation.
- Destination-scoped persisted diagnostics with current-destination pending metrics and explicit orphaned-destination counts.
- Strict line-protocol validation: tag control characters and string newlines are terminal local rejections before remote synchronization.
- Exact signed InfluxDB nanosecond bounds, clamped query windows and a remote-sync gate that serializes current-buffer clearing with read/write/ack synchronization.
- Candidate-only retention application through an App-layer `IHistoryRetentionManager`; active runtime settings are not used as a substitute for the working project candidate.
- Async History Settings maintenance commands with cancellation, overlap prevention, bounded operation lifetimes and activation-generation guards for late completions.
- Remote-only history queries with exact recorded-tick filtering and widened rollback windows; no local pending samples are merged into query results.
- History Settings provider selection, candidate-only Test Connection, retention and separately confirmed buffer maintenance actions.

## Implemented in Milestone 9

- Versioned project-persisted Machine Settings pages, groups and typed parameters with schema v4 → v5 in-memory migration.
- Pure Core canonical value codec and validation for Boolean, Integer, Decimal and String values, culture-aware editor conversion and invariant persistence.
- Transactional page Apply: all editable drafts validate/normalize before any project value changes, with a single dirty transition after success.
- `ProjectEditSession` remains the sole clone/comparison/save/revert/dirty authority for Machine Settings.
- `machine-settings.overview` internal hierarchical page/group navigation with typed WPF editor templates, validation, read-only/hidden behavior and accessible semantic labels.
- A flattened group-header/editor row composition preserves visible `ParameterGroup` semantics while one recycling `ListBox` owns parameter virtualization.
- Non-destructive UI rebuilds and successful Save preserve unapplied drafts; explicit page/project Revert deterministically discards the corresponding drafts.
- Read-only logical `LiveTagId` values sourced only from the central TagCache, with enabled-catalog resolution, active-page deduplication, subscribe-before-seed and generation-guarded UI updates.
- Deterministic lifecycle coverage for deactivation/disposal during subscription acquisition, stale queued callbacks and page replacement.

## Implemented in Milestone 11

- Project schema v6 with in-memory v5 → v6 migration, Alarm disabled by default, and atomic clone/comparer/validation/save/revert/restart-required integration through `ProjectEditSession`.
- Store-neutral Alarm configuration, lifecycle, event, exact-instance ACK, fingerprint, checkpoint/recovery and `IAlarmEventStore` contracts in `Scada.Core`.
- `AlarmRuntimeService` with Good-only TagCache evaluation, stale-sequence suppression, one subscription per distinct enabled logical TagId and subscribe-before-seed behavior.
- `DigitalEquals`, High, HighHigh, Low and LowLow evaluators with deterministic deadband and `ReturnedUnacknowledged` state semantics.
- One shared monotonic activation-delay coordinator using `TimeProvider` timestamps; observable transition, ACK and journal timestamps use UTC wall time only.
- Exact-instance ACK, stale-safe/idempotent ACK, ACK-all through the same per-instance path and deterministic ACK/current-value race isolation.
- Bounded Alarm persistence channel and batch coordinator with separate rejected, dropped, abandoned and write-failure diagnostics.
- Fail-closed durable recovery-untrusted startup marker: marker failure creates no TagCache subscription, deadline or live Alarm lifecycle state.
- Gap-free clean-drain trusted checkpoints, compatible material-definition recovery, untrusted recovery diagnostics and orphaned/incompatible instance accounting.
- Project-relative `Data/alarms.db` SQLite event/open-instance store with rooted/traversal path rejection, atomic session metadata and corrupt/newer-schema handling.
- `engineering.alarms`, `monitoring.alarms`, read-only Alarm journal query and compact Operation Alarm summary in WPF.
- Deterministic clock/state/ACK/quality/sequence/recovery/persistence/lifecycle/UI tests and a bounded sanity using 10,000 project tags, 2,000 Alarm definitions and 500 distinct Alarm TagIds.

## Verified

- `dotnet restore Scada.sln --ignore-failed-sources` — PASS.
- `dotnet build Scada.sln -c Release --no-restore` — PASS with 0 warnings and 0 errors.
- M11 feature-branch restore and Release build — PASS with 0 warnings and 0 errors.
- M11 full test suite — PASS; 382 tests, 0 failures (138 App, 114 Runtime, 36 Core, 3 Drivers, 64 Infrastructure, 27 Stress).
- M11 vulnerability audit — PASS; no vulnerable direct or transitive package was reported.
- M11 WPF startup smoke — PASS; the application remained running with Alarm resources, DI and routes loaded.
- M11 fresh copy-folder restore/build and original-path scan — PASS; the verification folder was removed after the check.
- M11 GitNexus post-change index — 3,731 nodes / 12,681 edges / 300 flows with 0 import cycles.
- M11 Runtime boundary — PASS; `Scada.Runtime` references only `Scada.Core`, with no WPF, App, Infrastructure or concrete-driver dependency.
- `dotnet list Scada.sln package --include-transitive --vulnerable` — PASS; no vulnerable packages reported. `SQLitePCLRaw.bundle_e_sqlite3`, `core`, `lib.e_sqlite3` and `provider.e_sqlite3` resolve to 2.1.12 through `Microsoft.Data.Sqlite` 10.0.11.
- `git diff --check` — PASS.
- InfluxDB package audit — PASS; no vulnerable packages reported.
- WPF startup smoke test — PASS; `Scada.App` stayed running through the startup check and resolved `MainWindow` with resources/templates loaded without a startup DI/XAML exception.
- Copy-folder portability verification — PASS on a fresh copy outside the repository; restore/build/startup do not depend on the original folder, and no original repository path was found in copied source/configuration files.
- GitNexus final M10 baseline review — PASS; the baseline index contains 3,248 nodes / 10,848 edges / 274 flows with 0 import cycles. `Scada.Runtime` still references only `Scada.Core`, and static boundary scans find no WPF, App or concrete-driver dependency in Runtime.
- UI automation remains out of scope.

## Not implemented — later milestones

- Real Siemens, Mitsubishi, Modbus or OPC UA drivers.
- MQTT Write and command-subscription support.
- The later Trend system.
- Automatic PLC communication alarms, Alarm-to-PLC acknowledgement and Alarm notification/escalation.
- PLC-backed Machine Settings Apply/Write, recipes, calibration workflow, audit trail and authorization.
- Deployment tooling.
- Deeper active-view subscription lifecycle optimization beyond the M3 activation/deactivation boundary.
- Distributed multi-runtime, redundancy/HA, web/cloud, advanced scripting and plugin marketplace.

## Technical debt

- A per-device factory that creates a driver with a mismatched `DriverType` can leave that instance without lease ownership before `Acquire` throws. Correct `IDisposable`/`IAsyncDisposable` cleanup on this exceptional misconfiguration path is deferred until resolver acquisition/lifetime design is expanded.
- A genuinely non-cooperative driver operation may remain in flight after the manager shutdown budget expires. M2 bounds manager return time and retains ownership rather than attempting to kill the task; deeper orphan-operation supervision is later work.
- Project persistence supports sequential schema v1 → v2 → v3 → v4 → v5 → v6 migration. Migration remains in memory until explicit Save; multi-process conflict handling and undo/redo remain deferred.
- Project startup requires an explicit canonical `--project-file` path (the supplied launcher provides it); automatic project discovery and hot reload are intentionally not implemented.
- Tag Manager validation is deterministic and synchronous; full UI automation, advanced tag scaling/offset semantics and runtime reconfiguration without restart remain later work.
- Import conflict resolution currently offers explicit apply-all for conflict-free imports or append-non-conflicting/cancel for conflicted imports; identity regeneration after a conflict is deferred.
- InfluxDB provider verification has not yet included a live remote InfluxDB server; transport error mapping, retention behavior and long-running replay remain subject to integration testing.
- The official InfluxDB.Client exception model exposes HTTP status but does not provide reliable point-level rejection metadata; production therefore preserves generic 400 rows, while point-specific splitting remains available only to an explicitly confirming transport implementation.
- The durable Influx outbox is single-process and SQLite-backed; multi-process writers and hot reload remain deferred. M10 already completed the authoritative 50-PLC/~10,000-tag qualification at `402ee9d46f41489fee8912bbed57dc1388550658`; live remote Influx integration remains a separate deferred test boundary.
- History Settings Test Connection performs a non-writing candidate probe only; it does not claim write permission until a live integration test is added. UI command behavior is unit-tested, not UI-automation-tested.
- Historian configuration changes are persisted and marked restart-required; runtime hot reload is intentionally not implemented.
- Alarm configuration changes are persisted and marked restart-required; runtime hot reload, automatic communication alarms, notification/escalation and multi-process Alarm SQLite writers remain deferred.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. M7 MQTT Publisher and M10 qualification are complete; MQTT Write, command subscriptions and PLC-write paths remain deferred. M11 is implemented on its feature branch and remains pending independent PR review and merge.
