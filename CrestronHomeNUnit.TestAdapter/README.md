# Crestron Home workflow test adapter

Run a complete gated processor workflow from Visual Studio Test Explorer or VSTest-based `dotnet test`: local tests, processor test package deployment and activation, remote tests, optional live tests, optional production driver update and health checks, and configured cleanup.

Use a **separate .NET 10 test project** with `Microsoft.NET.Test.Sdk` and this package, both marked `PrivateAssets="all"`. Driver and processor test projects can continue to target net472. This adapter does not replace the NUnit adapter used by ordinary local unit tests.

The package also includes `CrestronHomeNUnit.Android` for a separate .NET 10 NUnit UI-test project. Reference this package, `NUnit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk` there. No `Workflows.xml` is needed in the UI project: the Crestron adapter discovers no workflows in that assembly, while the NUnit adapter runs its fixtures. Select the UI project through the private plan's `androidTests` stage, not its `localTests`. See [Android setup](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md). This is desktop test tooling; do not reference it from a net472 processor package.

Add a `Workflows.xml` file to the workflow project:

```xml
<Workflows>
  <Workflow id="development" name="Development processor workflow"
            settingsEnvironment="CRESTRON_HOME_WORKFLOW_SETTINGS" />
</Workflows>
```

The package copies this file beside the test assembly automatically. Set `CrestronWorkflowManifest` to use a different manifest filename. Discovery reads only this public manifest and never contacts the processor.

Before starting Visual Studio, set the named environment variable to a private settings file containing `planPath`, `userName` and `password`. The referenced plan defines processor identity and certificate pins, source projects, package targets, suites, live inputs and cleanup. Keep credentials, plans and test results outside published source and packages.

Select the workflow in Test Explorer to run it. Individual test outcomes are reported as child results; the workflow passes only if all required stages pass. Do not include the workflow project in its own local test stage. Stop is cooperative: uncertain remote execution retains the processor lock for inspection.

See the [Visual Studio guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/VisualStudioTestExplorer.md) and [workflow configuration guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ContinuousIntegration.md) for setup and limitations.

Copyright (c) 2026 Neil Colvin. MIT licensed. Independent developer tooling; not affiliated with or endorsed by Crestron or the NUnit project.


## Package acceptance checks

The repository's `tools/Test-AdapterPackage.ps1` restores a clean consumer using an isolated cache and an exact source mapping for the package under test. It verifies workflow discovery, manifest copying, ordinary NUnit use of the Android API and the expected failure when private workflow settings are missing. It also compiles the Android regression fixtures against the packaged assemblies, compares executed tests with discovery and verifies those assembly bytes against the archive. No processor, emulator or production-source project reference is needed for these package checks. They do not replace physical workflow or driver acceptance tests.

## Temporary managed-child fixtures

Version 1.10.0 adds optional `androidTests.managedChildren` targets to the combined workflow. The selected actual driver supplies the parent identity; the fixture receives actual created child IDs through `session.Context.RequireManagedDevice(alias)`. Setup and cleanup use DevTools 1.6.0 under the existing reservations. Cleanup requires independently confirmed restoration, preserves failed test outcomes and removes only the run's own children. Existing plans without targets keep their previous context format.

See the [plan example, fixture contract and recovery rules](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md#temporary-managed-children-for-a-test-run). The normal workflow passed a CP4-R development run with one selected editor Cancel fixture and verified restoration/removal. This is not final submission-candidate acceptance or certification.