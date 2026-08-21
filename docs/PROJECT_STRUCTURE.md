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

`Scada.Runtime` is driver-neutral. It consumes `IPlcDriver` from `Scada.Core` and owns only the runtime-local resolver, lease, manager and polling workers. Concrete drivers are composed by `Scada.App` from `Scada.Drivers`.

## Main folders

```text
Scada.Core/
├── Common
├── Configuration
├── Devices
├── Drivers
└── Tags

Scada.Runtime/
├── Devices
├── Drivers
├── Engine
├── Polling
└── Tags

Scada.Drivers/
└── Simulator

Scada.Infrastructure/
└── Configuration

Scada.App/
├── Resources
├── ViewModels
└── Views
```

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
```

There is intentionally no `Scada.App.Tests` project and no UI automation test in this milestone.

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

TagCache remains the central runtime source. A disconnected device publishes `TagQuality.Disconnected`; a last-known value keeps its original PLC timestamp, while a tag without a valid value gets the failure transition timestamp.

## Portable configuration

`Scada.App/appsettings.json` is copied to application output. The application sets its content root to `AppContext.BaseDirectory`, so running from a copied template folder does not depend on the original repository or current working directory.
