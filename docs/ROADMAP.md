
# SCADA V1 Roadmap

The roadmap is ordered. Complete and review each milestone before moving to the next one.

## Milestone 1 — Foundation

Status: COMPLETE — merged and verified as the Architecture V1 foundation.

- Create `Scada.sln` and the five V1 projects.
- Create test projects and establish the dependency structure.
- Add Dependency Injection and configuration foundations.
- Add the basic WPF Shell and navigation foundation.
- Implement `RuntimeId`.
- Establish the Simulator, `TagDefinition`, `TagValue`, `TagQuality`, `TagDataType`, `TagEngine`, `TagCache` and tag subscriptions.
- Add the Online Tag Monitor foundation.
- Document the resulting structure.

## Milestone 2 — Runtime and device polling

Status: COMPLETE — merged and verified with driver-neutral asynchronous polling and device isolation.

- Implement `DeviceManager`, Scan Groups and polling workers.
- Establish asynchronous device isolation and Batch Read architecture.
- Add reconnect, timeout handling and runtime diagnostics.

## Milestone 3 — Shell and workspaces

Status: COMPLETE — merged and verified with the four canonical workspaces and hierarchical navigation.

- Complete the Shell structure and hierarchical navigation.
- Add Operation, Machine Settings, Monitoring and Engineering workspaces.
- Establish the WPF design system and reusable page layouts.

## Milestone 4 — Tag Manager

Status: COMPLETE — merged and verified with the planned editing workflows and virtualized tag table foundation.

- Implement visual Add, Delete, Edit, Duplicate, Search, Filter, Sort, Multi-select and Bulk edit workflows.
- Add Copy/Paste, CSV Import/Export, history configuration and MQTT configuration.
- Support DataGrid virtualization and a target of approximately 10,000 tags.

## Milestone 5 — Historian foundation

Status: COMPLETE — merged and verified with SQLite historian foundations and history profiles.

- Implement Digital, Analog, Fast Analog and Custom History Profiles.
- Add the Historian Queue, background writing and SQLite historian storage.

## Milestone 6 — InfluxDB provider

Status: COMPLETE — merged and verified with buffered InfluxDB provider foundations and failure isolation.

- Add batching, connection health, local buffering, reconnect, background resynchronization and retention configuration.
- InfluxDB failure must not stop PLC polling or normal SCADA operation.

## Milestone 7 — MQTT

Status: COMPLETE — merged and verified as publisher-only; MQTT Write remains deferred.

- Implement the MQTT Publisher, broker configuration and MQTT profiles.
- Generate topics automatically where possible.
- Publish TagCache values with quality and timestamp, including reconnect and health monitoring.
- Keep MQTT Write disabled by default.

## Milestone 8 — Reusable HMI controls and Faceplates

Status: COMPLETE — merged and verified with the seven read-only HMI controls and faceplate foundations.

- Implement reusable Motor, Pump, Valve, Tank, Pipe, Conveyor and Indicator controls.
- Implement reusable Faceplates.
- Prepare external symbol asset support without making it a core architecture dependency.

## Milestone 9 — Machine Settings

Status: COMPLETE — merged and verified with typed, transactional Machine Settings and TagCache-only live values.

- Implement reusable `ParameterEditor` and `ParameterGroup` components.
- Add machine-specific settings pages, validation and min/max/unit handling.

## Milestone 10 — Stress testing and optimization

Status: COMPLETE — qualified baseline accepted under the authoritative benchmark SHA `402ee9d46f41489fee8912bbed57dc1388550658`; no optimization was justified by measured evidence.

- Test 50 simulated PLCs and approximately 10,000 tags.
- Measure CPU, RAM, scan duration, scan jitter, missed cycles, updates/sec, UI responsiveness and historian queue performance.
- Optimize only from measured results and record findings in the project documentation.

## Milestone 11 — Alarm System

Status: COMPLETE — merged and verified on canonical `main` at `25ec87e91eba0be268384c7b941c63cb8bb0f6d9` via PR #15 and the alignment PR #16.

- Implement a PLC-read-only Alarm System that evaluates central `TagCache` values without additional PLC reads or PLC/MQTT writes.
- Support `DigitalEquals`, High, HighHigh, Low and LowLow rules with deterministic deadband, exact-instance acknowledgement and `ReturnedUnacknowledged` lifecycle semantics.
- Use one TagCache subscription per distinct logical TagId and one shared monotonic activation-delay coordinator; do not create a timer or task per alarm.
- Persist Alarm events and open-instance checkpoints in project-relative SQLite storage at `Data/alarms.db`, resolved under the canonical `ProjectPath.DirectoryPath`.
- Introduce project schema v6 with `AlarmOptions.Enabled = false` for v5-to-v6 migration so existing projects do not gain Alarm runtime behavior automatically.
- Restore live Alarm state only from an explicitly trusted, gap-free clean checkpoint with a compatible material definition fingerprint. Untrusted, missing, disabled or materially changed instances remain historical/orphaned and are not silently restored as authoritative.
- Add `engineering.alarms`, `monitoring.alarms` and a compact Operation Alarm summary while retaining `ProjectEditSession` as the project-editing authority.
- Verify M11 with controllable-time state/ACK/quality/recovery/path/lifecycle tests and a bounded Alarm-specific scale sanity. This does not replace or redefine the authoritative M10 benchmark at `402ee9d46f41489fee8912bbed57dc1388550658`.
- Keep communication alarms, PLC acknowledgement writes, MQTT Write, authentication/authorization, Trend and notification/escalation systems deferred.

## Remaining Architecture V1 Coverage After M11

The complete requirement-by-requirement matrix is maintained in `docs/V1_COVERAGE.md`. It records 44 audited areas: 24 `COMPLETE`, 8 `PARTIAL`, 6 `NOT STARTED` and 6 `EXPLICITLY DEFERRED`.

The remaining work is intentionally described as coverage, not as an authorization to start a new milestone:

1. `PARTIAL` — complete operational surface coverage: device engineering UI, system/diagnostics pages, unified overview health and compact status indicators; complete module/line/machine screen organization and screen metadata where needed.
2. `PARTIAL` — decide the next reusable HMI catalog additions and external-asset packaging boundary without introducing vendor or license dependencies.
3. `PARTIAL` — add a simulator fault-state mode and complete the device Address Browser only when their requirements are separately approved.
4. `NOT STARTED` — Engineering Devices, Engineering System, Engineering Diagnostics, generic screen metadata, deployment tooling and offline package/installation strategy.
5. `EXPLICITLY DEFERRED` — production Siemens/Mitsubishi/Modbus/OPC UA drivers, MQTT Write/command subscriptions, Trend, Recipes/Calibration, Reports, and distributed/Web/cloud/HA/scripting/plugin systems.

### Candidate ordering for a future milestone (proposal only)

If a future planning gate is opened, a dependency-aware review could consider:

1. operational engineering and health surfaces (Devices, System, Diagnostics, Overview/Status Bar);
2. screen metadata and module/line/machine organization;
3. deployment/offline portability tooling;
4. separately approved monitoring or HMI extensions such as Trend or additional asset support.

This is sequencing guidance only. It does not select or authorize M12 implementation scope.

## Explicitly deferred

Do not implement without separate approval:

- distributed multi-runtime;
- redundancy or HA;
- web frontend;
- cloud architecture;
- advanced scripting;
- plugin marketplace.
