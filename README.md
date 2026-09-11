# Crestron Home NUnit

Run NUnit tests **on a Crestron Home processor**, using either a Windows desktop runner or a standalone test tile in Crestron Home.

Crestron Home NUnit provides a Windows runner, an installable processor package containing selected NUnit framework self-tests and language compatibility tests, and reusable tools for packaging your own NUnit test assemblies. Tests execute inside the processor's Mono-based environment, where runtime, SDK, filesystem and networking behavior can differ from Windows.

Copyright (c) 2026 Neil Colvin. Project-owned code is licensed under the [MIT License](LICENSE). NUnit and other third-party components retain their own copyright notices and licenses; see [Third-party notices](THIRD-PARTY-NOTICES.md).

**Crestron notice:** Crestron and Crestron Home are trademarks or registered trademarks of Crestron Electronics, Inc. This project is not affiliated with, endorsed by, or sponsored by Crestron Electronics, Inc. It is an independent, unofficial development and testing tool. Use of the NUnit name identifies the test framework and included upstream tests; this is not an official NUnit distribution or an NUnit-endorsed Crestron product.

## Contents

- [What you install](#what-you-install)
- [Quick start](#quick-start)
- [Standalone Crestron Home tiles](#standalone-crestron-home-tiles)
- [Windows runner](#windows-runner)
- [Discovery, processors and connections](#discovery-processors-and-connections)
- [Test inputs and live tests](#test-inputs-and-live-tests)
- [Results, cancellation and interrupted runs](#results-cancellation-and-interrupted-runs)
- [Included NUnit and compatibility suites](#included-nunit-and-compatibility-suites)
- [Build and deploy with Visual Studio](#build-and-deploy-with-visual-studio)
- [Create your own processor test package](#create-your-own-processor-test-package)
- [Repository layout and package ownership](#repository-layout-and-package-ownership)
- [Runtime and packaging compatibility](#runtime-and-packaging-compatibility)
- [Validation and known limitations](#validation-and-known-limitations)
- [Troubleshooting](#troubleshooting)
- [Privacy and local configuration](#privacy-and-local-configuration)
- [Releases and versioning](#releases-and-versioning)
- [Licenses and attribution](#licenses-and-attribution)

## What you install

| Component | Runs on | Purpose |
| --- | --- | --- |
| Windows runner | Windows computer | Finds installed test packages, connects to a processor, selects suites and individual tests, transfers test inputs, displays progress and saves results. |
| NUnit self-test package | Crestron Home processor | An Entity V2 extension containing the test host, selected NUnit self-tests, language compatibility tests and its own Home tile. Its display name is **NUnit Test Host**. |
| Your processor test package | Crestron Home processor | A separate Entity V2 extension containing the shared host, your tests, their application dependencies and its own Home tile. |
| Package SDK and generator | Developer's Windows computer | Builds additional processor packages from existing NUnit test projects. |

**Every processor test package is self-contained.** You do not install the NUnit self-test package as a prerequisite for running another test package. Each package includes its own NUnit framework dependency and host. You also do not install NUnit or NUnitLite separately on the processor.

Several test packages can be installed on one processor. One Windows runner can select among packages on several processors, maintaining one active package connection at a time. Each package gets an available TCP port automatically; there is no reserved port range to assign manually.

The official Windows runner uses the licensed GlyphLab **Code – Play** application icon. The public source includes an MIT-licensed fallback icon so it can be built without the stock-icon license. Licensed local builds can set the `RunnerIconPath` MSBuild property in `CrestronHomeNUnit.Runner.Local.targets`, excluded through `.git/info/exclude`; see [Third-party notices](THIRD-PARTY-NOTICES.md).

The Windows runner is optional for ordinary suite runs from a Home tile. It is required for individual test selection, detailed interactive results, transferring configuration files, and explicitly starting suites marked as manual/live.

## Quick start

### Requirements for running released packages

- A Windows x64 computer supported by .NET 10. The release ZIP includes the .NET 10 Windows Desktop runtime; no separate runtime installation is needed.
- A Crestron Home processor supporting the Entity Model V2 driver SDK used by the package. Current development targets `net472` with Crestron DeviceDrivers SDK **27.0.24**.
- Access to Crestron Home Configure/Setup to import and add the test driver.
- Network access from Windows to the processor and its existing SFTP username/password for desktop connections.

Source builds additionally require the tools listed under [Build and deploy](#build-and-deploy-with-visual-studio). Neither Visual Studio nor the Crestron SDK is needed merely to launch the released Windows runner.

### Install the self-test package

1. Obtain `CrestronHomeNUnit.Driver.pkg` and `CrestronHomeNUnit.Runner-win-x64.zip` from the [GitHub Release assets](https://github.com/oznetmaster/CrestronHomeNUnit/releases/latest). GitHub's automatic **Source code** archives are not installable driver packages.
2. Upload the `.pkg` to the processor's `/user/ThirdPartyDrivers/Import` directory using SFTP, or use your normal Crestron Home driver import workflow.
3. Allow Home to import the package. In Configure/Setup, locate **Utility → Neil Colvin → NUnit Test Host** and add it to a room.
4. Open its Home tile. It should report its status and current TCP port. Importing a package alone does not start a test host; a configured instance must be active.
5. Extract the entire runner ZIP into a writable folder and launch `CrestronHomeNUnit.Runner.exe`. Keep the accompanying DLLs and configuration file beside the executable.
6. Click **Find packages**, select **NUnit Test Host** on the desired processor, and enter that processor's SFTP credentials if needed. Select **Connect** when credentials have been entered manually. Selecting a package with known credentials connects automatically.
7. Choose **C# compatibility** and select **Run all** for a short initial check. Then choose **NUnit framework self-tests** and run that suite.

**Discover is optional before Run all.** Use Discover when you want to browse fixtures or select an individual test. Changing the suite clears the previous test tree, so discover the newly selected suite before using Run selection.

## Standalone Crestron Home tiles

Every installed test package has its own tile and controls. A test package can run its ordinary suite without a connected Windows computer. Runs take place in the background, and the tile reports the operation and its result.

The supplied **NUnit Test Host** tile exposes:

| Control | Action |
| --- | --- |
| Test Host | Shows the current host status and last operation summary. |
| NUnit Framework Self-Tests / Run Tests | Runs the included upstream framework test selection. |
| Test Discovery / Discover Tests | Discovers the framework suite without executing its test methods. |
| C# 13 Compatibility / Run Tests | Runs the included language and asynchronous lifecycle compatibility suite. |
| Desktop Runner | Shows the active TCP port and discovery status for desktop connections. |

The historical **C# 13 Compatibility** tile label describes the suite's origin; the source now uses `LangVersion=latest` with additional compatibility probes.

Generated packages normally expose a **Unit Tests** run action, a discovery action, host status, and desktop connection information. A package can customize its tile to expose another ordinary suite, such as driver lifecycle tests. The Windows runner obtains the complete suite list from `ProcessorTests.json`; the generic tile is not an automatically generated picker for an unlimited number of suites.

Suites marked `ManualOnly=true` cannot be run from a Home command. The generator displays their status and directs the user to the Windows runner, rather than providing a live-run button. These suites can require device credentials and can change physical equipment.

Home and desktop commands share the same execution guard within a package. Starting a second operation while that package is busy is rejected. Results started by either interface update the tile, but a desktop runner does not automatically download the history of earlier standalone tile runs.

### A tested driver's UI and the test host's UI

A driver test package has its own identity and owns its top-level UI and translations. UI files needed by the driver under test must be included under a separate test-data directory, such as `DriverTestData`, and passed to the tested entities using that directory. They must not overwrite the test host's top-level UI.

The packaging tools also remove other driver manifests from the merged test-host assembly and verify that the generated package metadata belongs to the test host. This prevents a referenced production driver from supplying the test package's name, identity or configuration metadata. The production driver and its test package can therefore be installed as distinct drivers.

## Windows runner

The runner remembers its window position, size and maximized state independently of **Use at next restart**. Changes are saved immediately, so normal application shutdown is not required. Minimization leaves the previous placement intact. If a monitor is removed or its available area becomes smaller, the restored window is fitted to an available screen. Settings remain local and are excluded from release archives.

The two dropdowns select different things:

- **Top dropdown:** a processor test package, identified by processor name/address, package name and TCP port.
- **Lower dropdown:** a suite provided by the currently connected package, such as Unit Tests, Live Tests or Processor lifecycle.

| Control | Behavior |
| --- | --- |
| Find packages | Refreshes the local discovery list. It remains available while connected and idle. |
| Package dropdown | Switches to another package while idle, closing the previous connection and connecting with the selected processor's known credentials. |
| Processor / Port | Manual connection fields, editable while disconnected. Read the port from the package's Home tile. |
| SFTP user / Password | Existing processor credentials used to authenticate the desktop connection. |
| Connect / Disconnect | Opens or closes the selected connection. |
| Suite dropdown | Selects one advertised suite. Changing it clears the previous test selection and result rows. |
| Discover | Loads the selected suite's fixtures and cases into the tree. |
| Run all | Runs the currently selected suite, not every package or every suite on the processor. |
| Run selection | Runs the selected test or fixture within that suite. Enabled only when a tree node is selected. |
| Cancel | Requests cooperative cancellation of the active operation. |
| Test inputs… | Selects local configuration files to send with the selected suite's next discovery or run. |
| Clear inputs | Schedules removal of that suite's processor inputs on the next discovery or run. |
| Test results / Live output | Shows individual outcomes, durations, failure details and streamed output. |
| Open results folder | Opens the latest local result capture. |
| Use at next restart | Saves the package, suite and selected fixture/test for the next runner launch. |

Package switching and package discovery are disabled during a test operation. Refreshing the package list while idle preserves an existing connection, even if that package temporarily fails to respond to multicast discovery.

### Restore your selections on restart

Enable **Use at next restart** to retain the current processor/package, suite and selected fixture or test. On the next launch, the runner rediscovers the saved package's current IP address and port before connecting. Restoring a selected test may perform NUnit discovery, but **never starts a test run**.

If the saved package is unavailable, the runner reports that condition rather than guessing another package. A manually entered endpoint without a discovered package identity uses its saved address/port. Unchecking the box removes the saved selections. Credentials are held separately in Windows-protected storage.

## Discovery, processors and connections

Packages advertise `_crestron-nunit._tcp.local` through standard mDNS on UDP **5353**. The advertised TCP port is assigned by the processor when the package starts. It can change after a restart or driver update.

The runner also queries Crestron's native discovery service on UDP **41794** to obtain configured processor names without signing in. The package list is sorted by processor and package. It lists active test packages, not an inventory of every Crestron processor on the network.

Multicast discovery normally remains within the local network segment. Across routed networks or VLANs, use an appropriately configured mDNS reflector or enter the processor's address and the tile's current port manually. Package authors can configure a fixed port when needed; `Port=0` is the default for automatic assignment.

### Processor credentials

Each processor can have different credentials. The runner remembers them per processor using Windows DPAPI; credentials belonging to another processor are not deliberately carried across an address change.

On connection, the runner authenticates over SFTP on port **22** and retrieves an internal processor-wide test token. All test packages on that processor share the identity file at `/user/Data/CrestronHomeNUnit/ProcessorIdentity.txt`. There is **no separate pairing key for the user to create, enter or manage**.

The runner trusts an SSH host fingerprint on first successful use and pins it for later connections. A changed fingerprint is reported. The deployment helper is a separate SFTP client and currently uses the existing Posh-SSH `-Force` workflow, which does not verify the host key.

The test protocol authenticates the connection and encrypts transferred configuration files. Ordinary test names, results and streamed output are not encrypted. Use it on a trusted development network. This is not a TLS tunnel for arbitrary test traffic. Changing the SFTP password alone does not revoke a token already retrieved by a runner; see [TCP protocol documentation](docs/TcpProtocol.md) for the identity and authentication details.

## Test inputs and live tests

A public suite can include integration tests without embedding anyone's network configuration or credentials. Keep reusable fixtures and sanitized configuration examples in source; supply actual device selections and credentials locally at run time.

1. Connect to the package and select its live/manual suite.
2. Choose **Test inputs…** and select the files expected by the suite, such as `LiveTestSettings.json`.
3. Discover the suite if you want individual selection, then run the desired tests.

The runner reads the files again before each discovery or run. Editing the local JSON therefore does not require rebuilding or redeploying the package. A fixture must reload its configuration for each operation; a static configuration object retained from the first discovery can hide later changes.

Inputs are selected per processor and suite. Suite IDs should be unique across packages on the same processor so that unrelated packages do not share the runner's input-selection profile.

The transport accepts up to 16 files, at most 1 MiB each and 4 MiB combined. Only plain filenames are allowed. Inputs are stored under the suite's private data directory on the processor, outside the `.pkg`, until replaced or cleared. An absent selection leaves previously stored inputs in place. **Clear inputs** takes effect on the processor when the next discovery or run sends the empty selection.

### Contract for fixture authors

Tests locate uploaded data through the standard NUnit parameter:

```csharp
string directory = TestContext.Parameters.Get("TestDataDirectory", AppContext.BaseDirectory);
string settingsPath = Path.Combine(directory, "LiveTestSettings.json");
```

For legacy configuration with an enable flag, apply the operation-specific override after loading it:

```csharp
settings.Enabled = TestContext.Parameters.Get("EnableLiveTests", settings.Enabled);
```

The runner supplies `EnableLiveTests=true` only for the selected manual suite. The host supplies false for ordinary operations. The override is not written into the JSON or retained as permission for later operations. Normal Visual Studio tests can continue to use the local JSON default when no processor parameter is supplied.

`ManualOnly` is host metadata; an NUnit `[Category("Live")]` attribute alone does not enforce the host restriction. The generator creates both the category filters and manual-suite metadata when `-IncludeLiveTests` is specified.

### Writing useful live tests

- Prefer stable device IDs or unique names, resolved to current addresses through the library under test, over hard-coded IP addresses.
- Capture the device's initial state and restore it in `finally`, including after an assertion failure. Record restoration failures clearly. A process crash or power loss can still prevent restoration.
- Use bounded network waits, cancellation and meaningful failure diagnostics.
- Keep read-only sensor tests separate from tests that operate lights, plugs or other equipment.
- Cache expensive discovery for one run where appropriate, then clear the cache between runs. Avoid a process-lifetime cache that retains stale endpoints.
- Do not write credentials into assertion messages, test names or logs. Encrypted input transfer does not make test-generated output private.

## Results, cancellation and interrupted runs

The runner saves each operation beneath the current Windows user's `Documents/CrestronHomeNUnit/Results` directory. Captures include `TestTree.xml` for discovery, `TestResult.xml` for completed execution, `LiveOutput.txt` and `RunStatus.txt` as applicable. Completed execution uses NUnit-style XML, including counts, durations and per-test results. The live output display is bounded; the saved live-output file reflects that view, while per-test captured output remains in completed NUnit result XML.

Processor results are written beneath the driver's data directory in `TestResults/<suite-id>`. Inputs use its `Inputs` subdirectory. Each operation replaces the applicable previous processor result file; the Windows runner keeps separate timestamped capture directories.

Cancellation is cooperative. It asks NUnit to stop, allowing the active test to finish. It cannot reliably terminate a deadlocked test or forcibly interrupt arbitrary SDK/native calls. Disconnecting requests cancellation of that connection's active operation.

If the TCP connection closes or a host operation fails, the runner preserves received results and marks unfinished active tests **Incomplete**. It does not manufacture a successful NUnit result file for an interrupted run. Diagnostic captures identify the last active tests and received output.

All tests execute in the test host's process. Separate driver packages do not establish guaranteed operating-system process isolation. A fatal runtime error or processor restart can stop the host and its TCP connection. Reconnecting an idle runner is supported; automatically resuming an interrupted test run is not.

## Included NUnit and compatibility suites

The host uses the official **NUnit 4.6.1 NuGet package**, consuming its .NET Framework asset from the `net472` projects. It embeds NUnit through `NUnitTestAssemblyRunner`; **NUnitLite is not a runtime dependency**, and the framework is not built from a private fork.

### NUnit framework self-tests

The imported source is pinned to NUnit tag **v4.6.1**, commit `b9197a6f17635580a3a397f3eb0f28bddba2e0c7`. The selected top-level tests come from the upstream **Assertions**, **Constraints** and **Syntax** areas, with their supporting utilities and test-data fixtures.

This is a selected compatibility baseline, not the complete NUnit repository. Desktop partial-trust and Roslyn compiler-negative fixtures are excluded from compilation. Upstream ignores, platform skips and explicit demonstration fixtures remain identifiable; ordinary suite runs do not opt into explicit tests. Platform-dependent counts and timing warnings can differ between Windows and the processor.

The source retains NUnit's copyright and MIT license. Local changes recreate certain mutable/disposable test state between runs and use the driver's work directory for temporary test files. Exact source scope, exclusions and adaptations are documented in [NUnit provenance](vendor/nunit/PROVENANCE.md).

### Language compatibility

The 34-case compatibility suite exercises the accompanying library and asynchronous fixture lifecycle behavior under the same merge and runtime conditions as a processor package. It includes modern language constructs and required compatibility shims.

Processor projects target **.NET Framework 4.7.2** with **`LangVersion=latest`**. The Windows runner and its UI/transport validation target **`net10.0-windows`**. Shared transport and host libraries target **`net472;net10.0`**, while validation that loads merged processor assemblies stays on **`net472`**. The selected .NET 10 SDK compiles newer language syntax, but language version and runtime capability are separate concerns: a construct that needs unavailable runtime support is not made compatible merely by selecting `latest`. Actual processor execution remains the final compatibility check.

## Build and deploy with Visual Studio

### Build tools

| Tool | Current configuration |
| --- | --- |
| Visual Studio/MSBuild | A version supporting the .NET 10 SDK, with .NET desktop development and .NET Framework 4.7.2 targeting support |
| .NET SDK | `global.json` starts at 10.0.401 and allows later feature bands |
| PowerShell | PowerShell 7 (`pwsh`) |
| Crestron Driver SDK | ManifestUtil and the SDK libraries installed locally |
| NUnit | 4.6.1, restored from NuGet |
| Crestron DeviceDrivers DevKit | 27.0.24, restored from NuGet |
| ILRepack | `dotnet-ilrepack` 2.0.45 |
| Posh-SSH | Required only for the supplied automatic SFTP deployment script |

Install ILRepack if it is not already available:

```powershell
dotnet tool install --global dotnet-ilrepack --version 2.0.45
```

The Crestron SDK is obtained separately under Crestron's terms. The project does not redistribute an SDK installer. Consult the [official Entity Model V2 SDK documentation](https://sdkcon78221.crestron.com/sdk/Crestron_Certified_Drivers_SDK/Content/Topics/Driver-SDK-V2/Driver-SDK-V2.htm).

### Build the supplied projects

1. Open `CrestronHomeNUnit.sln` and restore packages.
2. Build **CrestronHomeNUnit.Driver** in Debug to create the self-test `.pkg`.
3. Build **CrestronHomeNUnit.Runner** and select it as the startup project to launch the Windows UI with F5.

The driver is a library loaded by Home; F5 does not launch it as a Windows application. Its package is written to `CrestronHomeNUnit.Driver/bin/Debug/net472/CrestronHomeNUnit.Driver.pkg`. Release uses the corresponding Release directory.

### Configure automatic Debug deployment

Before creating private settings, add these patterns to this checkout's **`.git/info/exclude`**:

```text
**/*.csproj.user
**/*.Local.targets
**/Runner.local.json
**/Runner.inputs.local.json
**/Runner.window.local.json
**/LiveTestSettings.json
```

Copy `CrestronHomeNUnit.Driver.csproj.user.example` to `CrestronHomeNUnit.Driver.csproj.user` beside the driver project. Set `DeployAfterBuild=true` and enter the processor address, SFTP username and password. Use XML escaping for special characters. Keep this file private; it contains plaintext deployment credentials.

A **Debug build inside Visual Studio** then packages and uploads the driver to `/user/ThirdPartyDrivers/Import`. A local Release build or command-line validation build does not automatically deploy. Compilation, merge, package validation and packaging failures stop deployment. Home still needs to import the update and activate the intended driver instance.

Machine-specific SDK and tool paths can be overridden in `CrestronHomeNUnit.Driver.Local.targets`, using the supplied example. Installation defaults in the build scripts are examples of tool locations, not credentials. Do not put personal checkout paths or deployment endpoints into tracked project files.

`PrepareDesktopRunner.ps1` can import this local deployment configuration into the runner's Windows-protected credential store. It is optional: credentials can instead be entered in the runner. Generated package projects provide a `PrepareRunner.ps1` wrapper for the same workflow.

## Create your own processor test package

Keep the fixtures in the ordinary NUnit test project so Visual Studio's NUnit adapter and the processor execute the same test sources. The processor package project targets **only `net472`**, even when the ordinary test project also targets a current .NET runtime.

Use a version of the NUnit framework compatible with the host's pinned version. Mark desktop-only test adapters, test SDKs, analyzers and collectors `PrivateAssets="all"` so they do not become processor runtime dependencies. Application libraries and NUnit remain normal references.

From the testing-tool repository, create a package in the owning driver or processor-package collection:

```powershell
pwsh -NoProfile -File .\New-ProcessorTestProject.ps1 `
  -TestProject ..\ExampleDriver\ExampleDriver.Tests\ExampleDriver.Tests.csproj `
  -OutputDirectory ..\ExampleDriver\ExampleDriver.ProcessorTests `
  -Solution ..\ExampleDriver\ExampleDriver.slnx `
  -SuiteId example-driver `
  -TestNamespace ExampleDriver.Tests `
  -DisplayName "ExampleDriver Tests" `
  -ExpectedUnitTestCount 42 `
  -IncludeLiveTests
```

Replace the example paths and case count with your project. Omit `-IncludeLiveTests` if the package has no Live category. The script refuses to overwrite an existing directory, creates a distinct driver identity, and adds the project to the specified existing solution. It does not require a separate solution for each suite.

The generated project imports the shared host and build targets through `ProcessorTestSdkRoot`. Use adjacent checkouts or override that property in a locally excluded `.Local.targets` file. If checkouts are on different drives, ensure the generated SDK/test references do not commit an absolute personal path; arrange a suitable local source mapping instead.

Build the new processor project in Visual Studio. For an explicit command-line package build:

```powershell
dotnet build ..\ExampleDriver\ExampleDriver.ProcessorTests\ExampleDriver.ProcessorTests.csproj `
  -c Debug -p:BuildProcessorTestPackages=true -p:DeployAfterBuild=false
```

Without `BuildProcessorTestPackages=true`, generated projects skip packaging on the command line, allowing ordinary desktop builds without an installed Crestron SDK. Configure that project's own private `.csproj.user` to enable Visual Studio Debug deployment.

### Define suites

`ProcessorTests.json` supplies the package name, port and suite catalog:

```json
{
  "Name": "ExampleDriver Tests",
  "Port": 0,
  "Suites": [
    {
      "Id": "example-driver",
      "Name": "ExampleDriver Tests — Unit Tests",
      "FilterXml": "<filter><not><cat>Live</cat></not></filter>",
      "ManualOnly": false,
      "ExpectedCount": 42
    },
    {
      "Id": "example-driver-live",
      "Name": "ExampleDriver Tests — Live Tests",
      "FilterXml": "<filter><cat>Live</cat></filter>",
      "ManualOnly": true,
      "ExpectedCount": 0
    }
  ]
}
```

This shortened example illustrates the fields. The generator also restricts each filter to the requested test namespace, which is important when merged dependencies contain other fixtures. A zero expected count disables the exact-count comparison; it does not make an empty suite a useful test package.

Give each suite a stable, unique ID. Add a Processor category/suite for tests that require actual Crestron SDK behavior and exclude those tests from the normal desktop run settings. A processor-only test can still use simulated device responses; it does not have to operate real equipment.

For driver test packages, build the referenced production driver without its own merge/package/deploy side effects. The test package performs the final merge. For example, the driver's test-project reference can set `SkipMergeDependencies=true;DeployAfterBuild=false` when the driver supports those properties. This avoids deploying the production driver just because its tests were built.

For a worked description of the shared settings and packaging contract, see [Processor test packages](docs/ProcessorTestPackages.md).

## Repository layout and package ownership

| Project or directory | Responsibility |
| --- | --- |
| `CrestronHomeNUnit.Runner` | Windows UI, processor authentication, credentials and saved selections |
| `CrestronHomeNUnit.Driver` | Supplied self-test driver, tile, manifest and shared build/deploy scripts |
| `CrestronHomeNUnit.Runtime` | NUnit loading, filtering, execution, progress and suite operation coordination |
| `CrestronHomeNUnit.Transport` | TCP protocol, discovery, processor identity and protected input transfer |
| `CrestronHomeNUnit.SelfTests` | Project compiling the selected upstream NUnit tests |
| `vendor/nunit` | Pinned NUnit sources, upstream license, test signing key and provenance |
| `CrestronHomeNUnit.CompatibilityLibrary` / `.Tests` | Language compatibility probes and their tests |
| `ProcessorTestPackage` | Reusable MSBuild props and targets for additional packages |
| `ProcessorTestPackage.Validation` | Discovery/execution validation of merged package assemblies |
| `CrestronHomeNUnit.DesktopValidation` | Local self-test and compatibility validation |
| `CrestronHomeNUnit.TransportValidation` | Local runner, authentication, protocol and recovery checks |
| `tools` | Focused upstream issue reproductions and supporting notes |
| `licenses` | Third-party license texts distributed with this project |

Crestron-specific drivers may keep their processor test project in the **same repository and solution** as the driver. Platform-independent libraries should keep Crestron packaging out of their repositories. A separate collection repository can hold all their processor package projects in one solution, referring to the original libraries and NUnit test projects.

Local convenience solutions or source mappings can expose those external package projects in Visual Studio without publishing Crestron-specific changes in an independent library repository. Keep such machine-specific files locally excluded. Processor `.pkg` files are release assets; there is no requirement to publish test packages to NuGet.

## Runtime and packaging compatibility

The build keeps compiler output, merged output and processor-patched output separate. Application dependencies are merged with ILRepack; Crestron SDK assemblies remain platform dependencies.

The post-merge pass handles the Crestron Home restriction against locally defined `System.*` types by renaming shim namespaces to `_Stripped.System.*`. This happens after compilation and merge; it does not require library source code to rename its normal `System` compatibility declarations.

Additional adaptations repair affected compiler attribute references, NUnit reflection-name lookups and overloaded indexer metadata lost during merging. The async-state-machine lookup uses `mscorlib` for `net472`. These are documented packaging adaptations to official NUnit binaries, not a separately maintained framework fork.

The package builder stages the host UI and any explicit test data, checks discovery where configured, constructs the `.pkg`, verifies the test host's metadata and normalizes ZIP separators. Third-party license notices are part of the distribution requirements even when the corresponding DLLs have been merged.

## Validation and known limitations

Both Windows validation and actual processor execution matter. A passing desktop run cannot establish that filesystem, SDK or network behavior is identical under the processor's Mono runtime.

Verified development results include:

- 34 language compatibility tests passing on the processor.
- A selected NUnit framework run with 2,407 passed, no failures, 55 skipped and four performance warnings.
- External library unit and live suites operating through the same package host and runner.
- User-reported success for the KasaTapoCrestronDriver package's 34 unit cases and 20 processor lifecycle cases.
- Local TCP regression checks for package switching, saved selections, secure inputs, cancellation, interrupted runs and reconnecting.

These are recorded development results, not a claim that every later build, processor model or firmware release has been validated. See [Validation history](Validation.md) for the observed versions and limitations.

**Known repeated-run limitation:** the pinned NUnit 4.6.1 framework has a reproduced stream-comparison problem that can make some framework self-tests fail on repeated execution in the same process. The imported test sources also required documented repeated-run lifecycle adaptations. The project currently uses official NUnit and does not claim that the complete repeated self-test validation is green. Review the reproduction notes under `tools/NUnitRepeatRunReports` before interpreting such failures as failures in your application.

To build and validate the self-test assembly extracted from the actual package on Windows:

```powershell
pwsh -NoProfile -File .\Verify.ps1
```

This check records evidence under `artifacts/validation` and can expose the known repeated-run limitation. It does not deploy or execute on a processor. Run the transport validation executable after building its project for local runner/protocol checks. Its synthetic failing fixtures are intentional checks of failure reporting, not part of the processor package.

The package validator's optional `--run-twice` mode executes non-manual suites in the same loaded assembly. Use it only for suites that can run on Windows; a suite requiring the processor SDK runtime still needs validation on the processor. No test framework can guarantee cleanup after a fatal process failure, force a hung device call to return, or make arbitrary test code safe to execute on an occupied system.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| No package appears in discovery | Confirm the package has been imported **and added as an active driver**, inspect its tile, and check multicast/firewall access. Use the tile's current port for a manual connection. |
| Wrong package or suite | The top dropdown selects the package; the lower dropdown selects a suite within it. You can switch packages while connected and idle. |
| No individual tests in the tree | Click Discover for the selected suite. Run all does not require the tree. |
| Run selection is disabled | Select a fixture or test node after discovery, and ensure the host is idle. |
| Port changed after an update | Use Find packages again. Do not assume an automatically assigned port remains fixed. |
| SFTP authentication fails | Check the credentials for this particular processor and access to the shared processor identity file. |
| Host fingerprint changed | Verify the processor's identity before resetting saved trust; do not treat the warning as a test failure. |
| Live tests say settings are missing | Select the expected files with Test inputs… for the correct suite. A library's local JSON is not automatically embedded in its processor package. |
| Settings edits appear ineffective | Run a new discovery/run to resend files and check that fixtures reload configuration between operations. |
| Test passes on Windows but fails on the processor | Inspect processor-side network discovery, SDK/runtime behavior, paths, permissions and timing. Compare complete captured output. |
| Operation appears hung | Inspect Live output and the active test. Cancel is cooperative. If the host exits, collect the incomplete-run capture and processor logs. |
| Framework self-tests produce timing warnings | Inspect the warning details and platform-specific assumptions; warnings are distinct from assertion failures. |
| Build reports normalized package paths | `FixPkgPathSeparators` is normalizing ZIP entry separators for Home; the informational message is not a test failure. |
| Driver or test tile has the wrong UI/identity | Rebuild with the shared packaging checks; keep tested driver resources separate from the host's top-level resources. |

## Privacy and local configuration

Deployment credentials, device settings, real test results and personal paths must not be committed or attached to releases. Use `.git/info/exclude` for local settings. It is private to each checkout and must be configured again after a fresh clone. `.gitignore` remains for ordinary build-output exclusions.

| Local data | Location / treatment |
| --- | --- |
| SFTP deployment settings | Project `.csproj.user`; locally excluded, plaintext XML |
| SDK and personal path overrides | Project `.Local.targets`; locally excluded |
| Saved processor credentials and trust | `%LOCALAPPDATA%/CrestronHomeNUnit/ProcessorKeys.dat`; Windows DPAPI |
| Window position, size and maximized state | `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.window.local.json`; saved immediately as the window changes; minimization is ignored |
| Restart selections | `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.selections.local.json`; contains no credentials |
| Optional runner connection defaults | `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.local.json` |
| Selected input-file paths | `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.inputs.local.json` |
| Actual device settings | Files chosen by the user, outside published source/package content |
| Operation captures | User Documents results directory or local validation artifacts; review/redact before sharing |

An exclusion does not untrack a file that is already in Git and does not erase earlier commits. Before publication, review both tracked files and history, plus the contents of every release archive. The upstream `vendor/nunit/nunit.snk` is NUnit's published test signing key used for friend-assembly access; it is not a deployment credential or a private project signing key.

## Releases and versioning

Build the Windows x64 release ZIP with:

```powershell
pwsh ./PublishRunner.ps1
```

This publishes a self-contained .NET 10 Windows Forms application under `artifacts/runner` and creates `CrestronHomeNUnit.Runner-win-x64.zip` plus its SHA-256 file. Extract the complete ZIP and launch `CrestronHomeNUnit.Runner.exe`. No installer or administrator access is required to extract and run it. An ARM64 build can be requested with `-Runtime win-arm64`; the currently validated release is x64.

The publish script pins the bundled runtime to 10.0.12, includes its license and third-party notices, disables trimming, and rejects private settings in the output. Future releases should update `-RuntimeVersion` to the selected serviced .NET 10 version and repeat validation: a self-contained application receives runtime fixes by replacing its bundled runtime, rather than through a separately installed desktop runtime. This runner upgrade keeps the existing processor protocol; compatible deployed `net472` packages do not need redeployment.
The initial distribution consists of the Windows runner ZIP and the NUnit self-test `.pkg`, accompanied by license/attribution material, release notes and checksums. Additional library and driver test packages can be released independently by their owning repositories. Test packages are not published as NuGet packages by this project.

Driver manifests use four numeric version components:

- **Debug builds:** increment the fourth component for each development package update.
- **Local Release builds:** preserve the current manifest version.
- **CI Release builds:** use the explicitly requested release version and reset the fourth component to zero; CI builds without an explicit version retain the existing increment behavior.

The existing version script recognizes `CI`, `TF_BUILD` or `GITHUB_ACTIONS`. A Git tag should identify the committed source used for the release, and release notes should identify the packaged driver version and pinned NUnit version. To publish a release, run the **Release** workflow on `main` and supply a three-part version, such as `1.0.0`. CI updates `Version.props` and the processor manifest (`1.0.000.0000`), creates a local release commit, builds and validates the outputs, then pushes that commit and its annotated `v1.0.0` tag together. Only successful validation is followed by a GitHub Release. The release contains the self-contained Windows x64 runner ZIP, processor `.pkg`, documentation/license ZIP and `SHA256SUMS.txt`. No processor deployment or NuGet publishing takes place. Future releases must use a new version and tag.

The runner stores connection defaults and selected input-file paths under `%LOCALAPPDATA%/CrestronHomeNUnit`, alongside its existing protected credential store and restart preferences. On first use it imports legacy `Runner.local.json` and `Runner.inputs.local.json` from beside the executable, or from the sibling `net472` build output when upgrading a Visual Studio build. Existing destination settings win, and original files remain intact. When installing into a different directory, copy those two legacy files beside the new executable once to import them. Keep actual test-input files at their selected paths. These settings do not belong in source control or release archives.

When upgrading, close the Windows runner before replacing its files. Import the new processor `.pkg` and apply the Home driver update as needed. Rediscover packages after activation so the runner uses the current endpoint. Runner-only UI fixes generally do not require processor deployment; protocol changes can require updating both ends.

## Licenses and attribution

Project-owned source, scripts and documentation: **Copyright (c) 2026 Neil Colvin**, under the [MIT License](LICENSE).

**NUnit framework and self-tests:** Copyright (c) Charlie Poole, Rob Prouse and Contributors, under NUnit's MIT license. The pinned v4.6.1 license specifically records Copyright (c) 2024 Charlie Poole, Rob Prouse. The complete unmodified text is included in [vendor/nunit/LICENSE.txt](vendor/nunit/LICENSE.txt). NUnit is used both as the executing framework and as the source of the included framework self-test suite. See [NUnit's repository](https://github.com/nunit/nunit), the [v4.6.1 source](https://github.com/nunit/nunit/tree/v4.6.1), and [local provenance](vendor/nunit/PROVENANCE.md).

Imported source retains its original notices. Local modifications are identified separately; the root license does not replace upstream licenses. Additional copied compatibility sources and restored dependencies are attributed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md), with license texts under `licenses`.

This project references **Crestron.DeviceDrivers.DevKit**, which is subject to Crestron's SDK license agreement. That agreement governs the SDK libraries; the source code in this repository is licensed independently under the terms above. Crestron SDK components are platform/build dependencies and are not relicensed by the project's MIT license. Obtain the SDK from Crestron under its applicable terms.

When redistributing a runner, processor package, or derived package, preserve the applicable copyright, license and attribution materials, including those for dependencies merged into a single assembly.
