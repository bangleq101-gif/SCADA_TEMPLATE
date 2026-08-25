# Architecture V1 Coverage

This document reconciles the original Architecture V1 specification with the
implementation on canonical `main` after M11. It is a traceability document,
not an authorization to begin a new milestone.

## Audit baseline

- Audited main: `25ec87e91eba0be268384c7b941c63cb8bb0f6d9`.
- Original Architecture V1 reference: `09d48864365a2062f73220a158e47951608ac358`.
- M11 base merge: PR #15, `4b903723ed94d846420c2bf3867eec18a395d1c4`.
- M11 alignment approved head: PR #16, `636e8fb16080f29e98d3ea976e5e584e1abe7887`.
- M10 qualified benchmark authority: `402ee9d46f41489fee8912bbed57dc1388550658`.
- M11 merged-main verification: 397/397 tests, GitNexus 3,806 nodes / 13,067 edges / 150 clusters / 300 flows / 0 cycles, and `Scada.Runtime → Scada.Core ONLY`.

The statuses below describe delivered coverage, not production certification:

- `COMPLETE` — the planned V1 capability is implemented and has direct source and/or test evidence.
- `PARTIAL` — a meaningful foundation exists, but one or more V1 details remain.
- `NOT STARTED` — no implementation was found on the audited baseline.
- `EXPLICITLY DEFERRED` — intentionally outside the approved milestone sequence or separately gated.

## Requirement matrix

