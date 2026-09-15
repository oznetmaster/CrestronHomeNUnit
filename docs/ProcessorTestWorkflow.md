# Visual Studio and CI processor-test workflow

For the complete local-to-processor development cycle, see the [continuous integration guide](ContinuousIntegration.md), including private settings, gated actual-driver deployment, evidence and optional test-instance removal.

This document records the agreed workflow. The shared execution-order/deployment-gate policy is implemented and unit-tested; the standalone NUnit CLI and configuration-management primitives have been validated on a processor. The CLI includes a build/deploy/test backend using the published CrestronHomeDevTools NuGet dependency. The Visual Studio Test Explorer adapter is not implemented yet.

## Hardware validation

On September 13, 2026, a complete KasaTapo workflow passed 57 local tests, 57 processor unit/lifecycle tests, three read-only processor live tests and seven checks of the updated installed driver. The run built and installed its test package, updated the actual driver only after the required test gates passed, automatically removed the test instance and released its processor lease. Independent API and SSH host-manager checks confirmed removal. This run required no reboot or manual intervention.

An earlier run passed its tests but could not complete cleanup while Home configuration processing was stalled. Its result remains failed; recovery required a separately approved processor reboot. The underlying cause has not been established. A missing tile alone does not prove removal, and one successful repeat run does not establish that the earlier fault cannot recur. The workflow retains its lease after unconfirmed cleanup and never reboots as an unrequested cleanup fallback. V1 lifecycle reboots require the explicit policy described in the [CI guide](ContinuousIntegration.md#v1-development-and-reboot-policy).

Private run plans, logs, device bindings and deployment credentials are kept outside this repository. The Visual Studio adapter remains future work.

## One build, separate results

A run identifies the source revision and working-tree changes, builds the library/driver and its processor-test package from the same source, and records package hashes and versions. Rebuilding after testing invalidates deployment eligibility. Credentials, processor selection, device bindings and deployment paths belong in private local configuration or CI secrets, never committed examples.

Test Explorer should distinguish Local, Processor, Processor Live and Deployed Driver Live results. The CLI must use the same orchestration and result rules. Deployment/update happens once per package per run, with exclusive access to its processor throughout the run. Test discovery must not deploy or operate physical devices.

## Run sequence

1. Run the required local tests.
2. Upload the processor-test package and import it. Install the intended instance if absent, update it if older, or verify and reuse it if current. Wait for catalogue availability, update eligibility and the expected Loaded version, then rediscover and connect to its dynamic test port.
3. Run processor unit and lifecycle tests.
4. Run processor live fixtures required by the private project configuration. Use explicitly bound devices and supplied private input files.
5. For a driver project with deployment enabled, require all preceding mandatory stages to pass, then deploy and update the actual driver package built from the same source. Verify its installed/running version before continuing. Library projects stop after testing.
6. Run required smoke/live checks against the actual installed driver: loading, connection, entities, reported state and, when configured, device control.
7. Show and save all stage results, including restoration failures and the final installed driver version.

A suite counts as passed only when the required tests actually execute successfully. Missing, skipped, zero-test, timed-out, cancelled or incomplete required stages cannot unlock driver deployment. Optional live fixtures remain opt-in and must not silently become mandatory. An individual Run Selection result cannot masquerade as completion of the full required gate.

## Live state and deployment failures

The processor test host and the installed driver are different targets. Tests against a synthetic driver instance do not prove that the installed Home instance works. Post-deployment tests must address the intended installed instance, rather than construct another copy inside the test host.

Read-only post-deployment checks run first. Control tests capture initial state and restore it in cleanup where devices support reliable state queries and restoration. A restoration failure fails the run even if assertions passed. Devices without readable/restorable state need an explicit project-specific plan; do not invent an original state. Prevent overlapping tests or active drivers from issuing conflicting commands to the same live device.

A post-deployment failure occurs after the new driver is already installed and must be labelled accordingly. Retain the prior package and configuration evidence for recovery. Automatic rollback is allowed only after it has been validated for that driver and can restore the intended configuration without affecting unrelated instances. Reboot-required updates must follow an explicit project policy and are not silently performed.

## Configuration and component boundaries

- CrestronHomeDevTools owns discovery, authentication, SFTP upload, catalogue import, update/reload and installed-device management.
- CrestronHomeNUnit.Client/Transport own remote test discovery, private inputs, execution, cancellation and result transport.
- The orchestration layer owns ordering, exclusive processor access, build/package identity, mandatory-suite policy and deployment gating.
- A Visual Studio adapter maps that workflow and its individual results into Test Explorer; the CLI provides the same behavior for unattended CI.

The first hardware target is the designated development processor. Actual-driver deployment requires a private opt-in setting with the exact processor and installed driver target. No public configuration includes real credentials, device addresses or personal paths.


## Optional test-instance cleanup

Private workflow configuration provides an opt-in `removeTestInstanceAfterRun` setting. Cleanup runs after results are preserved and every requested test stage that needs the host has finished, including deployed-driver live checks. Remove only the explicitly identified test instance; never infer it from a display name alone and never remove the actual driver under development. Match its recorded device ID, model and version and refuse a dependency scope that includes other devices.

Cleanup also runs after completed test failures. A hung or disconnected run first requires confirmation that execution has stopped. Preserve test outcomes and report cleanup errors separately. A cleanup failure does not convert failed tests into a pass. The shared workflow policy implements and tests these cleanup decisions. The lower-level configuration `remove` command was validated by removing a completed test instance and automatically reinstalling it. The CLI wires these decisions to the verified configuration-management lifecycle APIs. The stable Test Explorer adapter uses this same backend; see [Test Explorer integration](VisualStudioTestExplorer.md).


## Version identity

Compare every numeric component of the exact built `.pkg` version with catalogue and installed state. Zero-padding may differ, but the Debug build component must not be ignored. Three-part GitHub/NuGet release versions are a separate concept and may differ from later local Debug builds. Record the artifact hash and retain the tested bytes across deployment. Uploaded does not mean activated, and matching version text alone cannot prove two separately rebuilt files have identical contents.

The CLI now waits for discovery and connection readiness (bounded by `--wait-ready` and the overall timeout), and rediscovers after a connection-establishment failure. Authentication failures and processor-identity changes stop immediately. Once a test request has been submitted, it is never automatically replayed.

## Preview CLI backend

Build `CrestronHomeNUnit.Cli` normally; it restores CrestronHomeDevTools 1.0.0 from NuGet. For joint source development, explicitly set `DevToolsProject` to that library project. `EnableProcessorWorkflow=false` excludes workflow support; the official CLI release includes it. Run the additional `CrestronHomeNUnit.Workflow.Tests` project when working on this backend.

```powershell
dotnet run --project CrestronHomeNUnit.Cli -- workflow --plan C:\Private\workflow.json --settings C:\Private\processor.json --results C:\Private\Results\new-run
```

The plan uses the public `WorkflowPlan` model. Required fields are `host`, verified `certificateSha256` and `sshFingerprint`, `sourceRoots`, `localTests`, `testPackage`, and `processorSuites`. Driver plans also specify `actualDriver`, required `liveSuites`, and read-only `deployedChecks`. Every suite needs a positive `minimumPassed`. Package targets specify absolute project/package paths, instance name, room ID and optional expected installed device ID. Test data paths are private `inputs` on their live suite. Processor credentials come from `CRESTRON_HOME_USER`/`CRESTRON_HOME_PASSWORD`, or `userName`/`password` in the separate private settings file. Never commit a real plan, settings, results or private inputs.

The workflow builds Debug packages with deployment disabled, copies them to the new result directory, and holds the copies open against modification. It hashes tracked and non-ignored source files before testing and checks that identity throughout. Only generated manifest dates and fourth-component Debug counters may change. A source change closes the gate. The actual package is built before the processor tests and the retained bytes are later deployed; there is no rebuild after passing tests.

For the first version, the workflow requires a newly built package version absent from the catalogue. This avoids mistaking older bytes with reused version text for the tested artifact. The underlying lifecycle API supports verifying/reusing current instances; allowing cross-run package reuse in the workflow itself awaits durable verified artifact provenance. Existing older instances update; missing instances install automatically.

`Workflow.json` records the outcome and whether an actual-driver update was attempted and verified. Per-stage directories contain TRX or NUnit XML, progress and summaries. Package metadata/hash files and activation receipts distinguish upload, catalogue availability and the loaded driver. Build logs and test output may contain local details: results stay private unless deliberately reviewed and shared.

Read-only deployed checks bind exact Home device IDs/models, verify their parent chain reaches the updated actual driver, and require an expected JSON value or numeric range. Their individual NUnit results are in `InstalledDriver.xml`. These checks do not issue device-control commands. The current backend does not implement control/restoration or automatic rollback.

An atomic SFTP directory `/user/CrestronHomeNUnit-WorkflowLease` coordinates cooperating workflows across computers. An interrupted or uncertain activation/test keeps this lease and the host for inspection. There is no automatic expiry or lock stealing. `Lease.json` records the run's owner ID and release status; after independently confirming all remote activity has stopped, remove only that owned file and empty lease directory. The lease does not prevent manual actions in Configure, Visual Studio or the Windows runner. Do not build or modify the same checkout concurrently with a workflow.

An unconfirmed test-instance removal retains the processor workflow lease. A cleared room assignment alone does not prove that Home has removed the instance. Inspect the retained instance and lease before another run; do not automatically remove the lease or create a replacement test host.

## Apple TV V1 hardware validation

A complete unattended Apple TV V1 workflow subsequently passed on the development MC4-R: 116 local tests, 105 processor driver tests, 11 processor SDK lifecycle tests and three read-only installed-driver health checks. It installed a fresh Entity V2 test host, staged the V1 update, received the matching swap-completion event, requested one Home configuration reboot, reconnected and verified lease ownership, verified the new driver version was Loaded, online, ready and configured, then removed the test host and released the lease. Independent checks confirmed the other 19 driver instances retained their identities and versions and were Loaded. The first V1 attempt exposed an incorrect assumption that swap initiates reboot; it required one separately recorded assisted reboot and was not counted as an unattended pass. A second attempt confirmed swap completion but an immediate SSH reboot returned with the previous version; it was stopped, reconciled and retained as a failed validation. The passing run used Home configuration reboot instead. V1 initial-install/removal reboot paths still have simulated coverage only. The SDK lifecycle tests and read-only health checks do not establish playback or device-control behavior.

Initial-installation configuration and health checks using the assigned actual-driver ID are documented in the [CI plan guide](ContinuousIntegration.md). These additions are available from version 1.2.1.


## Successful CI package cleanup

With tooling 1.3.0, set both `removeTestInstanceAfterRun` and `removeTestPackageAfterSuccessfulRun` to true in the private CI plan. The latter defaults to false for retained manual deployments. Only a successfully tested, uninstalled archive introduced by that run is eligible; pre-existing paths are protected and stored bytes must match the retained build. Storage is reclaimed immediately; a cached catalogue entry can remain until the next planned reboot. No cleanup reboot is automatic. See [cleanup guarantees and recovery evidence](ContinuousIntegration.md#cleanup-and-interrupted-runs).
