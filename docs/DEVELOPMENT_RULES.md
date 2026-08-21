# Milestone 1 Development Rules

- Keep project references one-way and verify that no circular dependency is introduced.
- Keep `Scada.Runtime` independent from WPF, `Scada.App` and concrete drivers.
- Put protocol-specific behavior under `Scada.Drivers`; do not add `if Siemens`/`if Simulator` branches to Runtime.
- Keep `DeviceDefinition` static. Put connection state, errors and statistics in Runtime state objects.
- Route acquired values through the central `TagCache`; UI must not read a PLC directly.
- Use `AppContext.BaseDirectory` for runtime file resolution. Do not use absolute or working-directory-dependent paths.
- Keep external service integrations, real PLC drivers, Tag Manager and advanced HMI features outside Milestone 1.
- Run restore, Release build and tests before reporting a milestone complete.
- Update `CURRENT_STATE.md`, `PROJECT_STRUCTURE.md` and `DECISIONS.md` when implementation changes the documented architecture.
