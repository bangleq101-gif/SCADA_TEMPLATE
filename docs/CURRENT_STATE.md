# Current State

Architecture V1 is approved.

Current implementation milestone:

Milestone 12 — Read-only Operational Health and Engineering Diagnostics

Status:

M11 — Alarm System — COMPLETE. The base implementation was merged via PR #15 at `4b903723ed94d846420c2bf3867eec18a395d1c4`; the approved architecture-alignment revision was merged via PR #16 with approved head `636e8fb16080f29e98d3ea976e5e584e1abe7887`, producing final canonical `main` at `25ec87e91eba0be268384c7b941c63cb8bb0f6d9`.

M12 — Read-only Operational Health and Engineering Diagnostics — implemented on `feature/milestone-12-operational-health`; PR #19 is open and pending independent re-review and merge. Canonical `main` remains the M11 baseline until M12 is merged.

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

The approved Revision 1 Alarm architecture and its alignment are merged to canonical `main`. The implementation retains an immutable case-insensitive `TagId` fan-out index and stable alarm order, avoids full snapshot materialization for raw samples with unchanged lifecycle/quality, and delivers latest-state snapshots to WPF through one coalesced Dispatcher item per active generation. Alarm events carry nullable source quality, and SQLite AlarmEvents uses explicit schema v2 migration; legacy rows remain quality-null. The implementation remains PLC-read-only, consumes central TagCache values, uses exact-instance SCADA-state acknowledgement, monotonic activation delays, bounded persistence coordination, project-relative SQLite storage and trusted-checkpoint recovery. Project schema v6 migrates existing v5 projects in memory with `AlarmOptions.Enabled = false`.

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
- Bounded Alarm persistence startup operations; a timeout or store-owned cancellation fails Alarm closed without delaying unrelated polling startup, and late non-cooperative completion remains observed.
- Bounded trusted-checkpoint commit using the actual store-operation cancellation token; timeout/cancellation cannot escape through host shutdown, and timed-out persistence workers retain an exception-observation owner.
- Current-schema project validation rejects a missing/null Alarm options container with a structured blocking issue instead of allowing a later startup null-reference failure.
- Gap-free clean-drain trusted checkpoints, compatible material-definition recovery, untrusted recovery diagnostics and orphaned/incompatible instance accounting.
- Project-relative `Data/alarms.db` SQLite event/open-instance store with rooted/traversal path rejection, atomic session metadata and corrupt/newer-schema handling.
- `engineering.alarms`, `monitoring.alarms`, read-only Alarm journal query and compact Operation Alarm summary in WPF.
- Deterministic clock/state/ACK/quality/sequence/recovery/persistence/lifecycle/UI tests and a bounded sanity using 10,000 project tags, 2,000 Alarm definitions and 500 distinct Alarm TagIds.

## M11 final merged-main alignment

- Runtime retains one case-insensitive `TagId → MutableAlarm[]` index and precomputed definition order; a TagCache callback evaluates only matching definitions.
- Runtime snapshot publication is meaningful-change/diagnostic based: unchanged raw source samples update runtime state but do not rebuild and fan out a full snapshot per sample.
- Operation and Alarm Monitoring use generation-guarded latest-state Dispatcher coalescing with at most one pending UI update per active generation; deactivation/disposal invalidates stale queued callbacks.
- `AlarmEvent.SourceQuality` is nullable and store-neutral. Fresh Alarm SQLite databases use schema v2; v1 rows migrate deterministically and retain `NULL` quality when the old schema had no source-quality value.

M11 merged-main evidence on `25ec87e91eba0be268384c7b941c63cb8bb0f6d9`:

- Full verification — PASS; 397/397 tests, 0 failures.
- GitNexus — 3,806 nodes / 13,067 edges / 150 clusters / 300 flows / 0 cycles.
- Runtime boundary — PASS; `Scada.Runtime` references `Scada.Core` only.
- Alarm hot-path structural evidence — 10,000 tags, 2,000 Alarm definitions and 500 distinct Alarm TagIds; T0=4 matching definitions, followed by 100 unchanged samples with 0 additional full comparisons, snapshot materializations, publications or subscriber deliveries.

## Implemented in Milestone 12

