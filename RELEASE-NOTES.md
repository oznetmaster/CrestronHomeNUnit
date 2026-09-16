# Crestron Home NUnit v1.7.0

This is the **first release of Android UI testing support**. A development workflow can now run NUnit tests against the real Crestron Home Android app after its desktop tests, processor tests and gated driver update.

The new Android toolkit is included in the `CrestronHomeNUnit.TestAdapter` package for .NET 10 test projects. It provides Home and extension-page navigation, checks against visible controls, private screenshots and test results, and verified return to the starting Home screen. The processor and Android session are reserved together so cooperating workflows cannot overlap. Developers supply their own ADB installation, emulator and Crestron Home app.

This release also introduces an optional way to test an already-built Release driver package. The workflow verifies its package hash, driver identity, version and source commit, then uses those exact bytes without rebuilding them. Ordinary workflows continue to build Debug packages.

An existing Debug-version reconciliation problem is fixed: driver manifests named after the output package are now recognized alongside the project-name convention. Custom or ambiguous layouts can specify `manifestPath` explicitly.

Validation: the complete Wiser development workflow passed with the packaged toolkit, including desktop and processor tests, live reads, the actual-driver update, installed health checks and both Android UI cases. Gateway and room state was preserved, Home was restored and reservations were released. Automated workflow, Android, adapter and isolated-package checks also passed; see [validation details](Validation.md).

Android hardware validation currently covers read-only gateway checks with minimized BlueStacks in a logged-in Windows session. Physical UI controls, service-session operation, exact Release-candidate hardware validation and the final Crestron submission workflow remain work in progress.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.7.0** or the matching runner/CLI ZIP. Existing plans remain valid; Android and Release-candidate testing are opt-in. Official NUnit 4.6.1 and DevTools 1.4.0 remain unchanged. Processor test packages remain GitHub-only.

See [Android setup](docs/AndroidUiTesting.md), [Release candidate testing](docs/ReleaseCandidateTesting.md), and [version reconciliation](docs/ContinuousIntegration.md#readiness-and-version-identity). Keep credentials, worker settings and device evidence private.
