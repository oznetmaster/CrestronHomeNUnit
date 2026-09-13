# Crestron Home NUnit v1.1.0

This minor release adds a processor-test CLI and an optional gated development workflow: local tests, retained Debug packages, SFTP deployment, install/update readiness, processor and live tests, actual-driver update after passing gates, read-only installed-driver checks, and optional test-instance removal. Uncertain operations retain evidence and a processor lease instead of silently proceeding.

See the [continuous integration guide](docs/ContinuousIntegration.md) for setup, example plans, private SDK/settings handling and recovery. Workflow support is included in the self-contained CLI release and consumes CrestronHomeDevTools 1.0.0 from NuGet. No adjacent DevTools checkout is required. A Visual Studio Test Explorer adapter is still planned; it is not part of this implementation. Opt-in reboot-aware lifecycle handling now supports V1 development workflows; the Apple TV V1 update/reboot cycle has passed unattended hardware validation; initial-install/removal reboot paths remain hardware-unverified. Automatic rollback is not included.

Validation includes a complete KasaTapo gated workflow and 400 distinct tests across six driver packages passing twice on the development processor. The initial full KasaTapo workflow used its then-current 57-test suite. These records demonstrate different validation stages, not one combined run.

## Downloads and updating

Extract the complete Windows runner or CLI ZIP. Both include their .NET 10 runtime. Existing runner preferences and protected credentials are retained. The CLI ZIP provides `CrestronHomeNUnit.Cli.exe`; use `--help` for commands and the included CI guide for private workflow plans. The test-host package is version 1.1.000.0000; NUnit remains the official 4.6.1 package. The runner's licensed icon is embedded only; no licensed source icon is distributed.

CP4-R validation also passed temporary Entity V2 installation/update/reload, configuration reboot/recovery, 116 tests before and after restart, and cleanup. V1 update evidence remains MC4-R only. See the DevTools compatibility matrix for the precise hardware scope.

Release validation covers the runner/transport, CLI client and workflow regressions, plus the packaged NUnit self-test and language-compatibility suites. Processor test packages remain GitHub-only assets. Private settings, local paths, credentials and results are excluded from releases.