| # | V1 Area / Requirement | Original section | Status | Implementation evidence | Test/evidence | Remaining gap | Decision / proposed next step |
|---:|---|---|---|---|---|---|---|
| 1 | Copy-folder reusable workflow and portable project boundary | §§1, 59 | `COMPLETE` | `AppContext.BaseDirectory`, project-relative paths, explicit project-file workflow | Copy-folder restore/build/startup and original-path scan passed | Automatic project discovery is not provided | Keep copyability as a release gate |
| 2 | Five product projects and relative references | §2 | `COMPLETE` | `Scada.Core`, `Scada.Runtime`, `Scada.Drivers`, `Scada.Infrastructure`, `Scada.App` in `Scada.sln` | Solution restore/build and project tests pass | No product project is required beyond the V1 set | Preserve the five-project boundary |
| 3 | One-way dependency architecture and no cycles | §3, §§65-66 | `COMPLETE` | Product references are layered; composition is in App | GitNexus reports 0 cycles | Future additions must preserve the graph | Require impact and cycle checks for every milestone |
| 4 | Core, Runtime, Drivers, Infrastructure and UI boundary | §§3-5, 13, 16 | `COMPLETE` | Runtime references Core only; concrete storage and WPF remain outside Runtime | Runtime boundary scan passed on merged main | No boundary gap found | Do not move WPF or concrete transport into Runtime |
| 5 | Driver-neutral `IPlcDriver` and Simulator foundation | §§5, 60 | `COMPLETE` | Async batch driver contract and deterministic smooth Simulator under `Scada.Drivers/Simulator` | Simulator and driver-neutral contract tests pass | Simulator does not generate a dedicated fault-state mode | Keep fault injection in test/runtime seams until separately approved |
| 6 | Siemens, Mitsubishi, Modbus TCP and OPC UA drivers | §5, future scope | `EXPLICITLY DEFERRED` | No production vendor driver is included | No real-driver qualification exists | Protocol-specific connection and address semantics remain undefined | Approve each concrete driver scope separately |
| 7 | Batch Read, Scan Groups, asynchronous device isolation and scale architecture | §§6-10, 61 | `COMPLETE` | `DeviceManager`, per-device workers, planned scheduling and logical device/scan-group batching | Runtime polling, isolation and stress contract tests pass | Protocol-specific range optimization remains driver-owned | Preserve one worker per device and bounded scheduling |
| 8 | M10 qualified 50-PLC / approximately 10,000-tag evidence | §§6-7, 61 | `COMPLETE` | Phase A qualification harness and authoritative v3 baseline | 15 compatible runs passed under `402ee9d46f41489fee8912bbed57dc1388550658` | Baseline is not a production SLA | Do not reinterpret or recapture without a new gate |
| 9 | `RuntimeId`, one Runtime and central data flow | §§11-14 | `COMPLETE` | Runtime identity, polling flow and central runtime services | Core/runtime tests and merged-main verification pass | Distributed runtime is not part of V1 | Keep one Runtime as the V1 operating model |
| 10 | Central TagCache, subscriptions and no consumer PLC rereads | §§11-13 | `COMPLETE` | Thread-safe TagCache, disposable subscriptions and last-known-value quality semantics | TagCache, polling, MQTT, Historian and Alarm tests pass | No new consumer-specific cache is required | Keep TagCache as the sole live-value source |
| 11 | Historian profiles, queue, background writer and SQLite | §§15, 17-27 | `COMPLETE` | Core profiles, Runtime queue/coordinator and Infrastructure SQLite store | Historian and SQLite test suites pass | Runtime hot reload remains deferred | Use profile-based configuration and bounded queues |
| 12 | InfluxDB provider, buffering, resync, retention and health | §§16, 22-27 | `COMPLETE` | Buffered Influx store, outbox, transport, retention and connection test paths | Influx provider tests and package audit pass | Live remote Influx integration has not been qualified | Treat remote integration as a separately scheduled test boundary |
| 13 | Historian failure isolation | §§15-16 | `COMPLETE` | Storage failures are isolated from polling/runtime ownership | Failure, retry, outbox and shutdown tests pass | Multi-process outbox writers remain unsupported | Preserve non-blocking runtime behavior |
| 14 | Tag Manager workflows, metadata, CSV and approximately 10,000 tags | §§17-20 | `COMPLETE` | Add/edit/delete/duplicate/search/filter/sort/bulk/copy-paste/import/export and virtualized DataGrid | Tag Manager and large-dataset tests pass | Advanced address scaling and runtime reconfiguration remain later work | Keep WPF Tag Manager as the engineering workflow |
| 15 | Device selection and Address Browser | §§18-19 | `PARTIAL` | Tag rows support configured device selection and preserve unknown references for repair | Tag editing and validation tests cover the current selection path | No protocol-aware Address Browser exists | Design a vendor-neutral browser only with a concrete driver contract |
| 16 | Online Tag Monitor | §21 | `COMPLETE` | Monitoring workspace consumes TagCache with active-view lifecycle | Monitoring lifecycle and WPF tests pass | No additional gap identified for the V1 foundation | Preserve active-only subscriptions |
| 17 | MQTT publisher, settings, profiles, topics, payload and health | §§28-33 | `COMPLETE` | Runtime publisher, MQTTnet Infrastructure transport, settings UI, automatic topic/payload and Test Connection | MQTT runtime/settings/reconnect/coalescing tests pass | MQTT Write is not part of this capability | Keep publisher read-only and TagCache-driven |
| 18 | MQTT Write and command subscriptions | §33, future scope | `EXPLICITLY DEFERRED` | No write or command subscription path is composed | Absence verified by source/boundary review | Safety, authorization and interlock semantics are unspecified | Require a separate simulator-first approval |
| 19 | Operation, Machine Settings, Monitoring, Engineering, design system and compact UI | §§34-43 | `COMPLETE` | Four workspace groups, shared resources/styles and compact settings layouts | Shell, workspace and WPF resource tests pass | Unified system-health surfaces remain partial | Extend only through the existing App design system |
| 20 | Module, Line and Machine multi-screen organization | §50 | `PARTIAL` | Hierarchical shell and internal Machine Settings/HMI composition exist | Navigation and workspace tests pass | No generic module/line/machine screen composition model | Define metadata and hierarchy together before implementation |
| 21 | Reusable HMI control catalog | §§44-49 | `PARTIAL` | Motor, Pump, Valve, Tank, Pipe, Conveyor and Indicator are read-only state-aware controls | HMI template/state/accessibility tests pass | Additional equipment and display controls are not provided | Add only from an approved equipment catalog |
| 22 | Logical tag controls, faceplates and vendor-neutral boundary | §§44-49 | `COMPLETE` | `HmiEquipmentContext`, logical roles, shared faceplates and non-owning host | HMI context, lifecycle and faceplate tests pass | No write/command interaction is included | Keep controls unaware of PLC addresses |
| 23 | External symbol assets and licensing boundary | §49 | `PARTIAL` | Architecture and decisions define external assets as graphic sources only | Boundary review confirms no vendor package dependency | No packaged Symbol Factory or licensed asset library is shipped | Add vendor-neutral fallback assets and explicit license handling later |
| 24 | Navigation groups and actual route coverage | §§50-51 | `PARTIAL` | Shell provides Operation, Machine Settings, Monitoring and Engineering routes, including Alarm/History/MQTT/Tag Manager | Shell navigation tests pass | Engineering Devices/System/Diagnostics and Trend routes are absent | Add routes only with corresponding workspace contracts |
| 25 | Engineering Devices UI | §51 | `NOT STARTED` | No dedicated Engineering Devices workspace was found | No implementation test exists | Device diagnostics/editing UX is undefined | Candidate operational surface after separate planning |
| 26 | Engineering Tags UI | §§17-20, 51 | `COMPLETE` | `TagManagerViewModel` and `TagManagerView` provide the engineering tag workflow | Tag Manager functional and scale tests pass | Advanced address browsing remains partial | Preserve current logical-tag editing model |
| 27 | Engineering History UI | §§22-27, 51 | `COMPLETE` | `HistorySettingsViewModel` and view expose profiles, storage and connection actions | History settings and provider tests pass | Live remote integration remains a separate boundary | Keep write/test-connection semantics explicit |
| 28 | Engineering MQTT UI | §§28-33, 51 | `COMPLETE` | `MqttSettingsViewModel` and view expose publisher configuration and Test Connection | MQTT settings and runtime tests pass | MQTT Write UI is intentionally absent | Keep publisher-only scope |
| 29 | Engineering System UI | §51 | `NOT STARTED` | No dedicated System workspace was found | No implementation test exists | System configuration/health ownership is undefined | Define the minimal read-only surface before coding |
| 30 | Engineering Diagnostics UI | §51 | `NOT STARTED` | No dedicated Diagnostics workspace was found | No implementation test exists | Cross-subsystem diagnostics aggregation is undefined | Base it on existing runtime/store snapshots if approved |
| 31 | Alarm System and Alarm Monitoring | Current V1 M11 addendum §§14.1-14.2 | `COMPLETE` | Core Alarm contracts, Runtime evaluator, SQLite store, Engineering/Monitoring views and Operation summary | M11 merged-main verification: 397/397; Alarm-specific state/ACK/recovery/persistence/UI tests pass | Communication alarms and notifications remain deferred | Preserve PLC-read-only, exact-instance ACK and trusted recovery semantics |
| 32 | Monitoring Trend | §33 and future scope | `EXPLICITLY DEFERRED` | No Trend implementation is present | No Trend qualification exists | Historian query-to-trend UX and retention policy are not approved | Plan separately after a dedicated gate |
| 33 | Overview and System Health summary | §§34-42 | `PARTIAL` | Operation shows runtime and alarm summaries; runtime/store diagnostics exist | Operation and Alarm WPF tests pass | Complete PLC, Historian, MQTT, DB, CPU, memory and uptime health composition is not unified | Define a read-only health aggregation contract |
| 34 | Compact status bar health indicators | §43 | `PARTIAL` | Shell status bar has runtime status and shared styling | Shell resource/startup tests pass | PLC, Historian, MQTT, DB and resource indicators are not all exposed | Extend the existing status bar without reducing machine-display space |
| 35 | Machine Settings, ParameterEditor, ParameterGroup and typed validation | §§52-53 | `COMPLETE` | Project schema v5 settings, typed editors, canonical codec, transactional Apply and TagCache-only live values | Core, session, lifecycle, scale and WPF tests pass | PLC-backed Apply/Write is intentionally absent | Keep `ProjectEditSession` as the sole persistence/dirty authority |
| 36 | Recipes and Calibration workflow | §§52-53, future scope | `EXPLICITLY DEFERRED` | No recipe/calibration workflow is composed | No qualification exists | Domain model, audit and write safety are unspecified | Require separate domain and simulator-first approval |
| 37 | Screen metadata: ScreenId, Title, Category, Icon, Order and RequiredRole | §54 | `NOT STARTED` | Navigation items currently provide route/title semantics, not the full metadata contract | No generic metadata test exists | Metadata ownership and permission model are undefined | Define metadata before expanding screen organization |
| 38 | Deployment folder, publish scripts and environment checks | §§55-58 | `NOT STARTED` | Only launcher scripts exist; no deployment package/tooling was found | No deployment qualification exists | Installer, runtime prerequisites and environment validation are undefined | Treat deployment as an independent operational workstream |
| 39 | Offline NuGet/package and installation strategy | §§57-58 | `NOT STARTED` | No offline package source/cache or installation procedure is committed | No offline-install verification exists | Offline dependency provenance and update policy are undefined | Specify package provenance before building offline tooling |
| 40 | Simulator fault-state mode | §60 | `PARTIAL` | Deterministic smooth values and test/fake-driver failure seams exist | Simulator and polling failure tests pass | Simulator itself has no configurable fault/disconnect scenario mode | Add only if needed for a separately approved test or commissioning workflow |
| 41 | Optional external-service failure isolation | §§15-16, 28-33 | `COMPLETE` | Historian, Influx and MQTT failures are isolated from polling/normal operation | Runtime, provider, retry and shutdown tests pass | Multi-process coordination is not included | Preserve optional-service isolation as a boundary requirement |
| 42 | Reports | Future scope | `EXPLICITLY DEFERRED` | No Reports subsystem is present | No Reports qualification exists | Reporting model, export and retention requirements are undefined | Do not start without a separate scope and data-ownership review |
| 43 | Distributed runtime, Web/cloud, HA, advanced scripting and plugin marketplace | §§77-79, explicit future scope | `EXPLICITLY DEFERRED` | No such production architecture is present | Boundary review confirms these are not in the current solution | These would change deployment and ownership assumptions | Do not implement without separate architecture approval |
| 44 | Documentation, workflow, logging, configuration validation and test gates | §§62-67, 76 | `COMPLETE` | Versioned Architecture, Current State, Roadmap, Decisions, build/package policy and validation are in-repository | 397/397 merged-main verification, vulnerability audit, GitNexus and portability checks passed | Remaining coverage is tracked by this matrix | Keep docs and evidence versioned with each approved milestone |

## Summary

| Status | Count |
|---|---:|
| `COMPLETE` | 24 |
| `PARTIAL` | 8 |
| `NOT STARTED` | 6 |
| `EXPLICITLY DEFERRED` | 6 |
| **Total** | **44** |

The matrix identifies remaining Architecture V1 coverage; it does not select
M12 or authorize implementation. Any next milestone must first receive its
own plan and architecture gate.
