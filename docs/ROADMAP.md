
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

Status: COMPLETE. M11 code/runtime was completed and verified at `25ec87e91eba0be268384c7b941c63cb8bb0f6d9` via PR #15 and the alignment PR #16. M11 governance/docs closeout was merged via PR #17 at `2cfd0c39f05e8a9251984e0c82198b72f7616745`; that commit became the M12 implementation base.

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

Status: COMPLETE — merged and verified on canonical `main` at `1b575a0e969703a01b006ab4a44147ab01e73ee7` via PR #19.

- Add one Runtime-owned health sampler/coordinator at a production 1-second cadence with one immutable snapshot publication per tick.
- Aggregate existing DeviceManager, Historian, optional Influx store, MQTT, Alarm, TagCache and process telemetry snapshots without changing PLC polling or provider contracts.
- Keep TagCache as the sole live-value source; production TagCache hot-path metrics remain disabled unless explicitly enabled by an existing diagnostic seam.
- Sanitize health/error data before the App boundary and expose unavailable metrics explicitly rather than fabricating zeroes.
- Add read-only Operation/Shell health summaries, compact PLC/History/MQTT/Runtime status indicators, provider-aware read-only Local Buffer status in `engineering.system` and virtualized `engineering.diagnostics` device diagnostics with active-only lifecycle/coalescing.
- Prove the health aggregation boundary with 50 synthetic runtime device snapshots and separately prove the sampler boundary with 50 configured device identities, 10,000 TagCache values and 100 raw updates without raw-update-driven publication.
- Verify one sampler/timer, bounded Dispatcher delivery, WPF resources, Runtime boundaries, copy-folder portability and no command/PLC/MQTT writes.
- M12 does not include thresholds, notifications, event persistence, device editing, reconnect/command actions, runtime configuration mutation or any M13 scope.

## Milestone 13 — Engineering Devices and Address Browser

Status: COMPLETE — merged and verified on canonical `main` via PR #21 at
`3bf14de5f6f9af6d0121fee367f19a2c9da1607d`.

- Add the App-layer `engineering.devices` workspace while keeping the existing Engineering route hierarchy and `ProjectEditSession` as the only project-editing authority.
- Add a Core driver-engineering provider contract for typed option metadata, provider validation and read-only logical address candidates. The contract must not be an `IPlcDriver` replacement and must not perform PLC polling or writes.
- Provide a Simulator engineering provider with deterministic option validation, read-only address candidates and configurable deterministic fault/disconnect scenarios for commissioning and tests.
- Provide virtualized device editing with Add, Duplicate, Delete, Revert, Save, typed driver options and read-only Address Browser behavior. Device definitions remain static configuration; runtime state remains in Runtime.
- Verify copy-folder portability, provider validation, project-session save/revert semantics, deterministic Simulator scenarios, WPF rendering/accessibility, no PLC/MQTT writes and the existing `Scada.Runtime → Scada.Core` boundary.

M13 does not include production Siemens/Mitsubishi/Modbus/OPC UA drivers, runtime hot reload, reconnect/command controls, PLC writes, MQTT Write, a plugin framework or deployment tooling.

## Milestone 14 — Tag Engineering and Bounded Online Tag Monitor

Status: COMPLETE — merged to canonical `main` via PR #22 at merge commit
`f5cd141f2f26ebc9fa56bd0f8139ce23a670d640`.

- Add `SourceDataType`, canonical `DataType`, finite `Scale` and finite
  `Offset` metadata to `TagDefinition`; use a pure Core transform contract so
  drivers return raw source values and `TagEngine` publishes canonical values to
  central TagCache exactly once.
- Request source types through the existing driver-read plan while keeping
  `Scada.Runtime` driver-neutral. A Good-value transform failure becomes Bad
  through existing TagCache D-019 quality/timestamp semantics; it never causes
  a PLC reread.
- Migrate project schema v6 → v7 in memory, retain legacy identity semantics,
  and update clone/compare/save/revert, CSV/TSV and Tag Manager/bulk editing.
- Bound Online Tag Monitor to static-metadata filtering and paging: default
  250, maximum 500 visible tags, deduplicated active-page subscriptions,
  subscribe-before-seed and one latest-state App Dispatcher delivery per active
  generation.
- Verify numeric/non-numeric conversion contracts, invalid Good-transform
  quality behavior, `Int64` precision, v6 migration, 10,000-tag subscription
  bounds, lifecycle/sequence races and WPF DataGrid virtualization.

M14 does not add PLC/MQTT write paths, direct UI PLC reads, `Min`/`Max`
clamping, calibration/deadband policy, runtime hot reload, new product projects,
new packages, Trend, Reports, Recipes or M15 scope.

## Milestone 15 — Screen Metadata and Module/Line/Machine Composition

Status: COMPLETE — merged to canonical `main` via PR #24 at merge commit
`4f4d325273fcdd1420182b061e40dc5b51bbc235`.

- Add an App-layer immutable screen metadata contract with `ScreenId`, `Title`,
  `Category`, `IconKey`, `Order`, optional `RequiredRole` and route identity.
