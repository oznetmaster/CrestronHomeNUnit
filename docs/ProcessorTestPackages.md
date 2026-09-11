# Processor test packages

`New-ProcessorTestProject.ps1` creates an Entity V2 package project referencing an existing NUnit test project. The original fixtures remain the single source for desktop and processor builds. Package projects import the shared host, runtime, packaging and deployment tools; they do not maintain copies of those components.

## Repository ownership

Platform-independent library repositories contain only their libraries and ordinary NUnit test projects. Their processor package projects live in the separate CrestronLibraryTests collection, with one Visual Studio solution. Source projects are referenced through that collection's sources directory. Crestron-specific drivers may keep processor test projects in their own driver repositories and solutions. Never add Crestron packaging references or documentation to independent library repositories.

## Create a package project

Run from the CrestronHomeNUnit checkout:

```powershell
pwsh -NoProfile -File .\New-ProcessorTestProject.ps1 `
  -TestProject ..\CrestronLibraryTests\sources\Example\Example.Tests\Example.Tests.csproj `
  -OutputDirectory ..\CrestronLibraryTests\packages\Example.ProcessorTests `
  -Solution ..\CrestronLibraryTests\CrestronLibraryTests.sln `
  -SuiteId example -TestNamespace Example.Tests `
  -DisplayName "Example Tests" `
  -ExpectedUnitTestCount 42 -IncludeLiveTests
```

The test project must support net472 and use the official NUnit framework version used by the host (currently 4.6.1). Keep desktop-only test SDKs, adapters and collectors private using `PrivateAssets="all"`, so they do not become processor dependencies. The test framework and application dependencies remain normal references.

The generator refuses to overwrite an existing output directory. Use -Solution to add the package to the collection or driver solution. Without that argument, it adds the package to the solution in its parent directory when unambiguous. It does not create a separate solution. Command-line builds skip processor packaging unless `-p:BuildProcessorTestPackages=true` is supplied, preserving ordinary library and CI builds without the Crestron SDK. It also adds local settings patterns to the owning repository's `.git/info/exclude`; it does not add settings rules to tracked `.gitignore` files.

Open the repository solution and build the package project in Visual Studio, or pass `-p:BuildProcessorTestPackages=true` when explicitly building it from the command line on Windows. It records a path to the SDK checkout used by the generator; override `ProcessorTestSdkRoot` in a locally excluded `.Local.targets` file when needed. Packaging requires Visual Studio/MSBuild, the Crestron Driver SDK, PowerShell 7 and the existing ILRepack tooling.

Build output is `bin\Debug\net472\<project>.pkg`. Local Debug builds increment the fourth version component. Local Release builds do not change the manifest version; CI Release builds increment the release component. SFTP deployment uses the shared `Deploy.ps1` and local `.csproj.user` settings, only for Debug builds inside Visual Studio with `DeployAfterBuild=true`.

## Suites and validation

`ProcessorTests.json` declares each suite's ID, display name, NUnit filter and optional expected case count. The runner receives the catalog when connecting. The primary suite excludes the `Live` category. `-IncludeLiveTests` adds a separate live suite, marked `ManualOnly=true`, with a Home status display. Live execution is started only by explicit selection in the Windows runner.

The build merges application dependencies, applies the processor metadata fixes, stages the Home UI, and validates discovery against that exact patched assembly before constructing the package. Invalid tests or a mismatched expected case count stop packaging. A failed build must not be deployed; an older package may still be present in the output folder.

For an additional desktop check, run the package validator with `--run-twice`. It executes only suites not marked `ManualOnly`; hardware suites are never executed by this check. Processor behaviour must still be validated after deployment.

## Discover and connect

Choose **Find packages** in the Windows runner. Packages announce their name, processor identity and automatically assigned TCP port through mDNS on UDP 5353. The runner lists packages sorted by processor. Selecting a package automatically connects when its processor credentials are available. Otherwise enter the processor's existing SFTP username and password and use **Connect**. Credentials are shared across that processor's packages and remembered using Windows DPAPI. Suite names are loaded from the connected package; the suite selector stays disabled until then. `PrepareRunner.ps1` can import the credentials already used for deployment. A read-only SFTP login retrieves an internal processor-wide test token, so there is no separate pairing key to manage.

