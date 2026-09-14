# Crestron Home NUnit

For the complete local-to-processor development cycle, see the [continuous integration guide](docs/ContinuousIntegration.md), including private settings, gated actual-driver deployment, evidence and optional test-instance removal.

Version 1.2.0 includes a [Visual Studio Test Explorer workflow adapter](docs/VisualStudioTestExplorer.md), available as the stable [CrestronHomeNUnit.TestAdapter NuGet package](https://www.nuget.org/packages/CrestronHomeNUnit.TestAdapter). Add it to a separate .NET 10 workflow test project to run the same gated development cycle from Visual Studio.

Version 1.2.0 coordinates desktop tests, updated Home test tiles and DevTools/build operations through a shared processor reservation. Upgrade the participating tools and test packages together, using DevTools 1.1.0 or later; see [hardware CI setup and coordination](docs/GitHubHardwareCI.md). [Build deployment settings and retained package inspection](https://github.com/oznetmaster/CrestronHomeDevTools/blob/HEAD/docs/ProcessorCoordination.md) are documented in DevTools.

To run hardware checks from GitHub Actions on your own Windows computer and processor, follow [GitHub hardware CI setup](docs/GitHubHardwareCI.md). It includes private configuration, a relocatable plan wrapper and an example workflow for a private orchestration repository.

Run NUnit tests **on a Crestron Home processor**, using a Windows runner, automation CLI or a standalone test tile in Crestron Home. This checks your code in the processor's Mono-based environment, where SDK, filesystem and networking behavior can differ from Windows.

[Download the latest release](https://github.com/oznetmaster/CrestronHomeNUnit/releases/latest) · [Changelog](CHANGELOG.md) · [Full user and developer guide](docs/UserGuide.md) · [Create your own test package](docs/ProcessorTestPackages.md)

Copyright (c) 2026 Neil Colvin. Project-owned code is [MIT licensed](LICENSE). NUnit and other dependencies retain their own licenses and notices.

## Contents

- [What you install](#what-you-install)
- [Quick start](#quick-start)
- [Using the Windows runner](#using-the-windows-runner)
- [Standalone Home tiles](#standalone-home-tiles)
- [Your own processor tests](#your-own-processor-tests)
- [Build and release](#build-and-release)
- [Documentation and known limitations](#documentation-and-known-limitations)
- [Licenses and acknowledgments](#licenses-and-acknowledgments)

## What you install

| Component | Purpose |
| --- | --- |
| **Windows runner** | Finds packages, authenticates, selects and runs tests, transfers inputs and displays results. The Windows x64 ZIP includes .NET 10; no separate runtime installation is needed. |
| **Automation CLI** | Self-contained Windows x64 console for unattended test runs and the complete gated development workflow. Uses CrestronHomeDevTools 1.1.0 from NuGet. |
| **Test Explorer adapter** | NuGet package for a separate .NET 10 workflow project. Runs the complete workflow and reports individual test outcomes in Visual Studio or VSTest. |
| **NUnit Test Host** | An optional processor package containing selected NUnit framework self-tests and 34 language/runtime compatibility tests. Appears under **Utility** in Configure/Setup. |
| **Your processor test package** | Contains your NUnit tests, their dependencies, a test host and its own Home tile. |

**Each processor test package is self-contained.** You do not need to install NUnit Test Host before installing another test package. Multiple packages can coexist on a processor, each advertising an automatically assigned TCP port.

For automation, download **CrestronHomeNUnit.Cli-win-x64.zip**, extract it completely and run `CrestronHomeNUnit.Cli.exe --help`. See the [CLI guide](docs/CommandLineRunner.md) and [CI workflow guide](docs/ContinuousIntegration.md).

## Quick start

1. Download **CrestronHomeNUnit.Runner-win-x64.zip** and, to try the supplied suites, **CrestronHomeNUnit.Driver.pkg** from [Releases](https://github.com/oznetmaster/CrestronHomeNUnit/releases/latest). The automatic source-code archives are not installable packages.
2. Upload the `.pkg` to `/user/ThirdPartyDrivers/Import` using your processor's SFTP credentials. Import and add **NUnit Test Host** through Crestron Home Configure/Setup, under **Utility**.
3. Extract the complete Windows ZIP and launch `CrestronHomeNUnit.Runner.exe`.
4. Select **Find packages**, choose the installed package, and connect with that processor's existing SFTP username and password. Known credentials allow automatic connection when selecting a package.
5. Choose a suite. Start with **C# 13 Compatibility**, then try the framework self-tests. Use **Run all**, or **Discover**, select a fixture/test and choose **Run selection**.

You need a Windows x64 computer supported by .NET 10, a compatible Crestron Home Entity V2 processor, and network access between them. Processor packages target **net472** and use Driver SDK **27.0.24**. Neither Visual Studio nor the SDK is needed to run the downloaded Windows application.

## Using the Windows runner

- **Discover** populates the test tree. **Run all** does not require discovery first; **Run selection** requires a selected fixture or test.
- Select a different package while idle to switch connections. The runner refreshes a discovered package's current address and port before connecting. **Find packages** also updates changed endpoints.
- After a dropped connection, the runner makes up to three rediscovery/reconnect attempts. Previous results remain visible, and interrupted tests are never rerun automatically. If the package is still restarting, use **Connect** once its Home tile is ready.
- **Test inputs…** selects configuration files shared by all suites in the same processor package. Switching suites keeps the selection; other packages and processors have separate inputs. Existing suite selections migrate when they agree; if they conflict, choose the intended files once for the package. The runner transfers their current contents when discovering or running tests. Private device settings are not part of a published package.
- Live/manual suites run only when explicitly selected in the runner. Their fixtures may operate physical equipment; review their requirements and choose the intended devices.
- **Use at next restart** remembers the package, suite and test selection. Restart restoration rediscovers the package's current endpoint and never starts tests automatically.
- Window position, size and maximized state save immediately, independently of that checkbox. Minimization is ignored; restoration handles missing monitors and smaller screens.
- **Live output**, detailed results and **Open results folder** provide diagnostics. Completed results are retained when a connection is interrupted. Cancellation is cooperative.

Settings live under `%LOCALAPPDATA%/CrestronHomeNUnit`; saved credentials use Windows DPAPI. Deployment settings and any legacy files in a checkout belong in **`.git/info/exclude`**, rather than a published `.gitignore`. See the guide for [privacy and local configuration](docs/UserGuide.md#privacy-and-local-configuration).

## Standalone Home tiles

Ordinary suites can run from the installed package's Home tile without the Windows runner. The supplied host exposes its framework and compatibility suites. Generated packages normally expose unit tests, discovery and host/connection status; they can customize their tile for additional ordinary suites.

The tile is not an automatically generated picker for every fixture. Detailed selection, private input transfer and explicit live/manual execution use the Windows runner. Each test package owns its own UI; a driver-under-test's UI resources remain separate from the test-host tile.

## Your own processor tests

Keep tests in their ordinary NUnit test project so Visual Studio's NUnit adapter and the processor can use the same test sources. Add a **net472-only** processor package project using `New-ProcessorTestProject.ps1` and the shared package SDK.

For Crestron-specific drivers, that project can stay in the driver's solution. For independent libraries, keep Crestron packaging in a separate repository, preserving the library repository's platform independence. Test `.pkg` files are release assets; this project does not publish test packages to NuGet.

See [the package creation guide](docs/ProcessorTestPackages.md) and [repository layout and ownership](docs/UserGuide.md#repository-layout-and-package-ownership).

## Build and release

**Updating to v1.0.1:** close the runner and extract the complete new Windows ZIP. Saved preferences and credentials remain in the user profile. The reconnection fix works with existing processor packages. Package authors should update their shared SDK checkout or pin, rebuild and redeploy to receive the fixes for dependency resource helpers and anonymous JSON payload types.

Open **CrestronHomeNUnit.sln** in Visual Studio with .NET 10 SDK support and .NET Framework 4.7.2 targeting tools. Install the Crestron Driver SDK and ILRepack **2.0.45** as described in the [build guide](docs/UserGuide.md#build-and-deploy-with-visual-studio).

Build **CrestronHomeNUnit.Runner** for Windows, or **CrestronHomeNUnit.Driver** for the supplied processor package. The runner targets `net10.0-windows`; processor packages remain `net472`. Configure private deployment settings locally. Automatic deployment is limited to configured Debug builds inside Visual Studio.

`PublishRunner.ps1` creates a self-contained Windows x64 ZIP. Licensed local builds can set `RunnerIconPath` in the excluded `CrestronHomeNUnit.Runner.Local.targets`. The public source has an MIT icon fallback; official releases use the privately supplied GlyphLab icon.

For a GitHub release, update `CHANGELOG.md` and `RELEASE-NOTES.md`, then run the **Release** workflow on `main` with a new three-part version. CI sets the version, builds and validates the packages, pushes the release commit and annotated tag, then publishes the runner, CLI, processor package, adapter, documentation and checksums. The adapter alone is also published to NuGet using the `release` environment and package-scoped Trusted Publishing policy; repository variable `NUGET_USER` identifies its owner. Release version changes occur in CI; local Release builds preserve the manifest version. Release CI does not deploy to processors.

## Documentation and known limitations

- [Complete user and developer guide](docs/UserGuide.md): discovery, authentication, inputs, package structure, compatibility, results, troubleshooting and release details.
- [Processor package guide](docs/ProcessorTestPackages.md) and [TCP protocol](docs/TcpProtocol.md).
- [Validation history](Validation.md), [changelog](CHANGELOG.md) and [release notes](RELEASE-NOTES.md).

The host uses official **NUnit 4.6.1 NuGet binaries**, with documented packaging adaptations. It uses the NUnit framework API; **NUnitLite and a maintained framework fork are not dependencies**.

The included NUnit tests are a selected subset. NUnit 4.6.1 has a known stream-comparison defect that can produce failures on repeated framework self-test runs; [the upstream fix is tracked here](https://github.com/nunit/nunit/pull/5416). Platform-specific skips and timing warnings can occur. Fatal host-process failures cannot preserve a TCP connection. See the full guide's [validation and limitations](docs/UserGuide.md#validation-and-known-limitations).

## Licenses and acknowledgments

Project-owned material: **Copyright (c) 2026 Neil Colvin**, [MIT License](LICENSE).

The NUnit framework and imported self-tests are **Copyright (c) Charlie Poole, Rob Prouse and Contributors**, under NUnit's [MIT license](vendor/nunit/LICENSE.txt). Imported source retains its upstream notices; adaptations are recorded in [the provenance document](vendor/nunit/PROVENANCE.md). The licensed GlyphLab application icon and all other dependencies are covered by [Third-party notices](THIRD-PARTY-NOTICES.md) and the full texts under `licenses`.

**Crestron notice:** Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc. It is an independent, unofficial development and testing tool. Use of the NUnit name identifies the test framework and included upstream tests; this is not an official NUnit distribution or an NUnit-endorsed Crestron product.

The Crestron SDK is obtained separately under Crestron's terms. Its proprietary components are platform/build dependencies and are not relicensed under MIT.
## Command-line automation (development)

A standalone .NET 10 CLI shares the Windows runner's TCP and authentication code. It supports package discovery, suite selection, private input transfer, NUnit XML results and CI exit codes. See [Command-line processor tests](docs/CommandLineRunner.md).


The CLI also runs the [gated development workflow](docs/ProcessorTestWorkflow.md): local tests, processor package installation, processor/live tests, actual-driver update and checks, then test-instance cleanup. Complete KasaTapo, Overkiz, WeatherLink and Apple TV V1 runs have been validated on hardware, including the explicitly authorized V1 reboot. The [Visual Studio adapter](docs/VisualStudioTestExplorer.md) uses the same backend.
