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

## Qualified Phase A execution

The authoritative Phase A execution was captured at:

- Qualified execution SHA: `402ee9d46f41489fee8912bbed57dc1388550658`
- Measurement contract: `m10-phase-a-v3`
- Workload: 50 simulated PLC/devices and 10,000 tags.
- Three compatible runs per profile; 15/15 valid runs total.
- `RuntimeBaseline`: 3/3; `HistorianHeavy`: 3/3; `MqttHeavy`: 3/3; `UiActive`: 3/3; `CombinedWorstCase`: 3/3.

The table reports measured medians. Scan duration and jitter are p50/p95/p99/max in milliseconds, summarized from the worst scan group in each run. Values are evidence for this qualified environment, not a production SLA.

| Profile | CPU % | Working set MiB | Managed heap MiB | Allocated GiB | Updates/s | Batches/s | Tags/s | Scan duration p50/p95/p99/max ms | Scan jitter p50/p95/p99/max ms | Shutdown ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| RuntimeBaseline | 0.36 | 58.04 | 15.99 | 2.72 | 32,556.67 | 667.17 | 32,556.67 | 0.10/1.28/3.84/47.37 | 8.19/16.38/36.86/73.64 | 128.42 |
| HistorianHeavy | 2.04 | 93.58 | 29.18 | 6.95 | 32,526.67 | 666.67 | 32,526.67 | 0.35/2.82/5.63/46.40 | 9.22/18.43/22.53/49.54 | 36.80 |
| MqttHeavy | 0.98 | 86.51 | 31.29 | 14.04 | 32,533.33 | 666.83 | 32,533.33 | 0.32/3.58/6.14/32.05 | 9.22/18.43/22.53/41.22 | 35.30 |
| UiActive | 2.17 | 81.62 | 26.95 | 11.23 | 32,527.60 | 666.68 | 32,527.60 | 0.35/2.56/4.61/47.86 | 9.22/18.43/26.62/43.75 | 12.11 |
| CombinedWorstCase | 3.97 | 154.11 | 57.65 | 52.79 | 32,530.40 | 666.80 | 32,530.40 | 0.58/4.61/8.19/48.64 | 10.24/20.48/32.77/68.98 | 34.72 |

Median GC collections (Gen0/Gen1/Gen2) were `491/474/18`, `1256/319/54`, `2707/1012/288`, `2005/802/65` and `9696/2288/591` for RuntimeBaseline, HistorianHeavy, MqttHeavy, UiActive and CombinedWorstCase respectively. Primary median ranges were: RuntimeBaseline updates 32,533.33–32,556.67/s and working set 57.64–58.04 MiB; HistorianHeavy 32,506.80–32,526.67/s and 90.27–93.58 MiB; MqttHeavy 32,510.13–32,533.33/s and 84.62–86.51 MiB; UiActive 32,519.07–32,527.60/s and 79.99–81.62 MiB; CombinedWorstCase 32,510.37–32,530.40/s and 152.44–154.11 MiB.

HistorianHeavy medians were `ServiceWrittenSamples=1,130,187`, `MeasurementSamplesWritten=1,129,931`, `PersistedRows=1,129,931`, sampled queue high-water `4,528`, 14.71 batches/s and write latency p50/p95/p99/max `6.66/57.34/81.92/134.56 ms`. CombinedWorstCase medians were `ServiceWrittenSamples=2,263,590`, `MeasurementSamplesWritten=2,263,590`, `PersistedRows=2,263,590`, sampled queue high-water `4,073`, 14.74 batches/s and write latency `7.68/49.15/57.34/129.34 ms`. All Historian rejection, drop, abandonment and write-failure counters were zero.

MqttHeavy median publish rate was 8,120/s with sampled pending high-water 10,000; CombinedWorstCase was 8,131.67/s with sampled pending high-water 8,301. Both had zero failures/reconnects, source-timestamp ordering PASS and zero PLC reads caused. UiActive Dispatcher latency was p50/p95/p99/max `0.11/5.12/13.31/23.57 ms`; CombinedWorstCase was `0.48/5.12/10.24/42.13 ms`; heartbeat gaps were zero.

## Qualified correctness and excluded evidence

All 15 qualified runs passed: missed cycles, polling failures, Historian rejected/dropped/abandoned/write failures, MQTT-caused PLC reads and Dispatcher heartbeat gaps were zero; `MeasurementSamplesWritten == PersistedRows` held for every applicable run; MQTT source ordering passed; shutdown completed; subscriptions after shutdown were zero; and all fingerprints were compatible.

`ABORTED_EXTERNAL_TERMINATION` runs had no `result.json` and are excluded. `INVALID_HARNESS_GATE_MAPPING` artifact `20260824-002727-HistorianHeavy-r1` used contract v2; its measurement-local samples matched persisted rows, but the obsolete harness mapping produced an invalid verdict. It is excluded and was never edited or reused. Older artifacts remain unchanged.

Controlled 1,000-value instrumentation smoke was PASS with instrumentation ON and OFF on the qualified SHA. OFF intentionally reported no measurement counters while preserving values and clean shutdown. This smoke is not part of the 50-device/10,000-tag qualification baseline.

No optimization was justified by measured evidence. CombinedWorstCase had zero missed cycles and no correctness violation; CPU, memory and update throughput do not identify a bounded hotspot requiring a change. Under the policy `MEASURE → HOTSPOT → ONE CHANGE → REMEASURE`, no optimization was performed. These results do not claim a production SLA or equivalent numbers on other hardware.

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
