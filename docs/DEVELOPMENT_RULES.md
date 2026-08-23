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
- Machine Settings parameter values are canonical Core text values. Page Apply must validate all drafts before one `ProjectEditSession.MarkChanged()` call; no editor mutates the project or writes a PLC per keystroke. Optional live values use logical TagCache IDs only and active-page subscription ownership.
- Product UI must not expose milestone, foundation, placeholder or fabricated health-status text.

## Tag Manager rules

- Use an explicit absolute project path. Do not search parent folders, infer a source-tree project or fall back to the application output directory.
- Treat the versioned `project.json` document as the whole project-document authority. Preserve tag order and save atomically; migrate v1 → v2 → v3 in memory without silently replacing malformed or invalid existing documents with defaults.
- Keep startup, saved and working project snapshots isolated through deep cloning. Mark runtime-affecting edits restart-required; do not add hot reload or runtime reconfiguration in this milestone.
- Keep Tag Manager editing in `Scada.App`; it must not read PLCs or add a second runtime data path.
- Use one disposable selected-row TagCache subscription for runtime quality observation. Do not create a subscription per row or fan out to the whole table.
- Keep validation rules in the shared Core validation source so load and edit behavior agree. Blocking issues reject save; warning metadata is preserved.
- Use `ProjectDocumentSchema.CurrentVersion` as the single schema version source. v1 migration is in-memory and explicit Save is the upgrade boundary; do not scatter schema literals through App or Infrastructure.
- Use deterministic quoted CSV/TSV codecs for interchange. Do not use naive `Split(',')` parsing and do not place JSON on the primary clipboard path.
- Treat CSV/TSV import as a prepare/decide/apply transaction. Never silently suffix, overwrite or regenerate a supplied conflicting Id/name.
- Keep editor option lists separate from filter option lists; `All` is a filter sentinel and is never an editable DeviceId or ScanGroup value.
- Model bulk fields as `Unchanged`, `Mixed` or `Explicit`; apply only explicit fields to one cloned candidate and validate once.
- Seed row quality from one central TagCache snapshot per row. Keep live subscriptions limited to the selected persisted tag and invalidate every old selection generation.
- Destructive delete requires an App-layer confirmation adapter; cancellation must leave the working project unchanged.

## Historian rules

- Historian consumes the central `TagCache` only. It must not read PLCs, call `IPlcDriver`, perform SQLite I/O in callbacks, or make polling await storage.
- Keep `IHistoryStore`, `HistorySample` and query contracts in `Scada.Core`; keep SQLite provider types, SQL and project-relative path resolution in `Scada.Infrastructure`.
- Keep the four required built-in profiles centralized and use a case-insensitive registry. A present `Historian:Profiles` configuration collection is authoritative; an absent collection retains defaults.
- Normalize values by declared tag type before enqueueing. Reject malformed and non-finite values without broad culture conversions. Preserve `TagValue.Timestamp` as the source timestamp and use a separate wall-clock `RecordedAtUtc`.
- Use monotonic `TimeProvider.GetTimestamp()` semantics for minimum intervals, maximum periodic due times and retry timing. Never persist monotonic timestamps.
- Subscribe before seeding from `ITagCache.TryGet`; use runtime-local `TagValue.Sequence` for duplicate suppression, never as a SQLite key.
- TagCache callbacks may only evaluate and call bounded queue `TryWrite`. Use `SingleReader`, `SingleWriter=false`, `FullMode=Wait` and `AllowSynchronousContinuations=false`; never block callbacks on storage.
- Keep `RejectedSamples`, `DroppedSamples`, `AbandonedSamples` and `WrittenSamples` separate. Count writes only after the SQLite transaction commits.
- Historian startup must use cancellation-aware bounded preflight, validate path/profile state, subscribe before launching background work and return without synchronous SQLite retry. Recoverable preflight continues in Degraded state while background initialization retries with capped exponential monotonic delays. Register Historian hosted service before polling and resolve one singleton instance for runtime status UI.
- Keep one coordinator with one schedule-change signal; an earlier due time must wake the coordinator, and an accepted periodic next-due decision remains scheduled even if the current queue write is dropped.
- Protect evaluator state per tag. Different tags must not share a global evaluation lock; callback and periodic evaluation for the same tag must remain serialized.
- On shutdown stop intake, dispose subscriptions, stop scheduling, complete and drain the queue within the configured budget, then cancel the writer. Cancellation is cooperative; do not force-kill non-cooperative storage work.
- SQLite history uses schema `user_version=1`, typed value columns, no `AUTOINCREMENT`, one connection/transaction per batch and deterministic `RecordedAtUtcTicks, Id` query order. Every write connection must apply finite `busy_timeout` and `synchronous=NORMAL`; disabled Historian must not create a database.
- `IHistoryStore.PreflightAsync(CancellationToken)` is the compatibility boundary. A store must honor cancellation; Runtime still bounds startup waiting and does not perform SQLite retry loops in `StartAsync`.
- History Settings edits `ProjectEditSession.WorkingProject`, protects built-in profile names/deletion, rejects reserved/duplicate custom renames without mutation, surfaces shared validation and save failures, persists through the normal Save boundary and displays only the running Historian snapshot. No hot reload is allowed.

