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

## Comparison policy

Automatic provisional repository regression verdicts require compatible machine/CPU, OS/.NET, workload version, scenario/configuration hash, power mode, seed and harness durations. Cross-machine results are observational only. The provisional relative thresholds are not deployment SLAs; product acceptance limits remain unestablished until baseline evidence is reviewed.

No optimization is authorized during Phase A. A measured hotspot is reported with evidence and a hypothesis, then waits for a separate review gate.
