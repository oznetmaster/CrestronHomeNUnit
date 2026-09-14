# Processor workflows in Visual Studio Test Explorer

The workflow adapter runs the same gated backend as the CLI. One Test Explorer entry represents one complete workflow: local tests, test-package deployment and activation, processor tests, optional processor live tests, optional actual-driver update and installed-driver checks, then configured cleanup. Individual local, processor, live and installed-driver results appear as child results after execution. The aggregate fails if any required stage or cleanup fails, including a failure after the actual driver was updated.

The adapter and its test container target .NET 10. Use a Visual Studio installation that supports .NET 10 and VSTest. The existing driver and processor test assemblies can continue to target net472. Remote execution does not attach a Visual Studio debugger to the processor.

## Add a workflow container

Use [the sample project](../samples/WorkflowTests/WorkflowTests.csproj) as the starting point. Add a separate .NET 10 workflow test project to your local solution, install `CrestronHomeNUnit.TestAdapter` version `1.2.0` and `Microsoft.NET.Test.Sdk`, and mark both references `PrivateAssets="all"`. No source checkout of the tooling is required. The sample uses the released package by default; contributors can set `UseSourceAdapter=true` to test source changes.

Add `Workflows.xml` to this project. The NuGet package copies it to the output automatically; set the `CrestronWorkflowManifest` project property to choose another filename.

The output assembly has a sidecar named `<assembly>.dll.workflow-tests.xml`. This is a public discovery manifest, for example:

```xml
<Workflows>
  <Workflow id="development" name="Development processor workflow"
            settingsEnvironment="CRESTRON_HOME_WORKFLOW_SETTINGS" />
</Workflows>
```

IDs must be unique within that container. Discovery reads only this manifest; it does not load private settings, contact a processor, build packages, deploy or run tests. Ordinary NUnit assemblies without this sidecar are ignored by the workflow adapter.

## Private configuration

Set the named environment variable to an absolute path to a private JSON file before launching Visual Studio. Restart Visual Studio after changing its inherited environment. The file has these fields:

```json
{
  "planPath": "C:/private/development-workflow.json",
  "userName": "development-user",
  "password": "replace-locally"
}
```

`CRESTRON_HOME_USER` and `CRESTRON_HOME_PASSWORD` override the corresponding values. The plan is the same private plan accepted by the CLI; see [continuous integration](ContinuousIntegration.md). Its certificate pins, exact package targets, live input files, removal policy and reboot authorization remain mandatory where applicable. Missing settings fail an attempted run; they never cause an unconfigured workflow to be reported as passed.

Keep settings, workflow plans, live inputs and generated results outside repositories or locally excluded with `.git/info/exclude`. The public sidecar contains only display names and an environment-variable name. Result files can contain device identifiers and test diagnostics; treat them as private development evidence.

## Run and stop

Build the workflow container, then select its workflow entry in Test Explorer and Run. Do not also select the underlying local NUnit projects in the same test cycle: the workflow runs those itself before deployment. Keep local test projects in `localTests`, not the workflow container or a solution containing it, to avoid recursive execution.

One workflow is the selectable gate. Child results describe that completed execution and are not independent commands for bypassing earlier stages. Rerunning the workflow repeats its required gates. Stage progress appears in the test output; the result lists the private evidence directory. TRX and NUnit XML retain the individual outcomes even after an incomplete workflow.

Stop requests cooperative cancellation through the shared backend and waits for its cleanup. It does not kill the test host or force a remote fixture to stop. If execution or activation is uncertain, the existing processor lease rules retain the instance and lease. Review those before retrying. Closing or forcibly terminating Visual Studio cannot guarantee cleanup.

Workflows execute serially within this adapter. The processor lease excludes cooperating jobs from other test hosts and machines. It does not prevent manual Configure operations or unrelated solution builds.

The same container works with VSTest-based `dotnet test`:

```powershell
dotnet test samples/WorkflowTests/WorkflowTests.csproj --list-tests
dotnet test samples/WorkflowTests/WorkflowTests.csproj --filter FullyQualifiedName=CrestronHome.Workflows.development
```

`--list-tests` remains offline. Running the second command operates the processor according to the selected private plan.

## Validation

Adapter regression tests cover offline discovery, stable identities, invalid manifests, selected-run deduplication, individual result mapping, cancellation with cleanup, malformed result files and exception redaction. Hardware validation and Visual Studio test-platform checks are recorded separately; a simulated adapter pass does not establish processor compatibility.

On 2026-09-14, the VSTest workflow container completed a real KasaTapo run on MC4-R / Home 4.11.322: 69 local tests, 69 processor tests, three processor live tests and seven installed-driver checks, all passed. The actual driver updated to Debug version 2.0.001.0001; the test instance was removed and the processor lease released. VSTest reported all 148 individual outcomes successfully. The 23 unrelated inventory entries retained their identities, models, room assignments and versions. Offline discovery and a non-matching selection filter were also checked through VSTest. A separate consumer of the stable 1.2.0 NuGet candidate was also built and run in Visual Studio Enterprise 18: Test Explorer discovered the workflow, displayed its category, and reported the expected missing-settings failure with an evidence link. Clean package acceptance separately verifies manifest copying and offline discovery using an isolated package cache. The 148-result hardware run was launched through VSTest; its full child-result tree has not been manually inspected in the Visual Studio UI.
