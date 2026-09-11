# Crestron Home NUnit v1.0.0

Initial public release of the Windows runner, NUnit self-test processor package, and reusable tools for creating processor test packages.

## Downloads

- **CrestronHomeNUnit.Runner-win-x64.zip**: extract the complete ZIP and launch `CrestronHomeNUnit.Runner.exe`. Includes the .NET 10.0.12 Windows Desktop runtime; no separate runtime installation is required.
- **CrestronHomeNUnit.Driver.pkg**: install through Crestron Home Configure/Setup. Appears as **NUnit Test Host** under **Utility**, version **1.0.000.0000**. Contains selected NUnit framework self-tests and 34 language/runtime compatibility tests.
- **CrestronHomeNUnit-Documentation.zip**: README, package/protocol guides, MIT license and dependency attributions. Additional runtime notices are included inside the runner ZIP.
- **SHA256SUMS.txt**: SHA-256 checksums for the three downloadable archives/packages.

## Features

- Discover installed test packages using mDNS, with dynamically assigned TCP ports and processor names.
- Authenticate with existing processor SFTP credentials; remember credentials using Windows protection.
- Select packages, suites, fixtures and individual tests; display live progress and save NUnit XML and diagnostics.
- Run ordinary suites from standalone Home tiles. Each processor test package includes its own host and NUnit dependency.
- Transfer private test inputs and explicitly opt into manual/live suites from the desktop runner.
- Restore package/test selections when requested. Save window position, size and maximized state immediately; ignore minimization and recover placement when a monitor is unavailable.
- Build and deploy custom net472 processor packages from Visual Studio using the shared SDK and generator.

## Compatibility and known limitations

The Windows runner targets .NET 10; processor packages target net472 for Crestron Home's Mono-based environment and Entity Model V2 SDK 27.0.24. The host uses official NUnit 4.6.1 NuGet binaries through the NUnit framework API, with the documented packaging adaptations. It does not depend on NUnitLite or a maintained framework fork.

The included framework tests are a selected subset of NUnit's own test suite. Upstream NUnit 4.6.1 has a known repeated-run stream-comparison issue; a second framework self-test run in the same process can fail even when the first succeeds. This is documented and has not been hidden by suppressing assertions. Platform-specific skips and timing warnings can occur. Cancellation is cooperative; a fatal host-process failure cannot preserve its TCP connection.

Release CI validates the runner regressions and one execution of each packaged suite on Windows. Earlier development versions were validated on a Crestron Home processor, including all 34 compatibility tests. The final versioned release package is built in CI and is not automatically deployed to any processor.

Private processor credentials, input settings, local paths and result captures are not included. Project-owned material is Copyright (c) 2026 Neil Colvin, MIT licensed. NUnit and other dependencies retain their own notices. This is an independent project, not an official or endorsed Crestron or NUnit product.