# Current State

Architecture V1 is approved.

Current implementation milestone:

Milestone 4 — Tag Manager

Status:

Completed and merged to `main` via PR #4.

Merge commit:

`e1a56b42b31b83d46649a897ca517a0d78026221`

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

## Verified

- `dotnet restore Scada.sln --ignore-failed-sources` — PASS.
- `dotnet build Scada.sln -c Release --no-restore` — PASS with 0 warnings and 0 errors.
- `dotnet test Scada.sln -c Release --no-build` — PASS; 110 tests, 0 failures (49 App, 32 Runtime, 3 Core, 3 Drivers, 23 Infrastructure).
- `git diff --check` — PASS.
- WPF startup smoke test — PASS; `Scada.App` stayed running through the startup check and resolved `MainWindow` with resources/templates loaded without a startup DI/XAML exception.
- Copy-folder portability verification — PASS on a fresh copy outside the repository; restore/build and explicit project-file startup do not depend on the original folder, and the project document remains unchanged.
- GitNexus post-change review — PASS; 0 import cycles, no Runtime dependency on App/WPF/Drivers, no TagCache source change, and no direct PLC reads from Tag Manager.
- UI automation remains out of scope.

## Not implemented — later milestones

- Real Siemens, Mitsubishi, Modbus or OPC UA drivers.
- Historian, SQLite historian and InfluxDB provider.
- MQTT publisher or write support.
- Complete Alarm and Trend systems.
- Reusable HMI controls and Faceplates.
- Full Machine Settings implementation.
- Deployment tooling.
- Stress testing at 50 simulated PLCs / approximately 10,000 tags.
- Deeper active-view subscription lifecycle optimization beyond the M3 activation/deactivation boundary.
- Distributed multi-runtime, redundancy/HA, web/cloud, advanced scripting and plugin marketplace.

## Technical debt

- A per-device factory that creates a driver with a mismatched `DriverType` can leave that instance without lease ownership before `Acquire` throws. Correct `IDisposable`/`IAsyncDisposable` cleanup on this exceptional misconfiguration path is deferred until resolver acquisition/lifetime design is expanded.
- A genuinely non-cooperative driver operation may remain in flight after the manager shutdown budget expires. M2 bounds manager return time and retains ownership rather than attempting to kill the task; deeper orphan-operation supervision is later work.
- Project persistence currently supports schema version 1 only; migrations, multi-process conflict handling and undo/redo are deferred.
- Project startup requires an explicit canonical `--project-file` path (the supplied launcher provides it); automatic project discovery and hot reload are intentionally not implemented.
- Tag Manager validation is deterministic and synchronous; full UI automation, advanced tag scaling/offset semantics and runtime reconfiguration without restart remain later work.
- Import conflict resolution currently offers explicit apply-all for conflict-free imports or append-non-conflicting/cancel for conflicted imports; identity regeneration after a conflict is deferred.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. Do not jump ahead to MQTT, InfluxDB or other later milestones without explicit approval.