## InfluxDB provider rules

- SQLite remains the default history provider. InfluxDB 2.x options live in Core, while `InfluxDB.Client`, transport, outbox SQL and provider-specific diagnostics remain in Infrastructure. Do not add an Influx dependency to Runtime or change the TagCache flow.
- Store only environment-variable token references such as `env:SCADA_INFLUX_TOKEN`. Never persist, log or display plaintext tokens, and report missing secrets as configuration state.
- Persist pending samples in the typed SQLite outbox using deterministic SHA-256 sample keys. Capacity is global across destination fingerprints, and duplicate/idempotent rows must not consume capacity twice. Acknowledge only after an explicit successful remote write; do not enable a hidden SDK write queue.
- Destination fingerprints exclude tokens. Remote timestamp counters are persisted per destination/runtime/tag, and counters/diagnostics survive acknowledgement and buffer clearing. Do not automatically clear or migrate a previous destination buffer.
- Use `HistorySample.RecordedAtUtc` for remote nanosecond `_time`; query exact recorded ticks and widen only the remote scan window for timestamp rollback. History queries are remote-only and do not include pending local outbox rows.
- Preserve rows for generic 400, 429, 5xx, timeout, offline and configuration failures. Only a transport-confirmed point-specific 400 may be bounded-split and terminally reject an isolated poison row; later rows must continue.
- Retention changes are explicit maintenance operations. Startup and Test Connection are non-mutating; the History Settings Test Connection checks a candidate configuration without writing data. Remote failure must not stop local buffering or PLC polling.
- Do not resolve an Influx buffer path until local preflight or initialization; never fall back to the working directory or application output directory. A missing canonical project path is a structured `PROJECT_PATH_REQUIRED` store fault.
- Map the official InfluxDB.Client exception/status types directly. Do not use reflection or provider response-text parsing, and do not label a generic production HTTP 400 as point-specific.
- Keep connection, write and query operation timeouts separate. Cancellation remains cooperative; the transport must honor the token and its operation-level timeout.
- Treat the SQLite append commit as the durable write success boundary. Do not add a second cancellable diagnostics operation after commit that can turn a committed batch into a failed caller operation.
- Use a bounded one-bit work signal. Repeated appends coalesce wake-ups; they must not accumulate unbounded semaphore releases.
- Scope persisted diagnostics to the current destination fingerprint; expose other fingerprints through an orphan count rather than summing their counters into the active destination.
- Reject tag control characters and string carriage-return/line-feed values before line-protocol encoding. Do not encode newline characters into a different value.
- Use the exact signed InfluxDB nanosecond bounds for allocation and clamp or reject out-of-range query windows without a timestamp-zero fallback.
- Serialize current-destination buffer clearing with the complete remote synchronization read/write/ack window. New local appends remain durable and are not treated as remote acknowledgements.
- Apply retention from a cloned working-project candidate through an App-layer service; do not apply the active runtime store's settings when the user is editing an uncommitted candidate.
- WPF maintenance commands must be asynchronous, non-overlapping, cancellation-aware and guarded against late completion after workspace deactivation. Keep UI property updates on the WPF synchronization context.

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
