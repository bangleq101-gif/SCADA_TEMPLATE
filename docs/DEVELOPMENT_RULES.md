# SCADA Development Rules

- Keep project references one-way and verify that no circular dependency is introduced.
- Keep `Scada.Runtime` independent from WPF, `Scada.App`, `Scada.Drivers` and concrete drivers.
- Put protocol-specific behavior under `Scada.Drivers`; do not add `if Siemens`/`if Simulator` branches to Runtime.
- Keep `DeviceDefinition` static. Put connection state, errors and statistics in Runtime state objects.
- Route acquired values and quality transitions through the central `TagCache`; UI must not read a PLC directly.
- Use `AppContext.BaseDirectory` for runtime file resolution. Do not use absolute or working-directory-dependent paths.
- Keep external service integrations, real PLC drivers, Tag Manager and advanced HMI features outside the approved milestone.

## Runtime polling rules

- `IPlcDriver` remains asynchronous, batch-oriented and `CancellationToken`-aware.
- Driver resolution belongs to Runtime orchestration; `IPlcDriverResolver` must not be moved into Core without an explicit architecture decision.
- The resolver hides shared versus per-device driver lifetime behind `IPlcDriverLease`.
- A device worker reuses its acquired driver instance across reconnect attempts.
- Shared driver instances must be thread-safe. A per-device driver instance is owned by one worker.
- One naturally asynchronous worker and one scheduler are used per enabled device; do not create a dedicated OS thread or timer task per scan group.
- Runtime batching groups tags by device and scan group. Protocol-specific block/range optimization belongs in concrete drivers.
- A non-cooperative driver cannot be force-killed by Runtime. Concrete drivers must honor cancellation and provide suitable transport-level timeouts.
- Shutdown must use a bounded host/cleanup budget. Do not let one faulty device block shutdown indefinitely.
- Do not dispose a driver lease while a non-cooperative operation is still in flight.
- Device state is exposed through immutable snapshots, not mutable runtime state objects.
- For a disconnect transition, a tag with a valid cached value keeps that value and its original PLC timestamp. A tag without a valid cached value receives `null` and the transition timestamp.

## Verification rules

- Run restore, Release build and tests before reporting a milestone complete.
- Run `git diff --check` before review.
- Use GitNexus impact analysis before changing existing runtime symbols and review post-change dependency impact.
- Update `CURRENT_STATE.md`, `PROJECT_STRUCTURE.md` and `DECISIONS.md` when implementation changes documented architecture.
- Do not commit generated `bin/`, `obj/`, `TestResults/`, logs, databases, secrets or copy-verification folders.
