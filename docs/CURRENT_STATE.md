# Current State

Architecture V1 is approved.

Current implementation milestone:

Milestone 1 — Foundation

Status:

Implemented — foundation complete; later milestones have not started.

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
- Generic polling foundation with driver shutdown through `DisconnectAsync`.
- Subscriber exception isolation so one callback cannot stop other updates or polling.
- Basic WPF Shell, navigation and design-resource foundation.
- Operation, Machine Settings, Monitoring and Engineering page foundations.
- Online Tag Monitor foundation.
- WPF page ViewModel DI registration and `MainWindow` startup resolution.
- Architecture, project-structure and development-rule documentation.
- Copy-folder portability remains a core requirement.

The V1 target remains:

```text
1 Runtime
n PLC
~10,000 tags
```

## Verified

- `dotnet restore Scada.sln --ignore-failed-sources` — PASS.
- `dotnet build Scada.sln -c Release --no-restore` — PASS with 0 warnings and 0 errors.
- `dotnet test Scada.sln -c Release --no-build` — PASS; 12 tests, 0 failures.
- `git diff --check` — PASS; line-ending notices are non-blocking.
- WPF startup smoke test — PASS; `Scada.App` starts and resolves `MainWindow` without a DI exception.
- Copy-folder portability verification — PASS on a fresh copy; no architecture or output-path changes were made in this stabilization fix.
- No `Scada.App.Tests` project is included; UI automation remains out of scope.

## Not implemented — later milestones

- `DeviceManager`.
- `DriverFactory`.
- Scan Group scheduling.
- Asynchronous multi-device isolation.
- Reconnect/timeout diagnostics beyond the current foundation state tracking.
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

These deferred items must not be added as part of the Milestone 1 pre-merge stabilization.

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. Do not jump ahead to MQTT, InfluxDB or other later milestones without explicit approval.
