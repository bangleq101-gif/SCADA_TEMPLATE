
# Architecture Decisions

## D-001 — Copy-folder portability

`SCADA_TEMPLATE` is copied as a complete folder to create a new SCADA project. The copied repository must build and run independently. Project references are relative, and no internal SCADA source dependency may exist outside the repository.

## D-002 — V1 project boundaries

V1 uses the following five primary projects:

```text
Scada.Core
Scada.Runtime
Scada.Drivers
Scada.Infrastructure
Scada.App
```

The architecture should not be split into excessive projects unless a future requirement justifies it.

## D-003 — Single runtime and target scale

V1 uses one Runtime and targets dozens of PLCs and approximately 10,000 tags. Distributed multi-runtime is intentionally deferred.

`Scada.Runtime` must not depend on WPF so it can potentially be separated later.

## D-004 — Central TagCache data flow

PLC data flows through:

```text
PLC → Driver → Polling → TagCache
```

UI, Alarm, Historian and MQTT consume TagCache values. They must not independently reread PLC data.

## D-005 — Polling architecture

Runtime uses Batch Read, Scan Groups, asynchronous device isolation and UI subscriptions. A disconnected PLC must not block other PLCs.

## D-006 — Configuration and historian profiles

Normal engineering uses the WPF Tag Manager rather than manually editing tag JSON. Historian configuration uses Digital, Analog, Fast Analog and Custom profiles, with advanced options hidden by default. SQLite and InfluxDB are supported storage targets.

## D-007 — Optional external services

SQLite is local application storage. MQTT Broker and InfluxDB Server are optional external services. Their failure must not stop PLC polling or normal SCADA operation. Historian writes use a queue/background writer.

## D-008 — MQTT behavior

Selected TagCache values may be published through MQTT. MQTT must not cause extra PLC reads, topics should normally be generated automatically, and MQTT Write is disabled by default.

## D-009 — WPF engineering and machine UI

Machine/process UI is designed manually in WPF/Visual Studio. The template supplies reusable Controls and Faceplates. Operation, Machine Settings, Monitoring and Engineering are distinct concepts; configuration UI must remain compact.

## D-010 — External symbols

External symbol libraries such as Symbol Factory are graphic sources only and must not become core architecture dependencies.

## D-011 — Versioned project knowledge

Architecture, current state, roadmap and decisions live inside the Git repository and are versioned together with the code.
