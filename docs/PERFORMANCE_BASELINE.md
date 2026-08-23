# SCADA V1 Performance Baseline

Milestone 10 Phase A is measurement-first. This document defines the reproducible harness and evidence contract; it does not claim that an optimization has succeeded.

## Qualification profiles

- `RuntimeBaseline`: polling and TagCache only.
- `HistorianHeavy`: polling, TagCache and local SQLite Historian.
- `MqttHeavy`: polling, TagCache and the deterministic in-memory MQTT transport.
- `UiActive`: polling, TagCache, existing Monitoring subscriptions and a real WPF Dispatcher.
- `CombinedWorstCase`: all normal subsystems above.

The qualification workload is 50 Simulator devices and 10,000 tags. Scan cadence and deterministic value-change intensity are separate configuration dimensions. The default Phase-A pattern changes each value every fourth read while every successful scan still updates TagCache.

## Run protocol

Run Release outside the debugger on AC power without concurrent build/test activity. RuntimeBaseline, HistorianHeavy, MqttHeavy and UiActive use 60 seconds warm-up plus 5 minutes measurement, repeated three times. CombinedWorstCase uses 2 minutes warm-up plus 10 minutes measurement, repeated three times.

`scripts/run-stress.ps1 -Smoke` is only a 5-device/1,000-tag correctness and instrumentation smoke. It is not M10 performance qualification.

Generated JSON, summaries, CSV, SQLite databases and traces live under ignored `artifacts/stress/<run-id>/`. Raw artifacts are not committed.

## Measurement contract and hard gates

Phase A qualification uses result schema `2` and measurement contract `m10-phase-a-v3`. The measurement contract is part of the compatibility fingerprint, so evidence captured under an earlier contract is observational only.

Qualification receives an explicit repository root and expected commit SHA. It rejects a mismatched `HEAD` or a dirty unignored working tree before execution, and records repository root, exact SHA and clean state in the result.

A qualification result passes only when configured device/tag counts and TagCache value count match; polling failures and missed cycles are zero; Historian rejected/dropped/abandoned/write-failure counters are zero and persisted rows equal measurement-local written samples; MQTT causes no PLC reads and has non-decreasing observable source timestamps; Dispatcher heartbeat gaps are zero; shutdown completes; and zero TagCache subscriptions remain after shutdown. A violation is persisted in `correctness.violations`, sets `correctness.passed` false and makes the harness return a non-zero exit code.

Historian evidence deliberately reports three distinct counters: `ServiceWrittenSamples` is the runtime-service counter delta, `MeasurementSamplesWritten` is the `TimedHistoryStore` measurement-local count, and `PersistedRows` is the persisted measurement-window row count. Only `MeasurementSamplesWritten == PersistedRows` is the hard persisted-data correctness invariant; service writes that began before the boundary may complete afterward and are reported separately.

Historian and MQTT latency/counter collectors begin at the measurement boundary. Their queue/pending high-water values are sampled once per second and are explicitly named `sampled*HighWater`; they are not claims of an exact transient peak.

Percentiles use a bounded eight-sub-bucket-per-octave histogram. They are bucket upper-bound estimates, not retained raw samples. This resolution keeps the declared 20% regression rules meaningful around the 2 ms polling-jitter and 5 ms Dispatcher noise floors.

## Comparison policy

Automatic provisional repository regression verdicts require compatible machine/CPU, OS/.NET, workload version, scenario/configuration hash, power mode, seed and harness durations. Cross-machine results are observational only. The provisional relative thresholds are not deployment SLAs; product acceptance limits remain unestablished until baseline evidence is reviewed.

For compatible three-run aggregates, median CPU and working set may increase at most 15%; updates/sec and Historian throughput may decrease at most 10%; sampled Historian queue high-water may increase at most 20%; scan-jitter p95 and Dispatcher p95 may increase at most 20% only when their absolute increase also exceeds 2 ms and 5 ms respectively. These are same-environment repository regression rules, not product SLAs.

No optimization is authorized during Phase A. A measured hotspot is reported with evidence and a hypothesis, then waits for a separate review gate.
