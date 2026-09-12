# Changelog

## [1.0.2] — 2026-09-12

### Fixed

- Share runner test inputs across suites in the same processor package, preserving the selection when switching between unit, read-only live and control tests. Keep other processors and packages isolated, migrate compatible saved selections, and preserve explicit clearing.

## [1.0.1] — 2026-09-12

### Fixed

- Refresh discovered package addresses and ports before connecting, and recover dropped connections after package restarts or redeployments.
- Update connected packages through Find packages without retaining a duplicate stale endpoint. Preserve previous results and never replay tests during reconnection.
- Prevent ILRepack from combining unrelated private resource helpers or compiler-generated anonymous types across dependencies, which could corrupt JSON payloads and error handling on the processor.

### Validation and documentation

- Add regression coverage for endpoint changes and recovery, including ambiguous/missing packages, retained results and explicit disconnect.
- Validate the merge corrections with 233 offline and six live OverkizClient tests passing on a processor.
- Update runner behavior and upgrade instructions; include this changelog and validation history in the documentation archive.

## [1.0.0] — 2026-09-11

- Initial Windows runner with mDNS package discovery, processor authentication, suite/test selection, private inputs, results and saved preferences.
- Self-contained .NET 10 Windows distribution and net472 NUnit Test Host with standalone Home tiles in the Utility category.
- Shared SDK for packaging NUnit test assemblies, Visual Studio build/deployment support, and documentation and third-party license notices.

[1.0.1]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.1
[1.0.0]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.0

[1.0.2]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.2
