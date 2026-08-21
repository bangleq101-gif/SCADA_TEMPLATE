# Milestone 1 Project Structure

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

`Scada.Runtime` is driver-neutral. It consumes `IPlcDriver` from `Scada.Core`; `Scada.App` composes `SimulatorPlcDriver` from `Scada.Drivers`.

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

## Test projects

```text
tests/Scada.Core.Tests
tests/Scada.Runtime.Tests
tests/Scada.Drivers.Tests
tests/Scada.Infrastructure.Tests
```

There is intentionally no `Scada.App.Tests` in Milestone 1 and no UI automation test.

## Runtime data flow

```text
PLC or Simulator
      ↓
IPlcDriver
      ↓
PollingRuntimeService
      ↓
TagEngine
      ↓
TagCache
      ↓
WPF subscriptions / Online Tag Monitor
```

## Portable configuration

`Scada.App/appsettings.json` is copied to the application output. The application sets its content root to `AppContext.BaseDirectory`, so running from a copied template folder does not depend on the original repository or current working directory.
