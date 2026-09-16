# Automated development and processor testing

**Processor requirement:** Automated deployment and configuration through DevTools require a **V2 Crestron Home processor**. V1 processors do not support these management commands. This is separate from V1 drivers: a V1 driver hosted on a supported V2 processor uses the explicit reboot workflow described below.

This guide explains the implemented CLI development workflow and how it connects local NUnit tests, CrestronHomeDevTools and processor test packages. It is the central integration guide for driver and library repositories.

**Current status:** the CLI implements the gated workflow and restores CrestronHomeDevTools from NuGet when built from source. Complete KasaTapo, Overkiz, Tesla, WeatherLink, Wiser and explicitly opted-in Apple TV V1 update/reboot workflows have passed unattended hardware validation. V1 initial-install/removal reboot paths have simulated coverage only. The stable .NET 10 Test Explorer adapter is available on NuGet as CrestronHomeNUnit.TestAdapter; see [its setup and validation](VisualStudioTestExplorer.md).

Post-deployment checks of the installed production driver currently read properties and validate expected values or ranges. Processor live-test fixtures can operate devices and implement their own state capture and restoration. From tooling 1.4.0, workflows also support optional [installed-device control checks with independent observation and state restoration](InstalledDriverControls.md). Optional [code rollback](DriverRollback.md) requires a known prior package and a driver-specific current-configuration compatibility verifier.

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
| `CrestronHomeNUnit.TestAdapter` | Native VSTest workflow entry, cooperative cancellation and individual result reporting in a .NET 10 test container. |
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

For a reusable GitHub Actions setup on your own computer, follow [GitHub hardware CI](GitHubHardwareCI.md), including the Windows agent, private plans, source-revision binding and workflow template.

Use a machine with .NET 10, the net472 targeting/build requirements of the driver projects, their packaging tools, and LAN reachability to the development processor. A Windows agent is the validated build environment. Processor discovery, HTTPS/WebSocket management, SFTP, mDNS and the selected dynamic test port must be reachable.