- One singleton `RuntimeHealthService` owns one 1-second production sampler, one `PeriodicTimer`, one sampler task and one immutable snapshot publication per tick.
- `RuntimeHealthAggregator` composes PLC/device snapshots, Historian, optional Influx diagnostics, MQTT, Alarm, TagCache counts and process telemetry without changing polling, provider or TagCache hot paths.
- Runtime health states use normal deterministic precedence (`Faulted` > `Degraded` > `Starting` > `Unknown` > `Healthy`); `Stopping` is a separate shutdown override, and absent or missing enabled-device snapshots are not reported as Healthy.
- TagCache `ValueCount` and `SubscriptionCount` remain available while optional update/callback/exception counters are explicitly unavailable when production metrics are disabled.
- Process CPU, working set and monotonic uptime are observational only; first CPU sample is unavailable and wall-clock changes do not affect uptime.
- Runtime error messages and optional store diagnostics are sanitized before publication to App; no credentials, tokens, connection strings or inappropriate local paths are exposed.
- App owns one shared health presentation source with generation-guarded, latest-state Dispatcher coalescing for active workspaces. Operation and the Shell status bar show compact read-only glyph/text indicators for PLC, History, MQTT and Runtime.
- `engineering.system` provides a compact read-only service-health surface covering Runtime/System health, Historian, MQTT and provider-aware Local Buffer status; `engineering.diagnostics` provides a virtualized, read-only device diagnostics table. Neither surface reads PLCs, writes configuration or owns commands/reconnect operations.
- M12 tests cover aggregation precedence, provider asymmetry, unavailable metrics, process telemetry, monotonic uptime, sanitization, sampler cadence/ownership/shutdown, subscriber isolation, 50 synthetic runtime device snapshots, a separate configured 50-device-identity/10,000-tag/100-update sampler scale gate, workspace lifecycle/coalescing, WPF status indicators and DataGrid virtualization.

## Verified

- `dotnet restore Scada.sln --ignore-failed-sources` — PASS.
- `dotnet build Scada.sln -c Release --no-restore` — PASS with 0 warnings and 0 errors.
- M11 merged-main base restore and Release build — PASS with 0 warnings and 0 errors.
- M11 merged-main base full test suite — PASS; 390 tests, 0 failures (138 App, 119 Runtime, 38 Core, 3 Drivers, 65 Infrastructure, 27 Stress).
- M11 merged-main base vulnerability audit — PASS; no vulnerable direct or transitive package was reported.
- M11 merged-main base WPF startup smoke — PASS; the application remained running with Alarm resources, DI and routes loaded.
- M11 merged-main base fresh copy-folder restore/build and original-path scan — PASS; the verification folder was removed after the check.
- M11 merged-main base GitNexus post-change index — 3,731 nodes / 12,681 edges / 300 flows with 0 import cycles.
- M11 merged-main base Runtime boundary — PASS; `Scada.Runtime` references only `Scada.Core`, with no WPF, App, Infrastructure or concrete-driver dependency.
- M11 architecture-alignment worktree restore and Release build — PASS with 0 warnings and 0 errors.
- M11 architecture-alignment worktree full test suite — PASS; 397 tests, 0 failures (141 App, 121 Runtime, 38 Core, 3 Drivers, 67 Infrastructure, 27 Stress).
- M11 architecture-alignment vulnerability audit — PASS; no vulnerable direct or transitive package was reported.
- M11 architecture-alignment WPF startup smoke — PASS; `Scada.App` remained running through the smoke window without a DI/XAML startup exception.
- M11 architecture-alignment fresh copy-folder restore/build/startup and original-path scan — PASS; no original repository path was found and the verification folder was removed after the check.
- M11 architecture-alignment GitNexus index — 3,806 nodes / 13,067 edges / 300 flows with 0 import cycles; changed-flow review covers TagCache → AlarmRuntimeService and AlarmEvent → SQLite paths. The unchanged-raw-sample structural test also confirms no full snapshot comparison or snapshot materialization is performed in that burst.
- M11 architecture-alignment Runtime boundary — PASS; `Scada.Runtime` references only `Scada.Core`, with no WPF, App, Infrastructure or concrete-driver dependency.
- `dotnet list Scada.sln package --include-transitive --vulnerable` — PASS; no vulnerable packages reported. `SQLitePCLRaw.bundle_e_sqlite3`, `core`, `lib.e_sqlite3` and `provider.e_sqlite3` resolve to 2.1.12 through `Microsoft.Data.Sqlite` 10.0.11.
- `git diff --check` — PASS.
- InfluxDB package audit — PASS; no vulnerable packages reported.
- WPF startup smoke test — PASS; `Scada.App` stayed running through the startup check and resolved `MainWindow` with resources/templates loaded without a startup DI/XAML exception.
- Copy-folder portability verification — PASS on a fresh copy outside the repository; restore/build/startup do not depend on the original folder, and no original repository path was found in copied source/configuration files.
- GitNexus final M10 baseline review — PASS; the baseline index contains 3,248 nodes / 10,848 edges / 274 flows with 0 import cycles. `Scada.Runtime` still references only `Scada.Core`, and static boundary scans find no WPF, App or concrete-driver dependency in Runtime.
- UI automation remains out of scope.
- M12 feature-worktree verification — PASS; restore, Release build (0 warnings/0 errors), full test suite (438/438), vulnerability audit, WPF resource/startup smoke, fresh copy-folder portability, `git diff --check`, GitNexus cycle checks and Runtime boundary scans all passed.

