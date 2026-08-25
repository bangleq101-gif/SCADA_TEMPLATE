
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

## Milestone 12 — Read-only Operational Health and Engineering Diagnostics

Status: IMPLEMENTED on `feature/milestone-12-operational-health`; pending independent source review, PR and merge.

- Add one Runtime-owned health sampler/coordinator at a production 1-second cadence with one immutable snapshot publication per tick.
- Aggregate existing DeviceManager, Historian, optional Influx store, MQTT, Alarm, TagCache and process telemetry snapshots without changing PLC polling or provider contracts.
- Keep TagCache as the sole live-value source; production TagCache hot-path metrics remain disabled unless explicitly enabled by an existing diagnostic seam.
- Sanitize health/error data before the App boundary and expose unavailable metrics explicitly rather than fabricating zeroes.
- Add read-only Operation/Shell health summaries, `engineering.system` service health and virtualized `engineering.diagnostics` device diagnostics with active-only lifecycle/coalescing.
- Verify one sampler/timer, bounded Dispatcher delivery, WPF resources, Runtime boundaries, copy-folder portability and no command/PLC/MQTT writes.
- M12 does not include thresholds, notifications, event persistence, device editing, reconnect/command actions, runtime configuration mutation or any M13 scope.

## Remaining Architecture V1 Coverage After M12 implementation (pending merge)

The complete requirement-by-requirement matrix is maintained in `docs/V1_COVERAGE.md`. It records 50 audited areas: 31 `COMPLETE`, 10 `PARTIAL`, 3 `NOT STARTED` and 6 `EXPLICITLY DEFERRED`.

The remaining work is intentionally described as coverage, not as an authorization to start a new milestone:

1. `PARTIAL` — Scale/Offset domain and runtime transformation semantics; active-view subscription scope beyond the M12 health/workspace boundary; Address Browser/device-selection extension; module/line/machine organization; broader HMI catalog; external asset packaging; Engineering Devices/Trend route coverage; screen metadata; Simulator fault mode; and consistent RuntimeId logging context.
2. `NOT STARTED` — Engineering Devices, deployment tooling and offline package/installation strategy.
3. `EXPLICITLY DEFERRED` — production Siemens/Mitsubishi/Modbus/OPC UA drivers, MQTT Write/command subscriptions, Trend, Recipes/Calibration, Reports, and distributed/Web/cloud/HA/scripting/plugin systems.

### Candidate ordering for a future milestone (proposal only)

If a future planning gate is opened, a dependency-aware review could consider:

1. remaining operational engineering coverage (Engineering Devices, screen metadata and Overview/Status Bar extensions);
2. bounded active-view delivery, Scale/Offset semantics, screen metadata and module/line/machine organization;
3. deployment/offline portability tooling;
4. separately approved monitoring or HMI extensions such as Trend or additional asset support.

This is sequencing guidance only. It does not select or authorize M13 implementation scope.

## Explicitly deferred

Do not implement without separate approval:

- distributed multi-runtime;
- redundancy or HA;
- web frontend;
- cloud architecture;
- advanced scripting;
- plugin marketplace.