Multicast discovery works on the local network segment. If it is blocked, enter the processor IP and the current TCP port shown on the Home tile. A fixed `Port` can be configured for that case; zero is the default for automatic assignment. All packages use `/user/Data/CrestronHomeNUnit/ProcessorIdentity.txt`, outside driver version directories. The first package creates it. Multiple packages and processor discovery have been exercised during processor development; verify access and discovery on each target installation.

## Runner-supplied test inputs

Use the updated Windows runner, select a suite, then choose **Test inputs…**. Select the files the suite expects, such as `LiveTestSettings.json`. Each processor and suite has a separate selection. Only local file paths are retained in `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.inputs.local.json`. Connection defaults, credentials and restart preferences are also stored outside the checkout. Any legacy settings files remaining in a checkout belong in `.git/info/exclude`.

The runner reads the current file contents before discovery or execution. Protocol 2 uses pairing-key challenge/response and protects test-input payloads with AES-CBC plus HMAC-SHA256, with separate keys and a random IV. The authentication tag binds the inputs to the session, request and suite. Pairing keys and input contents are not sent as plaintext. This protects configuration transfer; ordinary test names, results and output are not encrypted.

The host validates plain filenames and limits inputs to 16 files, 1 MB per file and 4 MB in total. Inputs are stored separately for each suite in the driver's private data directory, outside the package. Files remain stored until replaced or cleared, but storing files never enables live execution from Home. **Clear inputs** clears the saved selection and removes the processor copy on the next discovery or execution. A runner with no saved selection leaves existing processor inputs in place.

Tests locate input files using:

```csharp
string directory = TestContext.Parameters.Get ("TestDataDirectory", AppContext.BaseDirectory);
string settingsFile = Path.Combine (directory, "LiveTestSettings.json");
```

Reload configuration between operations. An assembly-level static cache initialized during first discovery will otherwise hide subsequent input changes. Never include credentials in public package resources. Tests themselves control what they log; assertion output can expose any values the fixture chooses to print.

Updated packages require the protocol-2 Windows runner. Older deployed hosts must be updated before using this runner with them.

## Examples of package ownership

Independent library packages belong in a separate processor-test collection; driver packages can remain beside the driver in its own solution. Both use the same shared host and runner. Supply actual network/device settings through the runner, never through committed package resources.

## Reusable explicit-selection contract

This behavior belongs to the shared package host and runner. Any future suite declared `ManualOnly=true` requires `EnableLiveTests=true` on its run request. The runner sets this flag when the user selects that suite; ordinary suites do not opt in. Discovery receives the same override so data sources can enumerate configured devices before running tests. Discovery itself does not execute fixture tests.

For every operation, the host passes the NUnit parameter `EnableLiveTests` as true only for an explicitly selected manual suite, and false otherwise. A Home command cannot run a manual suite. The value exists only in that operation's NUnit context; it is never written into uploaded JSON and is never inherited by later operations. The upload authentication tag also covers the flag.

Fixtures should read this standard NUnit parameter rather than require the user to maintain a second enable switch. For legacy configuration classes, after reading JSON:

```csharp
settings.Enabled = TestContext.Parameters.Get ("EnableLiveTests", settings.Enabled);
```

The JSON fallback preserves normal Visual Studio/NUnit-adapter behavior when no processor override exists. Processor operations always supply the parameter explicitly. Reload settings for each operation, including discovery; avoid static cached configuration. Device credentials, addresses and other options remain in input files, separate from execution intent. The project generator already supplies the manual-suite metadata and a Home status display without a live-run button.

Kasa live settings support stable deviceId or unique alias selectors. Prefer those over saved IP addresses. Test execution resolves the current address through the library's discovery API; NUnit discovery itself only enumerates configured selectors. Real settings and live-result files remain private and must not be published with the package.


## Standalone tiles, UI resources and licenses

Every package includes its own host and standalone Home tile; installing the NUnit self-test host separately is not required. The default tile exposes the primary ordinary suite and discovery. Additional ordinary suites can be exposed by customizing the UI; manual/live suites cannot be run from a Home command. See the repository README for the complete runner and tile workflow.

Keep UI files used by a tested driver under a separate data root such as DriverTestData. The shared package builder preserves the host's top-level UI and verifies that generated metadata belongs to the test host. Driver test references should suppress the production driver's own merge and deployment side effects.

The shared host's license and third-party notices are staged under Licenses/CrestronHomeNUnit. Package authors must additionally include the notices for their tests and application dependencies. Processor test packages are released as .pkg assets, not NuGet packages.