## Not implemented — later milestones

- Real Siemens, Mitsubishi, Modbus or OPC UA drivers.
- MQTT Write and command-subscription support.
- The later Trend system.
- Automatic PLC communication alarms, Alarm-to-PLC acknowledgement and Alarm notification/escalation.
- PLC-backed Machine Settings Apply/Write, recipes, calibration workflow, audit trail and authorization.
- Deployment tooling.
- Deeper active-view subscription lifecycle optimization beyond the M3 activation/deactivation boundary.
- PLC/device editing, reconnect, command, acknowledgement and configuration-write actions from health/diagnostics surfaces.
- Health thresholds, notifications, trend/reporting surfaces and runtime hot reload.
- Distributed multi-runtime, redundancy/HA, web/cloud, advanced scripting and plugin marketplace.

## Technical debt

- A per-device factory that creates a driver with a mismatched `DriverType` can leave that instance without lease ownership before `Acquire` throws. Correct `IDisposable`/`IAsyncDisposable` cleanup on this exceptional misconfiguration path is deferred until resolver acquisition/lifetime design is expanded.
- A genuinely non-cooperative driver operation may remain in flight after the manager shutdown budget expires. M2 bounds manager return time and retains ownership rather than attempting to kill the task; deeper orphan-operation supervision is later work.
- Project persistence supports sequential schema v1 → v2 → v3 → v4 → v5 → v6 migration. Migration remains in memory until explicit Save; multi-process conflict handling and undo/redo remain deferred.
- Project startup requires an explicit canonical `--project-file` path (the supplied launcher provides it); automatic project discovery and hot reload are intentionally not implemented.
- Tag Manager validation is deterministic and synchronous; full UI automation, `TagDefinition` Scale/Offset fields and runtime scaling/offset transformation semantics remain later work (see `docs/V1_COVERAGE.md` row 16). Runtime reconfiguration without restart also remains later work.
- The general Online Tag Monitor activates one TagCache subscription per configured row and can enqueue one Dispatcher callback per off-thread update; generation-safe activation/deactivation is implemented, while deeper visible-tag scoping and bounded/latest-state coalescing remain later work (see `docs/V1_COVERAGE.md` row 11).
- Import conflict resolution currently offers explicit apply-all for conflict-free imports or append-non-conflicting/cancel for conflicted imports; identity regeneration after a conflict is deferred.
- InfluxDB provider verification has not yet included a live remote InfluxDB server; transport error mapping, retention behavior and long-running replay remain subject to integration testing.
- The official InfluxDB.Client exception model exposes HTTP status but does not provide reliable point-level rejection metadata; production therefore preserves generic 400 rows, while point-specific splitting remains available only to an explicitly confirming transport implementation.
- The durable Influx outbox is single-process and SQLite-backed; multi-process writers and hot reload remain deferred. M10 already completed the authoritative 50-PLC/~10,000-tag qualification at `402ee9d46f41489fee8912bbed57dc1388550658`; live remote Influx integration remains a separate deferred test boundary.
- History Settings Test Connection performs a non-writing candidate probe only; it does not claim write permission until a live integration test is added. UI command behavior is unit-tested, not UI-automation-tested.
- Historian configuration changes are persisted and marked restart-required; runtime hot reload is intentionally not implemented.
- Alarm configuration changes are persisted and marked restart-required; runtime hot reload, automatic communication alarms, notification/escalation and multi-process Alarm SQLite writers remain deferred.
- Alarm SQLite connection configuration is still shared from the Infrastructure History namespace; moving this generic helper to a neutral Persistence namespace is deferred to avoid unrelated churn in the alignment hotfix.
- Centralized logging uses `ILogger<T>`, the Microsoft.Extensions.Logging pipeline, the Debug provider and structured `DeviceId` fields on polling paths; consistent `RuntimeId` contextual enrichment across Runtime subsystems is not yet standardized (see `docs/V1_COVERAGE.md` row 48).
- Remaining Architecture V1 partial/not-started coverage, including screen metadata, Engineering Devices, deployment/offline strategy and Simulator fault mode, is tracked in `docs/V1_COVERAGE.md`; this is documentation traceability, not M12 authorization.
- The M12 health sampler is observational and intentionally does not provide threshold evaluation, event persistence, notification, command or runtime configuration mutation.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. M7 MQTT Publisher, M10 qualification and M11 Alarm System are complete on canonical `main`; MQTT Write, command subscriptions and PLC-write paths remain deferred. M12 is implemented on this feature branch, PR #19 is open, and it remains pending independent re-review and merge; no subsequent milestone is authorized.
