# GitHub Actions with your own Crestron Home processor

A developer can run processor tests in GitHub Actions using a self-hosted Windows runner on the same network as their Crestron Home processor. GitHub schedules the job on that computer; our CLI or Test Explorer adapter builds and tests the code, uploads the test package, waits for Home to load it, runs the processor suites and returns an exit code. No AI agent, interactive Windows runner or manual Configure session is required for supported workflows.

The processor is a test target, not a GitHub Actions runner. Your Windows computer needs to be online and awake. Keep processor management on your LAN; this arrangement does not require exposing it to the Internet.

## Choose where hardware jobs run

Keep public pull-request builds and ordinary unit tests on GitHub-hosted runners. Use a **private hardware-orchestration repository** for the self-hosted runner and reviewed source revisions. A job on a LAN runner can execute code with access to that machine, its credentials and physical devices. GitHub [recommends private repositories for self-hosted runners](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners).

Use a dedicated development processor. Live tests may control equipment; configure only devices you intend the tests to operate. State restoration is implemented by fixtures where supported, not guaranteed for every device. Read-only installed-driver checks do not establish that playback, heating, blinds or other controls work.

## Prepare the Windows computer

1. Install .NET 10 and the build prerequisites of the projects under test, including net472 targeting support and the licensed Crestron SDK/package tools where required. Follow the driver project's build instructions.
2. Install a released CrestronHomeNUnit CLI, or build a reviewed tooling revision. Pin that tooling version for repeatability. CrestronHomeDevTools is the management dependency used by the workflow.
3. Confirm that the runner account can access the processor's management, SFTP and discovery services and its dynamically discovered test-service port. Supply verified HTTPS and SSH fingerprints in your private plan.
4. In the private repository, open **Settings → Actions → Runners → New self-hosted runner**, select Windows and follow GitHub's generated installation and registration instructions. Add the custom label `crestron-home`. [GitHub's registration guide](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners)
5. Run the agent under a dedicated Windows account with access to the required tools and private files. Service accounts do not automatically inherit your interactive account's environment, SDK paths or saved credentials. Run the CLI manually under that account first.

