# Crestron Home NUnit v1.2.0

This stable minor release adds **CrestronHomeNUnit.TestAdapter** on NuGet. A separate .NET 10 workflow project lets Visual Studio Test Explorer or VSTest run the complete gated development cycle: local tests, processor package deployment and activation, remote tests, optional live tests, optional actual-driver update and health checks, and configured cleanup. Individual test outcomes are imported into the workflow result.

Start with the [Test Explorer guide](docs/VisualStudioTestExplorer.md) and [sample project](samples/WorkflowTests/WorkflowTests.csproj). Discovery is offline. Execution requires a private plan and credentials; missing configuration fails the test. Cancellation is cooperative, and uncertain remote execution retains its processor reservation for inspection.

## Shared processor coordination

The Windows runner, standalone CLI, updated test-host tiles, workflows and DevTools/build deployment now share the processor reservation protocol. This prevents cooperating tools from deploying or running another suite while the processor is reserved. CLI reservation commands also cover manual Configure sessions. Upgrade participating tools and rebuild/update processor test packages together; older hosts and unrelated clients cannot be forced to honor the reservation.

The CLI and adapter consume **CrestronHomeDevTools 1.1.0** from NuGet. No adjacent DevTools checkout is required. See [hardware CI setup](docs/GitHubHardwareCI.md) for a self-hosted Windows agent, private settings, concurrency and reusable workflow templates.

## Downloads and updating

- Install `CrestronHomeNUnit.TestAdapter` **1.2.0** and `Microsoft.NET.Test.Sdk` in a separate .NET 10 workflow project, with `PrivateAssets="all"` on both references. Existing driver and processor test projects can remain net472.
- Extract the complete Windows runner or CLI ZIP; both include their .NET 10 runtime. Existing runner preferences and protected credentials are retained.
- The NUnit Test Host processor package is **1.2.000.0000**, in Configure/Setup's **Utility** category. NUnit remains the official **4.6.1** package.
- Processor packages are GitHub assets only. Only the desktop test adapter is published to NuGet. Private settings, credentials, local paths and test results are excluded. The licensed application icon is embedded in the runner only; its source is not distributed.

## Validation and scope

Release validation covers runner/transport, client, workflow and adapter regressions, packaged NUnit self-tests and language compatibility, plus installation and offline discovery in a clean NuGet consumer. Missing private settings are verified to fail before processor access.

The adapter completed a real MC4-R KasaTapo workflow through VSTest: **148 individual tests passed**, the actual driver updated, the test instance was removed and the reservation released. Shared desktop/tile exclusion and guarded build deployment also passed hardware validation. Complete Overkiz and WeatherLink workflows subsequently passed their local, processor, live and installed-driver checks. These are separate validation runs; see the [CI evidence and remaining work](docs/ContinuousIntegration.md#validated-behavior-and-remaining-work).

V1 update/reboot handling remains explicitly opt-in and has passed the Apple TV hardware workflow. V1 initial-install/removal reboot paths have simulated coverage only. Generic installed-device control/restoration and automatic rollback are not implemented. Tesla and Wiser complete production-update workflows remain outstanding.