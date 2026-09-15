# Crestron Home NUnit v1.2.1

Patch release completing the gated workflow's initial-installation path in the CLI and Visual Studio Test Adapter.

- Keep separate TRX results for every target framework in a multi-target library. Aggregate all required results and display them separately in Test Explorer, preventing one framework from overwriting or hiding another.
- Accept an optional private configuration file for an unconfigured actual driver. Support either named configuration values or an explicit ordered wizard. Validate the advertised items and steps, submit each once, and preserve already configured drivers.
- Let installed-driver health checks follow the actual instance ID assigned during installation, rather than requiring a previously known ID.
- Resolve private configuration paths through the hardware-CI wrapper, and keep configuration values and raw server errors out of retained evidence. Uncertain configuration stops the workflow and retains the processor lease for investigation.

These changes have offline regression coverage and passed a complete Wiser initial-installation workflow: 39 local tests, 39 processor tests, three read-only live checks, private wizard configuration and three installed-driver health checks. Complete Tesla Owner and Fleet workflows also passed, including automatic region discovery. Existing KasaTapo, Overkiz, WeatherLink and Apple TV V1 validation remains applicable to the unchanged paths.

## Updating

Install **CrestronHomeNUnit.TestAdapter 1.2.1** in a separate .NET 10 workflow project, or extract the complete runner/CLI ZIP. The tooling continues to use **CrestronHomeDevTools 1.1.0** and official **NUnit 4.6.1**. The self-test processor package is **1.2.001.0000**, under **Utility** in Configure. Processor test packages are GitHub assets only; only the desktop adapter is published to NuGet.

See the [Test Explorer guide](docs/VisualStudioTestExplorer.md), [CI setup](docs/ContinuousIntegration.md) and [hardware-agent template](docs/GitHubHardwareCI.md). Private credentials, machine paths and test results are excluded from source and release archives.

This release does not automatically add workflow projects to consumer solutions or enable their hardware CI jobs. V1 initial-install/removal reboot hardware validation, generic installed-device control/restoration, rollback and cross-run artifact reuse remain separate work.