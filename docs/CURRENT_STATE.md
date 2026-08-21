
# Current State

Architecture V1 approved.

Current implementation milestone:

Milestone 1 — Foundation

Status:

Not started

- Project goals defined.
- Architecture V1 approved.
- Git repository created.
- GitHub repository created.
- AGENTS.md established.
- docs/SCADA_ARCHITECTURE_V1.md established.
- Copy-folder portability defined as a core requirement.

The planned solution contains:

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

V1 uses:

```text
1 Runtime
n PLC
~10,000 tags target
```

Runtime must support:

- Batch Read
- Scan Groups
- central TagCache
- asynchronous device isolation
- subscription-based WPF updates

Currently there is no production SCADA source code.

The following have not yet been implemented:

- Solution/projects
- Runtime
- TagCache
- Simulator
- PLC drivers
- Tag Manager
- Historian
- MQTT
- Alarm
- Trend
- reusable HMI controls
- Machine Settings
- deployment tooling

Implementation must follow the ordered milestones in `docs/ROADMAP.md` and the constraints in `docs/SCADA_ARCHITECTURE_V1.md`. Do not jump ahead to MQTT, InfluxDB or other later milestones without explicit approval.
