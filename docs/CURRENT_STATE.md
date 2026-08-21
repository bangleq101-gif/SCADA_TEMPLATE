# Current State

Architecture V1 is approved.

Current implementation milestone:

Milestone 2 — Runtime and Device Polling

Status:

Implemented on `feature/milestone-2-runtime-polling`; checkpoint and stabilization changes are committed/pushed and pending final direct source review, PR and merge.

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

The V1 target remains:

```text
1 Runtime
n PLC
~10,000 tags
```

## Verified

- `dotnet restore Scada.sln --ignore-failed-sources` — PASS.
- `dotnet build Scada.sln -c Release --no-restore` — PASS with 0 warnings and 0 errors.
- `dotnet test Scada.sln -c Release --no-build` — PASS; 43 tests, 0 failures (31 Runtime, 3 Core, 3 Drivers, 6 Infrastructure).
- `git diff --check` — PASS.
- WPF startup smoke test — PASS; `Scada.App` starts and resolves `MainWindow` without a DI exception.
- Copy-folder portability verification — PASS on a fresh copy; restore/build does not depend on the original folder.
- No `Scada.App.Tests` project is included; UI automation remains out of scope.

## Not implemented — later milestones

- Real Siemens, Mitsubishi, Modbus or OPC UA drivers.
- Complete Tag Manager.
- Historian, SQLite historian and InfluxDB provider.
- MQTT publisher or write support.
- Complete Alarm and Trend systems.
- Reusable HMI controls and Faceplates.
- Full Machine Settings implementation.
- Deployment tooling.
- Stress testing at 50 simulated PLCs / approximately 10,000 tags.
- Active-view subscription lifecycle optimization for the Monitoring UI.
- Milestone 3 shell/workspace completion beyond the existing foundation.
- Distributed multi-runtime, redundancy/HA, web/cloud, advanced scripting and plugin marketplace.

## Technical debt

- A driver factory mismatch in `DriverResolver` can only synchronously dispose a wrongly-created `IDisposable`; a future async resolver/lifecycle design should handle mismatched `IAsyncDisposable` instances without sync-over-async.
- A genuinely non-cooperative driver operation may remain in flight after the manager shutdown budget expires. M2 bounds manager return time and retains ownership rather than attempting to kill the task; deeper orphan-operation supervision is later work.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. Do not jump ahead to MQTT, InfluxDB or other later milestones without explicit approval.