- Compose static screen registrations into deterministic hierarchical navigation
  using optional Module → Line → Machine → Screen path segments.
- Preserve `NavigationService.CurrentRouteKey` as the authoritative route and
  workspace lifecycle state while using `NavigationItem` only as the App-layer
  navigation/display projection.
- Keep the catalog compile-time and vendor-neutral. Do not add persisted screen
  editors, dynamic XAML/plugin loading, authorization enforcement, PLC/MQTT
  writes, Runtime changes or new product projects.
- Verify built-in route compatibility, hierarchy validation/order, WPF resource
  rendering/accessibility and bounded catalog composition tests.

M15 does not include deployment tooling, offline installation strategy, Trend,
production PLC drivers, MQTT Write, command subscriptions, authorization or
M16 implementation.

## Milestone 16 — Deployment and Offline Portability Foundation

Status: COMPLETE — merged to canonical `main` via PR #26 at merge commit
`377fe17a98fa274b4cb6beb3c3a84d0bfe55fca8`.

- Publish `Scada.App` as a portable Windows bundle with an explicit
  framework-dependent default and optional self-contained mode.
- Require an external canonical absolute `project.json` path at both development
  and published launch boundaries; do not discover a source-tree configuration.
- Validate target Windows/.NET Desktop Runtime prerequisites, bundle contents,
  project JSON, writable project storage and referenced environment secrets
  without logging secret values.
- Verify a copied bundle independently from the repository and provide a bounded
  WPF startup smoke.
- Export the exact restored NuGet dependency graph into an external approved
  folder feed with SHA-256 evidence, and restore using a temporary config that
  clears all online sources.
- Keep installers, package caches, `.nupkg` files, runtime installers, secrets,
  databases and logs outside Git.

M16 does not include MSI/EXE installer authoring, automatic updates, Windows
Service hosting, production PLC drivers, Trend, Reports, Recipes, PLC Write,
MQTT Write or command subscriptions.

## Milestone 17 — Modbus TCP Read-Only Production Driver

Status: COMPLETE — merged and verified on canonical `main` via PR #28 at merge
commit `0a735e2c846f1769c69ba4010d62995ebd7499ad`.

- Add one per-device `ModbusTcp` driver implementation beneath
  `Scada.Drivers/ModbusTcp` and compose it through the existing Runtime-local
  resolver lease. Do not change the driver-neutral Runtime polling contracts.
- Support read-only FC01, FC02, FC03 and FC04 with one serialized connection per
  configured device, cooperative cancellation and existing bounded Runtime
  reconnect/shutdown behavior.
- Use explicit zero-based logical address grammar and deterministic register
  decoding. Driver-specific range planning may merge contiguous requests but
  must obey 2,000-bit and 125-register protocol maxima.
- Add driver/device/tag engineering validation without direct UI PLC reads or
  fabricated register discovery.
- Preserve copy-folder portability: source, central package pin, documentation,
  tests and offline package discovery all travel with a complete copied folder.
- Verify full tests, vulnerability audit, WPF startup, fresh copy restore/build,
  publish/offline package flow, GitNexus cycles and the Runtime/Core boundary.

M17 does not include Modbus RTU, PLC Write functions, live hardware
certification, Siemens/Mitsubishi/OPC UA drivers, runtime hot reload, MQTT Write,
Trend, Reports, Recipes or M18 scope.

## Remaining Architecture V1 Coverage After M17

The complete requirement-by-requirement matrix is maintained in `docs/V1_COVERAGE.md`. It records 50 audited areas: 37 `COMPLETE`, 8 `PARTIAL`, 0 `NOT STARTED` and 5 `EXPLICITLY DEFERRED`.

The remaining work is intentionally described as coverage, not as an authorization to start a new milestone:

1. `PARTIAL` — real protocol-aware Address Browser/device-selection extension; broader HMI catalog; external asset packaging; Engineering Devices/Trend route coverage beyond the M13 device editor; Simulator scenario qualification; consistent RuntimeId logging context; and dynamic/persisted screen composition beyond the M15 static catalog.
2. `EXPLICITLY DEFERRED` — production Siemens/Mitsubishi/OPC UA drivers, Modbus RTU/write functions, MQTT Write/command subscriptions, Trend, Recipes/Calibration, Reports, and distributed/Web/cloud/HA/scripting/plugin systems.

### Candidate ordering for a future milestone (proposal only)

If a future planning gate is opened, a dependency-aware review could consider:

1. remaining operational engineering coverage (broader device lifecycle and Overview/Status Bar extensions);
2. concrete protocol-driver qualification or additional HMI/asset coverage;
3. separately approved monitoring or HMI extensions such as Trend or additional asset support.

This is sequencing guidance only. M17 does not authorize another production
driver, any write path, installer, Trend or M18 implementation.

## Explicitly deferred

Do not implement without separate approval:

- distributed multi-runtime;
- redundancy or HA;
- web frontend;
- cloud architecture;
- advanced scripting;
- plugin marketplace.
