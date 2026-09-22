# VS Code: Windows builds, NUnit tests and processor workflows

VS Code can use the same NUnit test projects and processor workflow adapter as Visual Studio. No separate Crestron VS Code extension or replacement adapter is required for the paths validated here.

There are two kinds of test project:

| Project | Target | Adapter |
| --- | --- | --- |
| Ordinary unit or live tests | net472, or the framework supported by the library | NUnit3TestAdapter |
| Processor workflow container | net10.0 | CrestronHomeNUnit.TestAdapter |

A processor package still targets net472. The .NET 10 container runs on the Windows development computer and coordinates local tests, deployment, processor tests and cleanup. It does not require .NET 10 on the processor. The same NUnit fixture sources can run locally and on the processor, subject to their runtime requirements.

## Install the prerequisites

1. Install VS Code for Windows and the .NET 10 SDK. A runtime alone cannot build the projects.
2. Install Microsoft's **C#** and **C# Dev Kit** extensions. Their .NET Install Tool dependency does not replace the development SDK.
3. Install the .NET Framework 4.7.2 developer/targeting pack if the project needs local reference assemblies. Follow the project's build instructions for Crestron's SDK, ManifestUtil, merge tools and PowerShell. Crestron packaging has additional requirements beyond compiling an ordinary C# library.
4. Install Git and clone the source. Open the solution's folder and select its `.sln` or `.slnx` solution in Solution Explorer. Trust that folder through VS Code's normal workspace-trust prompt only after reviewing its source.

If your driver has a desktop SDK lifecycle harness, also configure its documented local SDK dependencies. A DLL left in a previous build output is not sufficient: it must remain an explicit build reference so it is available after rebuilds. This prerequisite caused a correctly blocked local gate during validation.

A complete Visual Studio IDE is not inherently required to run `dotnet build` or `dotnet test`. This validation was performed on a computer that already had Visual Studio and its build prerequisites, however; it is not a clean-machine validation of a minimal tools-only installation. Old-style non-SDK projects may require MSBuild from Visual Studio Build Tools and a different C# project-system configuration.

Keep `LangVersion=latest` if that is the project's policy. The validation used SDK 10.0.401 and C# 14 with net472, including a field-backed property. The language version does not upgrade the target runtime or supply missing framework APIs; retain the project's compatibility packages and shims.

## Enable the tested net472 test path

The validated configuration used VS Code 1.138.0, C# 2.160.4, C# Dev Kit 3.40.204, NUnit3TestAdapter 6.3.0 and Microsoft.NET.Test.Sdk 18.10.1.

C# Dev Kit does not claim general .NET Framework project support. For the SDK-style net472 projects tested here, its lightweight Testing Explorer worked with this workspace configuration:

```json
{
  "dotnet.testWindow.skipTargetFrameworkCompatibilityCheck": true,
  "dotnet.testWindow.enableNextTest": true
}
```

Put these entries in `.vscode/settings.json`. The compatibility override is opt-in: the extension warns that unsupported targets can behave incorrectly. Treat these recorded results as a tested configuration, not a Microsoft support guarantee. See Microsoft's [C# Dev Kit FAQ](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq) and [testing documentation](https://code.visualstudio.com/docs/csharp/testing).

The older test-explorer path (`enableNextTest=false`) produced a VSTest translation-layer `MissingMethodException` with this toolchain. Use the tested lightweight path. Pin or record SDK and extension versions when reproducing a problem.

## Build and run ordinary tests

Optional [task](examples/vscode/tasks.json) and [workspace-setting](examples/vscode/settings.json) templates are provided. Copy their entries into your own `.vscode/tasks.json` and `.vscode/settings.json`, preserving existing settings. The tasks prompt for a project path; they contain no machine-specific paths or credentials.

Use **Terminal > Run Build Task**, or run the project's usual build command in VS Code's terminal. VS Code does not replace the build system. For a driver, disable incidental deployment while validating a local build:

```powershell
dotnet build path/to/Driver.csproj -p:DeployAfterBuild=false
```

Open **Testing** in the activity bar. Build the test project and refresh the test list if necessary. Select an ordinary fixture and choose **Run Test**. Multi-target projects appear separately for each framework; select net472 when validating the Windows .NET Framework path.

Do not choose Run All on a solution containing live tests or processor workflow containers unless those operations are intended. For a non-live command-line run:

```powershell
dotnet test path/to/Library.Tests.csproj -f net472 --filter "TestCategory!=Live"
```

A source-file command can report no tests even when the fixture appears in Testing Explorer. Select the fixture from the test tree. This distinction mattered during validation: discovery and execution worked, while our source-file lookup did not. Custom automation must preserve the exact test-controller IDs; a drive-letter case difference made an otherwise valid ID fail to match.