GitHub Actions can include these processor runs using a Windows self-hosted runner on that LAN. The Windows machine executes the CI job; the processor is its hardware target. Labels select the appropriate agent, and its private plan selects the processor. No inbound WAN exposure of the processor is necessary. The runner must be online for the job to execute. See [GitHub's self-hosted runner configuration](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/add-runners) and [runner labels](https://docs.github.com/en/actions/how-tos/manage-runners/self-hosted-runners/apply-labels).

For public libraries and drivers, keep ordinary pull-request checks on GitHub-hosted runners. A separate private hardware-orchestration repository is the preferred home for a LAN runner: run reviewed, immutable source revisions and retain private plans, credentials and device logs locally. GitHub recommends private repositories for self-hosted runners because pull-request code can compromise the runner and its reachable devices. Environment approval is useful for the hardware job but is not isolation from arbitrary code. Never automatically run unreviewed fork code with processor credentials. Only sanitized test counts/status should be copied to public checks; raw live-test outputs can identify devices or contain configuration data.

Hardware checks can gate a subsequent release or production update. Tie the check to the exact tested source revision and retained package hash, and distinguish a successful Debug workflow from certification of separately rebuilt Release bytes. Do not configure a required hardware check until the agent and its scheduling are ready; an offline agent leaves that check pending.

Use the self-contained CLI release, or check out `CrestronHomeNUnit` and the relevant driver/library projects. Set supported project/SDK path overrides privately. To build the CLI:

```powershell
dotnet build CrestronHomeNUnit.Cli/CrestronHomeNUnit.Cli.csproj -c Release -p:EnableProcessorWorkflow=true
```

`DevToolsProject` optionally selects a local library project for joint development. Normal builds restore CrestronHomeDevTools 1.1.0 from NuGet and include workflow support. The additional `CrestronHomeNUnit.Workflow.Tests` project tests the backend.

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
| `deployedChecks` | Named read-only checks against exact installed device IDs/models/properties; expected JSON value or numeric bounds. Set `useActualDriver: true` and `deviceId: 0` to use the actual driver's verified instance ID. |
| `removeTestInstanceAfterRun` | Explicit choice to remove the test host after tests and evidence preservation. |
| `removeTestPackageAfterSuccessfulRun` | Opt-in CI storage cleanup after a completely successful run. Requires instance removal; false by default to retain manual deployments. |
| `stageTimeoutSeconds` | Bounded stage wait, 600 by default. |

Every listed stage is required. Replace example `minimumPassed` values with meaningful counts for the suite; a single passing selected test must not unlock a gate requiring a full suite. Keep ordinary filters separate from manual/Explicit fixtures. `actualDriver` requires both `liveSuites` and `deployedChecks`, and its identity/path must differ from the test target. Read-only checks can target fixed children only when their parent chain reaches the updated actual driver; child IDs must be configured before the run. Root-driver checks can instead use `useActualDriver: true` with `deviceId: 0`; model and ownership checks still apply.

From version 1.2.1, `actualDriver.initialConfigurationFile` optionally names an absolute private JSON path containing configuration item IDs mapped to string values. The workflow snapshots it before connecting and applies it after verified activation only if the actual driver reports it is unconfigured. Existing configured drivers retain their settings. Only advertised writable items are submitted, once, using the driver's apply-configuration command. A rejected or uncertain response stops the workflow for inspection; For drivers requiring a wizard, supply an ordered `steps` array as shown below; the workflow checks each advertised step ID and writable item, then requires the wizard to finish after the last planned step. It never guesses answers to additional steps. Values and raw configuration errors are omitted from evidence. Keep this file outside source control, packages and uploaded CI artifacts, just like live-test inputs. The settings are sent only to the pinned processor. Follow application with checks for configured, online and ready state; command acknowledgement alone does not prove the external service connected.

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

Installed-driver checks read properties and validate expected values/ranges by default. From tooling 1.4.0, workflows also support opt-in `deployedControls`: explicit absolute commands, independent physical observations and verified restoration, all under the processor reservation. See [the control contract and private plan](InstalledDriverControls.md). KasaTapo outlet On/Off control has passed hardware validation; other command routes require their own validation. Devices without readable state require an explicit alternative test design; never assume an initial value.

## Readiness and version identity

Upload, catalogue import, update eligibility, Loaded state and test-listener availability are separate milestones. Management request timeouts follow `stageTimeoutSeconds`, as do the bounded workflow stages. The implementation waits for them, rather than assuming that completing SFTP makes the driver immediately updatable. Test connection attempts rediscover changing ports. Once a test request is submitted, it is not automatically replayed after a disconnect.

Numeric version comparisons preserve all four processor components. Zero-padding can differ; `2.0.000.0005` equals `2.0.0.5`, not `2.0.0.6`. Three-part public release tags do not replace the exact local Debug identity.

The workflow hashes tracked/non-ignored source files and records the package SHA-256. It retains the tested package bytes and refuses subsequent source changes; it does not rebuild the production package after passing the gate. Generated manifest dates and fourth-component Debug increments are normalized in source identity. Local secrets must already be excluded or outside the source roots.

For standard projects whose manifest has the same basename as the project and sits beside it, the workflow now reconciles the fourth-component Debug counter with the highest same-model catalogue version while holding its processor lease. The manifest must belong to a declared source root. The normal project build still increments that counter, and the workflow verifies the resulting driver identity, release components and newer revision before upload. Manifest formatting, UTF-8 BOM and unrelated fields are preserved. A newer release already on the processor, an unreadable matching version or an exhausted revision stops the build for deliberate correction.

Custom manifest layouts retain the existing collision checks and must manage their Debug counter explicitly. Keep private CI counters when using disposable checkouts: they preserve allocated revisions after catalogue packages are removed. The workflow rechecks for equal or newer matching catalogue versions immediately before deployment; a conflicting manual deployment stops that run.

Hardware validation deliberately reset a standard Kasa test-package source revision to zero with catalogue version 1.1.1.5 present. The workflow built 1.1.1.6, passed 69 local and 69 processor tests, removed the temporary test instance and released its lease. No actual-driver update was attempted. The version logic also passed 73 desktop workflow regressions.

The workflow requires a version newer than matching catalogue entries. From tooling 1.4.0, optional [artifact reuse](ArtifactReuse.md) can supply retained package bytes from a successful prior run after verifying source, build inputs and package hashes. All required tests run again. If the target catalogue already contains that version or a newer one, the workflow builds a fresh Debug revision instead; matching version text alone never establishes matching tested bytes.

## Results and deployment gates

| Evidence | Purpose |
|---|---|
| `Workflow.json`, `Stages.json` | Overall/stage outcome and actual-driver update status. |
| `BuildIdentity.json` | Source digest and build context. |
| Retained package/hash metadata and activation receipts | Exact bytes, catalogue availability and loaded-instance identity. |
| Local TRX / processor NUnit XML | Actual executed tests and outcomes; local multi-target projects retain a distinct TRX file for every target framework. Set each local project's minimum count for the combined required runs. |
| Progress and summaries | What ran before a connection loss or incomplete stage. |
| `InstalledDriver.xml` | Individual installed-device property checks and optional control/restoration outcomes. |
| `Lease.json` | Run owner and lease release/retention state. |

Failures, skipped required tests, insufficient counts, cancellation, timeout, incomplete execution or inability to preserve stage evidence close the actual-driver deployment gate. A post-deployment check can fail after the driver is already updated; the result records that distinction. By default the backend does not roll back or reboot to recover. An explicit [rollback policy](DriverRollback.md) can restore previous code after a completed failed installed-driver check, with current configuration preserved and all recovery guards satisfied.

## Cleanup and interrupted runs

`removeTestInstanceAfterRun` removes only the recorded test-instance ID/model/version after results are saved and all stages using it have finished. It also runs after completed test failures. It does not remove the actual driver or the catalogue package.

If execution, activation or requested removal is uncertain, retain the test instance and processor lease for investigation. A missing tile or cleared room assignment is not enough. The lease at `/user/CrestronHomeNUnit-WorkflowLease` coordinates cooperating jobs across machines. Current source also coordinates the Windows runner, standalone CLI, updated Home tiles and DevTools/build mutations; older releases and manual Configure/SFTP activity can bypass it. It has no automatic expiry or lock stealing. Inspect the owned lease and remote state before clearing a stale lease manually. Do not replace an uncertain host with another one just to make the next run start. See [hardware CI coordination](GitHubHardwareCI.md) for upgrade requirements and manual reservations.

From tooling 1.3.0, `removeTestPackageAfterSuccessfulRun: true` additionally removes this run's stored test archive, only after every required stage passes and instance removal is confirmed. Failed runs retain their archives for investigation. Manual deployments remain retained by default.

The workflow captures pre-existing paths before upload and never deletes those paths. It checks the exact package GUID/version, every supported model/alias, installed references and the SHA-256 of the retained build. It backs up the archive and catalogue manifest before deletion. A shared execution marker prevents tests from starting during cleanup; interrupted or uncertain cleanup retains the processor reservation for inspection. Recovery evidence is in the private results folder under `package-cleanup` and must not be published.

Deleting the archive and refreshing the catalogue frees storage and removes the persisted catalogue reference. Home can still list a cached entry until its next planned reboot. This is reported in `Workflow.json` and `package-cleanup/verified.json`; cleanup does not initiate a reboot. The standalone DevTools `stored-packages` command remains read-only.

Optional [artifact reuse](ArtifactReuse.md) verifies prior bytes and build inputs while rerunning all required tests.

Retained builds keep their original `.pkg` filename beneath `packages/processor` or `packages/actual`. These separate folders avoid filename collisions without renaming the uploaded archive to `processor.pkg` or `actual.pkg`.

Save live SSH logs when diagnosing an active problem; processor logs written to disk can lag. Log streaming is currently an external diagnostic aid, not a DevTools CLI command or automatic workflow evidence feature.

## Visual Studio integration

The existing NUnit adapter runs local projects in Test Explorer. Visual Studio can invoke the CLI through a terminal/external tool or a deliberately configured task now. That does not make remote stages appear as native Test Explorer cases.

The source tree now includes a dedicated .NET 10 workflow adapter and sample container. It exposes one complete workflow in Test Explorer and imports individual Local, Processor, Processor Live and Installed Driver outcomes as child results. Discovery reads only a public manifest and remains non-mutating. Execution uses the same backend as the CLI. See [setup, selection and cancellation](VisualStudioTestExplorer.md). The adapter is published with release 1.2.0; its hardware and test-platform validation are recorded separately.

## Publication boundaries

Development workflow execution is not a public release workflow. Publish production driver changes when runtime fixes or production dependency changes justify a release; test-only source updates do not require driver releases. Processor test packages can have separate GitHub release assets and are never NuGet packages. Independent library repositories remain free of Crestron packaging projects.

Before a public release, publish/review the shared tooling sources, pin known public SDK/dependency revisions, run hosted CI and the intended hardware gate, update README/changelog/release notes, and build the actual release candidates under their release version policy. A passing Debug processor run does not alone certify a separately rebuilt Release artifact.

## Validated behavior and remaining work

The expanded suites passed on Windows in Debug and Release. On MC4-R / Crestron Home 4.11.322, all 400 distinct offline and SDK lifecycle tests passed twice in the same test-host process: 800 processor passes.

| Driver | Distinct tests | Processor suite runs | Complete production-update workflow |
| --- | ---: | --- | --- |
| KasaTapo | 69 | Passed twice | Passed through VSTest: 69 local tests, 69 processor tests, 3 live checks, 7 installed-driver checks, test-host removal and lease release |
| Overkiz | 47 | Passed twice | Passed: 47 local tests, 47 processor tests, 3 live checks and 3 installed-driver checks; lease released |
| Tesla Powerwall | 73 | Passed twice | Passed with independent Owner and Fleet sessions: 73 local tests, 73 processor tests, 3 live checks and 3 installed-driver checks; automatic Fleet region discovery and lease release verified |
| WeatherLink Live | 56 | Passed twice | Passed: 56 local tests, 56 processor tests, 3 live checks and 3 installed-driver checks; lease released |
| Wiser Heat | 39 | Passed twice | Passed initial installation: 39 local tests, 39 processor tests, 3 live checks, private configuration wizard and 3 installed-driver checks; lease released |
| Apple TV, including extension lifecycle | 116 | Passed twice | Passed: 116 local tests, 105 processor unit tests, 11 processor lifecycle tests, V1 update/reboot, 3 installed-driver health checks, test-host removal and lease release |

The complete workflow column covers deploying/updating the actual production driver and checking that installed instance. Processor suites exercise the driver code inside a separate test package. Passing those suites does not establish that the complete production-update workflow has run for that driver. The table records the earlier read-only validation runs. A subsequent KasaTapo workflow passed 71 local tests, 71 processor tests, three processor live tests and four installed checks, including independently observed outlet control and restoration. It removed its temporary test instance and archive and released the reservation. See [installed-driver controls](InstalledDriverControls.md) for the verified command pair and limits.

CP4-R / Home 4.11.322 validation additionally covered a temporary Entity V2 Apple TV test host: installation, update, current-instance reuse, targeted reload, authorized reboot, reconnection, lease verification and removal. Its 116 tests passed before and after reboot. All seven pre-existing driver instances retained their identities, room assignments and versions and were Loaded. This was test-host lifecycle validation; no existing production driver was updated on the CP4-R.

Earlier failed cleanup/reboot attempts remain recorded separately from the later successful runs. An unresponsive cleanup once required an explicitly authorized manual reboot; the cause remains unknown. Reboot is never a generic timeout or cleanup-recovery fallback. See [workflow policy and evidence](ProcessorTestWorkflow.md) and [DevTools compatibility evidence](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/Compatibility.md).

Remaining work:

- Extend driver-specific control probes beyond the verified KasaTapo outlet and Wiser room Auto/Manual/Auto routes. The installed Overkiz one-way blind does not provide independently readable position, WeatherLink station data is read-only, and other routes need their own observation/restoration design. Processor fixtures retain their own cleanup where applicable.
- Expand V1 lifecycle evidence beyond the verified Apple TV initial-install/removal path and reviewed shared-instance scope. The initial-install startup verification needed a read-only resume; no commissioning or reboot was repeated.
- Validate cross-processor deployment of a reused artifact on hardware. The opt-in [artifact reuse backend](ArtifactReuse.md) has automated identity, corruption, dependency-change and build-bypass coverage; same-processor catalogue conflicts deliberately trigger a fresh Debug build.
- Extend production rollback verifiers beyond Wiser. A full deliberately failed Wiser post-update check restored the reviewed previous code and preserved current configuration and the installed room tile. Each other driver needs its own compatibility review; V1 and initial-install rollback remain unsupported.
- Extend the processor/firmware compatibility matrix beyond the two tested models and firmware version.

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

Initial installation and removal have optional per-package settings: `rebootAfterInstall` and `rebootAfterRemoval`, both false by default. Set them only for a known driver/firmware sequence that requires an explicit reboot after successful commissioning or removal. They require the global `allowProcessorReboot` authorization. From tooling 1.6.0, `additionalRemovalRebootDeviceIds` can explicitly list other existing instances in a reviewed shared V1 removal scope. It requires `rebootAfterRemoval` and the global reboot authorization. The reported scope must match the selected target plus that list exactly; only the target is removed, and the other identities, rooms, versions and reported configuration must survive restart. An empty list retains the single-instance restriction. The explicit Home configuration reboot is attempted once after the operation responds; if firmware has already closed the management connection, the workflow waits for recovery instead of sending a second reboot. A lost commissioning/removal response remains uncertain and does not trigger an inferred reboot. An unacknowledged configuration reboot stops the workflow without retrying it.

Authorization covers interruption of the entire Home processor, including unrelated running drivers. Reboots are allowed only between confirmed-stopped test stages. No reboot is triggered merely because a test or cleanup operation times out. Choose a stage timeout large enough for upload, update, reboot, discovery and driver startup; expiry never grants permission for another reboot.

The implementation has simulated regression coverage for authorization, lost update responses preventing reboot, no duplicate reboot, failed reconnection, changed driver identity, saved evidence and retained lease ownership. The complete Apple TV V1 update cycle has now passed on the development MC4-R; Apple TV V1 initial installation and removal have also been verified on MC4-R, including preservation of the existing shared-code instance; initial startup verification was resumed after a timeout. Existing successful Entity V2 workflow evidence remains valid for the default reboot-free path.


The unattended CLI reboot was exercised on the development MC4-R. The first recovery attempt authenticated after about three minutes but stopped on a transient HTTP 500 from device inventory while Home initialized. The fix retries bounded startup read failures (HTTP 500/502/503/504), while authorization/request errors still stop recovery. Read-only verification was then resumed without another reboot: all 21 previously loaded driver instances had unchanged identity/version and were Loaded; the original lease was verified and released. No driver packages changed. The failure and resumed verification are retained separately. The later uninterrupted V1 update validation is described below.

A complete unattended Apple TV V1 workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. The separate initial-install/removal validation used the existing catalogue, two Home configuration reboots and explicit shared-scope preservation. A read-only verification resume was required after initial startup; the temporary instance was removed and all original device identities, rooms and versions were preserved. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.

An initial configuration wizard file has this form (example values only):

```json
{
  "steps": [
    { "id": "Connection", "values": { "_Host_": "192.0.2.10", "HubSecret": "REPLACE_LOCALLY" } },
    { "id": "HeatSettings", "values": { "TemperatureUnits": "Celsius", "BoostDelta": "2", "BoostDurationMinutes": "60", "EnableWholeHouseHotWater": "false", "AllowAwayMode": "false" } }
  ]
}
```

Use IDs advertised by your driver. Supply each required choice explicitly, even when the UI displays a default; omitting a field does not guarantee that the driver accepts its default. A flat object such as `{ "SettingId": "value" }` uses the apply-all command and requires an already advertised settings list. Wizard validation errors, repeated steps, unexpected steps and non-writable items stop the workflow. Partial configuration may remain after a failure; inspect the retained lease and exact instance before recovery. Initial configuration and checks using newly assigned instance IDs are supported from version 1.2.1.