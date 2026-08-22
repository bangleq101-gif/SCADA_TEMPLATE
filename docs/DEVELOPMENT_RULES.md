# SCADA Development Rules

- Keep project references one-way and verify that no circular dependency is introduced.
- Keep `Scada.Runtime` independent from WPF, `Scada.App`, `Scada.Drivers` and concrete drivers.
- Put protocol-specific behavior under `Scada.Drivers`; do not add `if Siemens`/`if Simulator` branches to Runtime.
- Keep `DeviceDefinition` static. Put connection state, errors and statistics in Runtime state objects.
- Route acquired values and quality transitions through the central `TagCache`; UI must not read a PLC directly.
- Use `AppContext.BaseDirectory` for runtime file resolution. Do not use absolute or working-directory-dependent paths.
- Keep external service integrations, real PLC drivers and advanced HMI features outside the approved milestone unless explicitly scheduled. Milestone 4 owns the bounded WPF Tag Manager foundation only.

## Shell and workspace rules

- Keep canonical route keys in `NavigationService`; `CurrentRouteKey` is the single source of truth for the active workspace.
- Navigation groups are non-navigable. Exactly one canonical leaf is selected, and Shell selection is synchronized from the NavigationService state.
- Navigation transitions must deactivate the old workspace, update the route/view model coherently and activate the destination. Invalid and same-route navigation must not change lifecycle state.
- Keep workspace lifecycle abstractions in `Scada.App`; do not move UI navigation or lifecycle contracts into Core or Runtime.
- Monitoring must own TagCache subscriptions only while active. Activation is idempotent, deactivation disposes owned subscriptions, and queued callbacks must re-check their activation generation before updating rows.
- WPF Dispatcher marshaling belongs in `Scada.App`. Views and ViewModels must consume TagCache data and must not read PLCs directly.
- Reuse `WorkspaceLayout` and ResourceDictionary styles for workspace page structure and semantic colors. Do not add a third-party UI framework for the Shell.
- Product UI must not expose milestone, foundation, placeholder or fabricated health-status text.

## Tag Manager rules

- Use an explicit absolute project path. Do not search parent folders, infer a source-tree project or fall back to the application output directory.
- Treat `project.json` schema version 1 as the whole project-document authority. Preserve tag order and save atomically; do not silently replace malformed or invalid existing documents with defaults.
- Keep startup, saved and working project snapshots isolated through deep cloning. Mark runtime-affecting edits restart-required; do not add hot reload or runtime reconfiguration in this milestone.
- Keep Tag Manager editing in `Scada.App`; it must not read PLCs or add a second runtime data path.
- Use one disposable selected-row TagCache subscription for runtime quality observation. Do not create a subscription per row or fan out to the whole table.
- Keep validation rules in the shared Core validation source so load and edit behavior agree. Blocking issues reject save; warning metadata is preserved.
- Use deterministic quoted CSV/TSV codecs for interchange. Do not use naive `Split(',')` parsing and do not place JSON on the primary clipboard path.

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
- App behavior is covered with deterministic unit tests; UI automation is not required for this milestone.