## Add the processor workflow

Follow [the workflow-container guide](VisualStudioTestExplorer.md#add-a-workflow-container). Use a separate net10.0 project with the released `CrestronHomeNUnit.TestAdapter` package. The public `Workflows.xml` sidecar is the same in both editors. Our VS Code validation uses package 1.12.1.

Provide the private plan and settings as described in [private configuration](VisualStudioTestExplorer.md#private-configuration). The sidecar names an environment variable containing a **file path**, not a password. Set that path before starting VS Code; an already-running Code process can retain its old environment. Keep credentials, plans, live-device input and output evidence outside the public checkout. Do not put passwords in `.vscode/settings.json`, `tasks.json`, `launch.json` or `Workflows.xml`.

Select just the intended workflow entry in Testing Explorer and run it. The workflow runs its own local test stages, so do not also select those projects in the same run. Local test stages must not include the workflow container itself.

The workflow uses the same processor reservation as the CLI and Visual Studio. A reservation held by another cooperating job must be respected, including one running on another computer. Choose a development processor with a free reservation and review any live controls or actual-driver update included in the plan.

For temporary test packages, configure both options:

```json
{
  "removeTestInstanceAfterRun": true,
  "removeTestPackageAfterSuccessfulRun": true
}
```

Package cleanup requires confirmed instance removal. Failed or uncertain runs retain evidence and may retain the package or reservation for recovery. These options do not turn a manual deployment into a temporary CI deployment. See [the processor workflow guide](ProcessorTestWorkflow.md).

## Results, failures and stopping

The workflow's aggregate result represents its required gates. Progress and the private evidence-directory path are written to test output. Retained workflow JSON, local TRX and processor NUnit XML remain the detailed evidence if an editor does not display every child result.

A missing private configuration is a failed attempted run, not a passed or skipped hardware test. Inspect test output as well as the icon. Do not interpret an unconfigured negative test as hardware validation.

**Cancellation limitation in the tested C# Dev Kit version:** Testing Explorer's Stop action ended a workflow without completing cleanup. An offline diagnostic using the same executor recorded entry into execution, but no call to `ITestExecutor.Cancel` and no execution-finally callback. A positive control directly called the same instrumented executor and passed the existing regression test that waits for cleanup. This identifies a limitation in the tested editor/test-platform cancellation path; it does not identify the exact upstream component responsible. The missing callback matches the symptom in the existing [C# Dev Kit issue #947](https://github.com/microsoft/vscode-dotnettools/issues/947), which was still open when checked on 22 September 2026.

Allow processor workflows launched from this Testing Explorer to finish normally. If a run must be interrupted, treat it as an aborted run: retain the evidence, inspect the processor reservation and confirm execution/activation have stopped before recovery. Do not automatically clear the reservation or retry. Prefer the CLI's documented cooperative cancellation when controlled interruption is required; terminating a VS Code task or closing its terminal is not a substitute for that protocol. See [cancellation and recovery](GitHubHardwareCI.md#cancellation-and-recovery) and the exact-owner reservation commands in that guide.

The cancellation validation stopped during a local-only stage, before deployment. The retained reservation was subsequently released through the public CLI after verifying that stage and its exact owner. This is a known limitation, not a passing cleanup test. Remote execution is not a processor debugger; net472 debugging and remote breakpoints are outside the validation reported here.

## Validation scope

Recorded on Windows on 22 September 2026: C# 14 semantic editing, a net472 driver build through a VS Code task, two net472 NUnit probe tests passing twice, and 56 real library net472 tests passing in Testing Explorer. Separately, 130 library tests, 46 driver tests and 86 desktop lifecycle-harness tests passed through the command line; those totals are not additional Testing Explorer runs. The existing processor workflow adapter was discovered and executed with missing configuration as a negative test.

A complete test-only processor workflow then ran from VS Code: 86 local tests and 86 processor tests passed. The temporary test instance was removed and the shared processor reservation released. Package cleanup correctly preserved a pre-existing package path. No actual-driver update or live-device control was requested. The editor recorded one aggregate result; the retained local TRX and processor NUnit XML contain all 172 outcomes. This verifies the selected test-only workflow, not every optional deployment or live-testing branch.

A separate negative test deliberately required more local passes than its fixture contained. All 46 executed cases passed, but the gate failed; VS Code correctly retained a failed aggregate, deployment did not begin and the reservation was released. Its test output reported the failed stage. Individual child outcomes and detailed failures were not retained in the editor's saved result messages; use the private workflow evidence for those details.