During Windows registration, choose to install the runner **as a service**. It then starts automatically when Windows boots and does not require an interactive sign-in, an open console or Visual Studio. Manage it in Windows Services. The computer still needs to be awake and connected; sleep or shutdown makes the agent unavailable. If the runner was registered without service mode, GitHub documents removing/reconfiguring it to select that option. See [GitHub's Windows service instructions](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/configure-the-application?platform=windows).

Use a processor-specific agent label if separate computers serve different networks. With several processors reachable from one agent, the private plan selects the target by host or exact system name. The workflow rediscovers test-package ports; do not hard-code the last observed test port.

## Service build checklist

Validate the service account independently of your interactive Windows account. A successful desktop build does not prove that the service has the same tools, source checkouts or private settings.

- Provision credentials and plans outside the repository. Grant access only to the selected service identity and the administrators/users who maintain it. Do not copy your entire interactive profile or credential store.
- Pin every source dependency, including desktop test helpers and the processor packaging SDK. A project reference to a sibling repository requires that sibling at the expected location in the job workspace.
- Supply packaging tool paths explicitly. Install ILRepack for the service or provide its tool directory; an interactive user's global tool installation is not automatically available to the service.
- Keep the build root short. ManifestUtil can fail on long licence-file paths even when .NET builds successfully. Configure a short runner work directory, or use a temporary, unused drive mapping for the job and remove only the mapping that job created.
- Preserve Debug version counters outside disposable checkouts. Before updating an existing instance, reconcile a newer package built manually; do not assume a fresh checkout's revision is higher than the processor's catalogue version.
- Check each local test filter against its actual target framework. A fixture may carry the `Processor` category only in its net472 build. The workflow intentionally fails if a required local stage selects zero tests.

Use offline discovery first, then a test-only workflow with `removeTestInstanceAfterRun: true`. Verify processor execution, removal of the temporary instance and the released lease in the private evidence. Package files retained in the processor catalogue are separate from installed instances; instance cleanup does not purge catalogue storage.

## Create private configuration

Store the plan template, credential file, live inputs and result folders outside the checkout. Protect them with Windows file permissions for the runner account. Do not commit them, put them in workflow YAML, or publish them as artifacts. A local `.git/info/exclude` is available if a project requires a private file beside its source.

The credential file accepted by the CLI contains `UserName` and `Password`. Environment variables `CRESTRON_HOME_USER` and `CRESTRON_HOME_PASSWORD` override them. The plan holds the processor identity and fingerprints, required local/processor test counts, package projects, exact intended instances and optional live inputs. See [the complete plan reference](ContinuousIntegration.md#configure-a-private-run-plan).

For CI, source paths must refer to the **checked-out revision for that job**, not an unrelated working copy on your computer. The supplied `Invoke-ProcessorCi.ps1` wrapper resolves path fields in a private plan template:

| Prefix | Resolves to |
|---|---|
| `${CHECKOUT}/source/...` | A path under the current Actions workspace. |
| `${PRIVATE}/LiveTestSettings.json` | A path under your separately protected private-input folder. |

Use `${CHECKOUT}` paths for `sourceRoots`, local test projects, test/actual package projects and package output paths. Use `${PRIVATE}` paths for suite inputs and optional `actualDriver.initialConfigurationFile` (supported from 1.2.1). Each referenced build project and output must stay within the checkout. Other plan fields, including device IDs and expected values, are preserved. The resolved plan is written to the private result directory, not back into source control.

Example path fields to put into an otherwise complete private plan:

```json
{
  "sourceRoots": ["${CHECKOUT}/source"],
  "localTests": [
    {
      "project": "${CHECKOUT}/source/MyDriver.Tests/MyDriver.Tests.csproj",
      "minimumPassed": 20
    }
  ],
  "testPackage": {
    "project": "${CHECKOUT}/source/MyDriver.ProcessorTests/MyDriver.ProcessorTests.csproj",
    "packagePath": "${CHECKOUT}/source/MyDriver.ProcessorTests/bin/Debug/net472/MyDriver.ProcessorTests.pkg",
    "instanceName": "Development tests",
    "locationId": 12345
  }
}
```

This fragment is not a complete runnable plan. Supply your own processor, fingerprints, actual suite IDs/counts and room ID. For an existing instance, bind its exact expected device ID. Omit `actualDriver` for a library or a test-only run. Actual-driver update plans require both processor live suites and installed-driver checks.

## Add the workflow

Copy [the private-repository example](../examples/github-hardware.yml) into the private orchestration repository's `.github/workflows` directory. Replace `YOUR_ACCOUNT/YOUR_DRIVER` with the repository to test. Review and pin the workflow/action/tooling revisions according to your policy.

Configure these repository variables:

| Variable | Value on your Windows agent |
|---|---|
| `CRESTRON_CI_SCRIPT` | Absolute path to a reviewed copy of `Invoke-ProcessorCi.ps1`. |
| `CRESTRON_CLI_PATH` | Absolute path to the CLI executable or DLL. |
| `CRESTRON_PLAN_TEMPLATE` | Private plan-template path. |
| `CRESTRON_CREDENTIALS_PATH` | Private credential-file path. |
| `CRESTRON_PRIVATE_ROOT` | Private input folder. |
| `CRESTRON_RESULTS_ROOT` | Private results folder outside the checkout. |

Repository variables contain local paths, not passwords. Define a protected environment named `processor-development` and configure the approval/restrictions your GitHub plan supports. Review the exact source commit before approving a hardware run. The example requires a full 40-character commit SHA and is intended for manual dispatch from the private repository's main branch.

The example serializes jobs in that repository and disables cancellation of an existing job merely because a newer one arrives. The processor-side workflow lease coordinates cooperating jobs across repositories and computers. No automatic lease expiry or lock stealing is used.

GitHub may schedule the job while local development is in progress. The processor lease is acquired before any workflow build/deployment/test stage, rather than relying on the time the job was queued. Set `leaseWaitSeconds` in the private plan to a bounded wait (0–86400 seconds); zero, the default, fails immediately if busy. Waiting never cancels the owner, expires its lease or retries an ambiguous mutation.

The new CLI also supports a durable reservation for manual Configure sessions:

```powershell
CrestronHomeNUnit.Cli.exe reserve --settings C:/private/processor.json --receipt C:/private/manual-reservation.json
# Perform your manual development work, and wait for its operations to finish.
CrestronHomeNUnit.Cli.exe release --settings C:/private/processor.json --receipt C:/private/manual-reservation.json
```

The settings must include `Host`, `SshFingerprint`, `UserName` and `Password` (or the supported environment overrides). Use a new receipt path for each reservation. Release verifies the exact saved owner; it never removes another owner's lease. Reservations persist after the command exits and after a computer restart. Keep the receipt private and release it deliberately when manual work is complete. A receipt written before a failed acquisition records the attempted owner, not proof that a reservation was acquired.

**Coordination coverage:** the workflow, new standalone CLI test commands, Windows runner, updated Home tile execution, DevTools mutations and revised project deployment scripts use the shared lease. Desktop/tile exclusion and the build deployment path have passed MC4-R hardware validation. Use NUnit tooling and rebuilt processor test hosts 1.2.1 or later, with DevTools 1.1.0 or later: rebuild/update every participating tool and processor test host before enabling unattended hardware jobs. Already-installed older test hosts can bypass the gate. Crestron's Configure software and arbitrary SFTP/SSH clients cannot be forced to honor our lease. Reserve the processor before manual Configure work. See [the shared protocol and build settings](https://github.com/oznetmaster/CrestronHomeDevTools/blob/HEAD/docs/ProcessorCoordination.md). GitHub concurrency alone is insufficient.

Start with a library/test-only plan and optional test-instance cleanup. After that works, enable processor live suites with private inputs. Add actual-driver update and installed-driver checks only when those targets and checks have been configured deliberately. V1 reboot must be separately authorized by the plan; Entity V2 normally does not require it.

## Results and gates

The job fails when the CLI reports failed, skipped required, incomplete or insufficient tests, an activation failure, or unconfirmed required cleanup. It waits for the uploaded driver version to become available before selecting install/update, and verifies the loaded version afterward. The Windows agent must allow enough time for packaging, catalogue refresh and device startup.

Raw TRX, NUnit XML, deployment receipts, package hashes and lease state remain in the private result folder. The example publishes only the overall workflow outcome in GitHub's job summary; it does not upload live logs, credentials, input files or result archives. Add a reviewed redaction/export step if you need more public detail.

A successful hardware job can gate later work in the same pipeline with `needs: hardware`. An actual-driver update within the CLI workflow already waits for all preceding required tests. To make a hardware result a required check in a *different public repository*, add a separate narrowly scoped GitHub App or authorized check-reporting integration that posts the result against the exact tested SHA. The example intentionally grants no cross-repository write token and does not claim to implement that reporting bridge.

The current workflow builds Debug packages and retains their exact tested bytes. Passing it does not certify an independently rebuilt Release package. Public release policy must keep that distinction explicit.

## Cancellation and recovery

Use the CLI's cooperative cancellation when running locally. Cancelling an Actions job, stopping its Windows service or powering off the computer can terminate the process before cleanup finishes. Treat such a run as incomplete. Inspect the saved results and remote lease; confirm that execution and activation stopped before removing a test instance or clearing a stale lease. Do not automatically reboot or retry an uncertain update.

When diagnosing a current processor problem, use live SSH logging. Home's saved log files can lag behind current execution. Logs and workflow files may contain private configuration and should remain on the agent unless reviewed for sharing.

## Relationship to Visual Studio

[The Test Explorer adapter](VisualStudioTestExplorer.md) and the CLI use the same workflow backend. Developers can run the workflow locally from Visual Studio and use either the CLI or VSTest-based `dotnet test` for unattended GitHub jobs. An adapter job supplies the manifest's settings environment variable under the service account and generates its private plan against that job's checked-out sources. Keep its console output and TRX in the protected results folder, just as with the CLI example. Ordinary NUnit tests remain usable through NUnit's existing adapter. GitHub jobs do not require Visual Studio to be open.

The generic Actions example must be validated on each developer's runner account and network. Local hardware validation of our workflow is not evidence that a particular GitHub agent has been registered or that its job has executed.

## Recorded service validation

On 2026-09-15, a Windows GitHub Actions service running as NETWORK SERVICE completed real adapter workflows using published 1.2.1 tooling on a development MC4-R. Tesla library tests passed 236 local and 118 processor cases; KasaTapo driver tests passed 69 local and 69 processor cases; Overkiz driver tests passed 47 local and 47 processor cases. Each workflow removed its temporary instance and released its processor lease. These were test-only runs, without live device control or actual-driver updates.

Those jobs were manually dispatched in a private orchestration repository. This verifies unattended execution after scheduling; it does not establish an automatic public-PR trigger or a cross-repository required-check bridge. The reusable example remains a template to validate on each developer's own machine and network.
