# NUnit 5 Snapshot Tests processor package

See the [NUnit 5 snapshot guide](../README.md) for the pinned version, source provenance, build commands, test scope, comparison procedure and reporting limitations.

Build this project from the **NUnit 5 Snapshot** folder in `CrestronHomeNUnit.sln`. Its net472 package is `bin/Debug/net472/CrestronHomeNUnit.NUnit5.ProcessorTests.pkg`. Private deployment settings use the existing `.csproj.user` and `.Local.targets` convention; exclude them locally from Git.

Install **NUnit 5 Snapshot Tests** under **Utility** in Configure. The Windows runner discovers **NUnit 5 Snapshot Tests (beta.1.52)** automatically; its standalone Home tile also provides **Framework Self-Tests** and **C# Compatibility** buttons and displays the dynamically assigned TCP port.

This is a diagnostic package. Failed tests and invalid discovery nodes are findings to retain, not reasons to alter upstream code or mark a run successful. It has no live household-device suite or required input files. Stable NUnit packages remain independent.
