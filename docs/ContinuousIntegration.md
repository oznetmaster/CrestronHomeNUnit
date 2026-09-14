# Automated development and processor testing

This guide explains the implemented CLI development workflow and how it connects local NUnit tests, CrestronHomeDevTools and processor test packages. It is the central integration guide for driver and library repositories.

**Current status:** the CLI implements the gated workflow and restores CrestronHomeDevTools from NuGet when built from source. Complete KasaTapo and explicitly opted-in Apple TV V1 update/reboot workflows have passed unattended hardware validation. V1 initial-install/removal reboot paths have simulated coverage only. Direct Visual Studio Test Explorer integration is still pending.

Post-deployment checks of the installed production driver currently read properties and validate expected values or ranges. Processor live-test fixtures can operate devices and implement their own state capture and restoration. The shared workflow does not yet provide a generic installed-device control/state-restoration backend or automatic rollback.

## Contents

- [Components and responsibilities](#components-and-responsibilities)
- [What a workflow does](#what-a-workflow-does)
- [Prepare a development agent](#prepare-a-development-agent)
- [Configure a private run plan](#configure-a-private-run-plan)
- [Run locally or in CI](#run-locally-or-in-ci)
- [Live tests and installed drivers](#live-tests-and-installed-drivers)
- [Readiness and version identity](#readiness-and-version-identity)
- [Results and deployment gates](#results-and-deployment-gates)
- [V1 development and reboot policy](#v1-development-and-reboot-policy)
- [Cleanup and interrupted runs](#cleanup-and-interrupted-runs)
- [Visual Studio integration](#visual-studio-integration)
- [Publication boundaries](#publication-boundaries)
- [Validated behavior and remaining work](#validated-behavior-and-remaining-work)

## Components and responsibilities

| Component | Owns |
|---|---|
| Ordinary NUnit projects and NUnit's VS adapter | Local tests of reusable logic; desktop SDK harnesses where needed. |
| Processor test package | Shared NUnit fixtures, dependencies, net472 test host and standalone Utility tile. |
| `CrestronHomeNUnit.Client` / `Transport` | Authentication, mDNS test-package discovery, changing TCP endpoints, encrypted inputs, execution and results. |
| `CrestronHomeDevTools` | Processor discovery/authentication, SFTP import, installation/update/reload/removal and loaded-version checks. |
| `CrestronHomeNUnit.Workflow` | Run order, source/artifact identity, required gates, processor lease, retained evidence and test-aware cleanup. |
| `CrestronHomeNUnit.Cli` | Headless command entry point for development scripts and CI; no UI or AI agent required. |
| Windows runner | Interactive discovery, selection, test inputs and result inspection. |

The two discovery mechanisms are separate. DevTools discovers processors using native Crestron discovery. Test packages advertise their dynamically assigned TCP ports using mDNS. Each package contains its own host; installing the NUnit self-test package is optional. Independent library packaging belongs in CrestronHomeLibraryTests or an equivalent separate repository; driver-specific packages can stay in the driver's solution.

## What a workflow does

1. Acquire an exclusive cooperating-workflow lease on the selected processor and identify the current source state.
2. Run every required local test stage and check actual result counts.
3. Build the test package and, if configured, the actual driver package in Debug with direct deployment disabled. Retain the exact resulting package bytes and hashes.
4. Upload/import the test package; install if absent or upgrade the intended existing instance. Wait for catalogue eligibility and the expected Loaded version, then discover and connect to its test service.
5. Run required processor unit/lifecycle suites and any required processor live suites, supplying their private input files.
6. Only after all required gates pass, deploy/activate the retained actual driver package when an `actualDriver` target is configured. Library/test-only plans omit this stage.
7. Check the intended installed driver's properties and related child instances using the configured read-only checks.
8. Save the outcomes. If requested and execution is confirmed stopped, remove the identified test instance and verify removal. Release the lease only when remote state permits it.

The actual driver is a different instance from the driver code exercised inside the test package. A passed synthetic fixture does not prove that the installed Home driver works. A passed local build does not prove a processor update happened.

## Prepare a development agent

Use a machine with .NET 10, the net472 targeting/build requirements of the driver projects, their packaging tools, and LAN reachability to the development processor. A Windows agent is the validated build environment. Processor discovery, HTTPS/WebSocket management, SFTP, mDNS and the selected dynamic test port must be reachable.

Use the self-contained CLI release, or check out `CrestronHomeNUnit` and the relevant driver/library projects. Set supported project/SDK path overrides privately. To build the CLI:

```powershell
dotnet build CrestronHomeNUnit.Cli/CrestronHomeNUnit.Cli.csproj -c Release -p:EnableProcessorWorkflow=true
```

`DevToolsProject` optionally selects a local library project for joint development. Normal builds restore CrestronHomeDevTools 1.0.0 from NuGet and include workflow support. The additional `CrestronHomeNUnit.Workflow.Tests` project tests the backend.

Some real Crestron SDK desktop lifecycle harnesses need `Newtonsoft.Json.Compact.dll` to read the production manifest. Supply the verified SDK/runtime copy via their `CompactJsonPath` property. This also applies to Entity V2 tests; it is not specific to V1 video servers. The processor already supplies it. It is not a dependency of DevTools or the NUnit transport. Maintainer CI can restore an authorized copy from encrypted secrets, verify its checksum and keep it in the agent's temporary directory. Never include that private runtime copy in source or processor packages. Fork jobs do not receive maintainer secrets.

Keep processor credentials, verified HTTPS/SSH fingerprints, device bindings and real paths outside tracked files. Use CI secret storage or private local files. The console's Windows-encrypted profile is bound to its user account and is not automatically portable to a CI service account.

## Configure a private run plan

Start with [test-only example](examples/test-only-workflow.example.json) or [driver-update example](examples/driver-workflow.example.json). They deliberately contain fictitious paths, host names, fingerprints and device IDs and are not runnable configurations. Save the customized copy outside the checkout or locally exclude it. Do not commit real settings by replacing the public examples.

The plan is deserialized into [WorkflowPlan](../CrestronHomeNUnit.Workflow/WorkflowPlan.cs). Property names are case-insensitive.

| Field | Requirement |
|---|---|
| `host` | Exact processor address for this workflow. Name selection is supported by the ordinary CLI; the workflow plan itself supplies the connection host. |
| `certificateSha256`, `sshFingerprint` | Independently verified HTTPS/WebSocket certificate and SSH host-key fingerprints. |
| `sourceRoots` | All source checkouts that determine the retained builds, including dirty edits and shared SDK sources. |
| `localTests` | Project, optional filter and positive `minimumPassed` for each required local stage. |
| `testPackage` | Build project, absolute output package path, unique instance name, room ID and optional expected device ID. |
| `processorSuites` | Required suite IDs and positive minimum counts. Use package metadata/discovery, not assumed generic IDs. |
| `liveSuites` | Required explicitly selected live suites, each with private input paths and a positive minimum. Empty for a test-only plan without live tests. |
| `actualDriver` | Optional production driver build/instance target. Omit when only validating a library or test suite. |
| `deployedChecks` | Named read-only checks against exact installed device IDs/models/properties; expected JSON value or numeric bounds. |
| `removeTestInstanceAfterRun` | Explicit choice to remove the test host after tests and evidence preservation. |
| `stageTimeoutSeconds` | Bounded stage wait, 600 by default. |

Every listed stage is required. Replace example `minimumPassed` values with meaningful counts for the suite; a single passing selected test must not unlock a gate requiring a full suite. Keep ordinary filters separate from manual/Explicit fixtures. `actualDriver` currently requires both `liveSuites` and `deployedChecks`, and its identity/path must differ from the test target. Read-only checks can target fixed children only when their parent chain reaches the updated actual driver; their IDs must be configured before the run.

Credentials come from `CRESTRON_HOME_USER` and `CRESTRON_HOME_PASSWORD`, or `userName`/`password` in the separate `--settings` file. The workflow's host and fingerprints come from its plan. This command does not automatically load DevTools console profiles.

## Run locally or in CI

```powershell
dotnet run --project CrestronHomeNUnit.Cli -- workflow --plan C:/Private/workflow.json --settings C:/Private/processor.json --results C:/Private/Results/new-run
```

Select a fresh result directory for every run. The workflow prints stage outcomes and whether the actual-driver update was attempted and verified; read `Workflow.json` for structured evidence. Unlike ordinary inspection commands, workflow stdout is stage progress rather than one JSON object.

The workflow exit code is **0** for a passed workflow, **1** for a returned failed workflow, **2** for configuration/exception failure and **130** for cancellation. The [ordinary test CLI](CommandLineRunner.md) has its own documented exit codes, including incomplete and zero-test outcomes.

A LAN CI job can invoke the built CLI using paths from private agent configuration. For example, after checking out and building reviewed sources:

```yaml
# Job fragment for a trusted, explicitly selected hardware run.
runs-on: [self-hosted, Windows, X64, crestron-development]
concurrency:
  group: crestron-development-processor
  cancel-in-progress: false
steps:
  - name: Run processor development workflow
    shell: pwsh
    env:
      CRESTRON_HOME_USER: ${{ secrets.PROCESSOR_USER }}
      CRESTRON_HOME_PASSWORD: ${{ secrets.PROCESSOR_PASSWORD }}
    run: |
      $results = Join-Path $env:PRIVATE_RESULTS_ROOT ([Guid]::NewGuid().ToString('N'))
      & dotnet $env:NUNIT_CLI_PATH workflow --plan $env:PRIVATE_WORKFLOW_PLAN --results $results
      exit $LASTEXITCODE
```

This is a job fragment, not a ready-made public workflow. Set the three path variables in the private agent environment, review the selected source and plan, and ensure the CLI was built with workflow support. Do not attach an untrusted pull-request job to processor credentials or real equipment. Public hosted CI can run offline tests without hardware. Archive hardware evidence only into an appropriate private location; raw build logs, test output and device properties may expose local configuration.

Building a processor project in Visual Studio can use its configured Debug deployment behavior. In an orchestrated run, direct deploy scripts are disabled so the workflow controls the order and evidence. Do not run a separate build or modify a participating checkout while the workflow is using its source snapshot.

## Live tests and installed drivers

Processor live fixtures use private `inputs`, usually `LiveTestSettings.json`. Their presence in `liveSuites` is an explicit required stage; the workflow supplies manual-suite enablement. NUnit `[Explicit]` semantics still apply: suite-level enablement does not make every Explicit test execute. Required skips fail the gate.

Bind real devices deliberately using the test suite's supported stable device ID/alias scheme. Do not silently select another device when discovery misses the configured target. Missing credentials, ambiguous binding, device unavailability or a restoration failure must remain visible as failed/incomplete evidence. A new run after an intermittent discovery failure must retain the earlier failed result.

The current installed-driver backend reads properties and validates expected values/ranges. It does not issue light, outlet, shade or heating commands. A future control backend would need to read and save current state, restore it reliably, and fail on restoration problems. Devices without readable state require an explicit alternative test design; never assume an initial value.

## Readiness and version identity

Upload, catalogue import, update eligibility, Loaded state and test-listener availability are separate milestones. The implementation waits for them, rather than assuming that completing SFTP makes the driver immediately updatable. Test connection attempts rediscover changing ports. Once a test request is submitted, it is not automatically replayed after a disconnect.

Numeric version comparisons preserve all four processor components. Zero-padding can differ; `2.0.000.0005` equals `2.0.0.5`, not `2.0.0.6`. Three-part public release tags do not replace the exact local Debug identity.

The workflow hashes tracked/non-ignored source files and records the package SHA-256. It retains the tested package bytes and refuses subsequent source changes; it does not rebuild the production package after passing the gate. Generated manifest dates and fourth-component Debug increments are normalized in source identity. Local secrets must already be excluded or outside the source roots.

The preview requires a newly built version absent from the catalogue. DevTools can independently reuse an already-current instance, but cross-run artifact reuse in the gated workflow is not implemented because matching version text alone cannot establish matching tested bytes.

## Results and deployment gates

| Evidence | Purpose |
|---|---|
| `Workflow.json`, `Stages.json` | Overall/stage outcome and actual-driver update status. |
| `BuildIdentity.json` | Source digest and build context. |
| Retained package/hash metadata and activation receipts | Exact bytes, catalogue availability and loaded-instance identity. |
| Local TRX / processor NUnit XML | Actual executed tests and outcomes. |
| Progress and summaries | What ran before a connection loss or incomplete stage. |
| `InstalledDriver.xml` | Individual read-only installed-device checks. |
| `Lease.json` | Run owner and lease release/retention state. |

Failures, skipped required tests, insufficient counts, cancellation, timeout, incomplete execution or inability to preserve stage evidence close the actual-driver deployment gate. A post-deployment check can fail after the driver is already updated; the result records that distinction. The backend does not automatically roll back or reboot to recover.

## Cleanup and interrupted runs

`removeTestInstanceAfterRun` removes only the recorded test-instance ID/model/version after results are saved and all stages using it have finished. It also runs after completed test failures. It does not remove the actual driver or the catalogue package.

If execution, activation or requested removal is uncertain, retain the test instance and processor lease for investigation. A missing tile or cleared room assignment is not enough. The lease at `/user/CrestronHomeNUnit-WorkflowLease` coordinates cooperating jobs across machines; it does not stop manual Configure or runner activity. It has no automatic expiry or lock stealing. Inspect the owned lease and remote state before clearing a stale lease manually. Do not replace an uncertain host with another one just to make the next run start.

Save live SSH logs when diagnosing an active problem; processor logs written to disk can lag. Log streaming is currently an external diagnostic aid, not a DevTools CLI command or automatic workflow evidence feature.

## Visual Studio integration

The existing NUnit adapter runs local projects in Test Explorer. Visual Studio can invoke the CLI through a terminal/external tool or a deliberately configured task now. That does not make remote stages appear as native Test Explorer cases.

A dedicated adapter mapping Local, Processor, Processor Live and Installed Driver results into one VS test cycle is still future work. Discovery must remain non-mutating; it must not deploy or operate devices merely because Test Explorer discovers tests. No claim is made that the current NUnit/MSTest adapters perform this orchestration.

## Publication boundaries

Development workflow execution is not a public release workflow. Publish production driver changes when runtime fixes or production dependency changes justify a release; test-only source updates do not require driver releases. Processor test packages can have separate GitHub release assets and are never NuGet packages. Independent library repositories remain free of Crestron packaging projects.

Before a public release, publish/review the shared tooling sources, pin known public SDK/dependency revisions, run hosted CI and the intended hardware gate, update README/changelog/release notes, and build the actual release candidates under their release version policy. A passing Debug processor run does not alone certify a separately rebuilt Release artifact.

## Validated behavior and remaining work

A complete KasaTapo workflow on MC4-R / Home 4.11.322 passed its then-current 57 local tests, 57 processor tests, three read-only live tests and seven installed-driver checks, then removed the test instance and released the lease without manual intervention. Later, all six expanded driver suites passed 400 distinct tests twice on that processor. That later coverage run is not a claim that each driver's full production-update/live workflow has been run.

An earlier cleanup stalled and required a separately approved manual reboot; the cause remains unknown. The automation retained the failed outcome. Reboot is never used as a generic timeout or cleanup-recovery fallback. See [workflow policy and evidence](ProcessorTestWorkflow.md) for the detailed history.

Remaining work includes native Test Explorer integration, reusable driver-specific installed-state/control contracts beyond the current read-only checks, broader firmware validation, verified cross-run artifact reuse, and any future validated rollback/control-restoration support.

## V1 development and reboot policy

Entity V2 drivers normally update without a processor reboot. V1 drivers such as a video-server driver can require one, so the workflow supports an explicitly authorized reboot path:

```json
{
  "allowProcessorReboot": true,
  "processorSystemName": "DEVELOPMENT",
  "stageTimeoutSeconds": 900
}
```

These are fields to add to a complete private plan, not a runnable plan by themselves. `allowProcessorReboot` defaults to false. `processorSystemName` is optional; when present, the workflow resolves that exact name before starting and again during reconnection, retaining the verified HTTPS/SSH pins. With only `host`, it reconnects to that configured address/name. Missing discovery results are retried within the deadline; duplicate system names are rejected. Required test-suite service addresses and dynamic ports are rediscovered before subsequent test runs.

For an update, the processor must report supported swap and an explicit reboot requirement. The update command stages the replacement. The workflow waits for the matching `swapDriverCompleted` event to confirm reboot is required and no device needs reconfiguration, then requests one Home configuration reboot. Generic operation success does not authorize that step. It saves target/version/mode evidence in `reboot-N.json` before submission and the confirmed swap event before requesting reboot, waits for the old authenticated event connection to close, authenticates a fresh connection, verifies its retained processor lease, and checks the intended driver identity/version is Loaded before proceeding. Lost responses do not replay the update. Failing to observe/recover from restart or verify the driver closes the gate and retains uncertain state for inspection.

Initial installation and removal have optional per-package settings: `rebootAfterInstall` and `rebootAfterRemoval`, both false by default. Set them only for a known driver/firmware sequence that requires an explicit reboot after successful commissioning or removal. They require the global `allowProcessorReboot` authorization. They do not relax identity or dependency-scope checks. The explicit Home configuration reboot is attempted once after the operation responds; if firmware has already closed the management connection, the workflow waits for recovery instead of sending a second reboot. A lost commissioning/removal response remains uncertain and does not trigger an inferred reboot. An unacknowledged configuration reboot stops the workflow without retrying it.

Authorization covers interruption of the entire Home processor, including unrelated running drivers. Reboots are allowed only between confirmed-stopped test stages. No reboot is triggered merely because a test or cleanup operation times out. Choose a stage timeout large enough for upload, update, reboot, discovery and driver startup; expiry never grants permission for another reboot.

The implementation has simulated regression coverage for authorization, lost update responses preventing reboot, no duplicate reboot, failed reconnection, changed driver identity, saved evidence and retained lease ownership. The complete Apple TV V1 update cycle has now passed on the development MC4-R; V1 initial-install/removal reboot paths remain unverified on hardware. Existing successful Entity V2 workflow evidence remains valid for the default reboot-free path.


The unattended CLI reboot was exercised on the development MC4-R. The first recovery attempt authenticated after about three minutes but stopped on a transient HTTP 500 from device inventory while Home initialized. The fix retries bounded startup read failures (HTTP 500/502/503/504), while authorization/request errors still stop recovery. Read-only verification was then resumed without another reboot: all 21 previously loaded driver instances had unchanged identity/version and were Loaded; the original lease was verified and released. No driver packages changed. The failure and resumed verification are retained separately. The later uninterrupted V1 update validation is described below.

A complete unattended Apple TV V1 workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal reboot paths still have simulated coverage only. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.
