
# SCADA V1 Roadmap

The roadmap is ordered. Complete and review each milestone before moving to the next one.

## Milestone 1 — Foundation

- Create `Scada.sln` and the five V1 projects.
- Create test projects and establish the dependency structure.
- Add Dependency Injection and configuration foundations.
- Add the basic WPF Shell and navigation foundation.
- Implement `RuntimeId`.
- Establish the Simulator, `TagDefinition`, `TagValue`, `TagQuality`, `TagDataType`, `TagEngine`, `TagCache` and tag subscriptions.
- Add the Online Tag Monitor foundation.
- Document the resulting structure.

## Milestone 2 — Runtime and device polling

- Implement `DeviceManager`, Scan Groups and polling workers.
- Establish asynchronous device isolation and Batch Read architecture.
- Add reconnect, timeout handling and runtime diagnostics.

## Milestone 3 — Shell and workspaces

- Complete the Shell structure and hierarchical navigation.
- Add Operation, Machine Settings, Monitoring and Engineering workspaces.
- Establish the WPF design system and reusable page layouts.

## Milestone 4 — Tag Manager

- Implement visual Add, Delete, Edit, Duplicate, Search, Filter, Sort, Multi-select and Bulk edit workflows.
- Add Copy/Paste, CSV Import/Export, history configuration and MQTT configuration.
- Support DataGrid virtualization and a target of approximately 10,000 tags.

## Milestone 5 — Historian foundation

- Implement Digital, Analog, Fast Analog and Custom History Profiles.
- Add the Historian Queue, background writing and SQLite historian storage.

## Milestone 6 — InfluxDB provider

- Add batching, connection health, local buffering, reconnect, background resynchronization and retention configuration.
- InfluxDB failure must not stop PLC polling or normal SCADA operation.

## Milestone 7 — MQTT

- Implement the MQTT Publisher, broker configuration and MQTT profiles.
- Generate topics automatically where possible.
- Publish TagCache values with quality and timestamp, including reconnect and health monitoring.
- Keep MQTT Write disabled by default.

## Milestone 8 — Reusable HMI controls and Faceplates

- Implement reusable Motor, Pump, Valve, Tank, Pipe, Conveyor and Indicator controls.
- Implement reusable Faceplates.
- Prepare external symbol asset support without making it a core architecture dependency.

## Milestone 9 — Machine Settings

- Implement reusable `ParameterEditor` and `ParameterGroup` components.
- Add machine-specific settings pages, validation and min/max/unit handling.

## Milestone 10 — Stress testing and optimization

- Test 50 simulated PLCs and approximately 10,000 tags.
- Measure CPU, RAM, scan duration, scan jitter, missed cycles, updates/sec, UI responsiveness and historian queue performance.
- Optimize only from measured results and record findings in the project documentation.

## Explicitly deferred

Do not implement without separate approval:

- distributed multi-runtime;
- redundancy or HA;
- web frontend;
- cloud architecture;
- advanced scripting;
- plugin marketplace.
