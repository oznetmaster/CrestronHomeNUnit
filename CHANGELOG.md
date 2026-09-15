# Changelog

## 1.3.0 - 2026-09-15

Add optional automatic storage cleanup for successful CI test runs, while preserving manual deployments.

- `removeTestPackageAfterSuccessfulRun` removes only this workflow's uploaded test archive after all requested stages pass and its test instance is confirmed removed. It requires `removeTestInstanceAfterRun` and defaults to false.
- Capture pre-existing storage paths before upload; preserve those paths and every package still referenced by an installed model or alias. Verify exact package identity, version and SHA-256 against the retained build, and save a backup and operation evidence before deletion.
- Hold the shared processor reservation and execution marker throughout removal and catalogue refresh. Uncertain cleanup retains the reservation for inspection; no command is automatically replayed.
- Free storage and remove persisted catalogue references without rebooting. Home may retain a cached catalogue entry until its next planned reboot; workflow results report this explicitly.
- Retain original package filenames in separate processor/actual artifact folders, so deployments no longer create generic `processor.pkg` or `actual.pkg` names.
- Management requests use the configured workflow stage deadline, avoiding the shorter default HTTP timeout during slow activation.

Validation: 100 desktop workflow regressions passed. A complete MC4-R test-only run passed 47 local tests, 47 processor tests and three read-only live tests, then automatically removed its instance and hash-verified archive and released the reservation. All 14 pre-existing storage paths were protected. No actual driver was updated and no reboot was requested. An earlier activation timeout was reconciled separately and is not counted as a successful end-to-end run.

## 1.2.2 - 2026-09-15

Patch release fixing Debug version collisions in the CLI and Visual Studio Test Explorer workflow when a newer build has been deployed manually.

- Reconcile standard project manifests with the highest matching catalogue revision before building, under the shared processor lease. Preserve the source major/minor/patch version; the normal build script allocates the next Debug revision.
- Verify the built driver's identity and fresh version before upload, and check again for conflicting catalogue versions before deployment. Newer release versions, unreadable matching versions and exhausted counters require deliberate correction.
- Preserve manifest formatting, UTF-8 BOM and unrelated values. Custom manifest layouts continue to require explicit counter management.
- Include the reusable private hardware-CI bridge template and setup documentation published since 1.2.1. These cover exact-source GitHub App checks, approved source selection, release preflight and adding projects.

Validation: 73 desktop workflow regressions passed. A complete MC4-R test-only run started with source revision zero, reconciled catalogue baseline 1.1.1.5, built 1.1.1.6 and passed 69 local plus 69 processor tests. Its temporary test instance was removed and the processor lease released. No actual-driver update was attempted in this validation.

## Automatic hardware-check template - 2026-09-15 (no package release)

- Add a reusable private orchestration template with exact-source hosted-test gates, approved PR selection, GitHub App reporting and sequential processor execution.
- Cover independent library changes and collection package definitions separately, with original dependency pins retained for collection checks.
- Document one-time App provisioning, adding projects, required-check policy and release-generated source validation. Include offline policy tests and generic release-check scripts.

## Hardware CI documentation - 2026-09-15 (no package release)

- Record successful GitHub runner service executions through the published adapter, including temporary-instance cleanup and lease release.
- Document service account provisioning, pinned helper sources, short packaging paths, Debug revision persistence and target-specific test filters.


## 1.2.1 — 2026-09-15

- Preserve separate local TRX files for every target framework, combine their required outcomes and show every framework's results in Test Explorer. A passing target cannot hide another target's failure or overwrite its evidence.

- Allow workflow health checks to follow the actual driver's newly assigned instance ID, supporting initial installation without a pre-existing device ID.
- Add optional private initial-configuration input for unconfigured actual drivers. Snapshot inputs before processor access, validate advertised writable items, preserve configured instances, and withhold values from evidence. Configuration commands are never retried automatically.

## 1.2.0 — 2026-09-14

- Publish the stable `CrestronHomeNUnit.TestAdapter` NuGet package, including automatic workflow-manifest copying and a package-based sample. Consume released CrestronHomeDevTools 1.1.0; no adjacent source checkout is required.
- Verify clean package installation, offline discovery and failure without private settings in hosted CI and release builds. Publish the adapter through package-scoped NuGet Trusted Publishing; processor packages remain GitHub assets only.

- Add a .NET 10 VSTest adapter for running the gated processor workflow from Visual Studio Test Explorer. Discovery reads a public manifest offline; execution uses private settings and the shared workflow backend, reports individual local/processor/live/installed-driver results, and requests cooperative cancellation.
- Reject recursive workflow execution from a workflow's own local test stage.
- Add a sample workflow container and [Test Explorer setup guide](docs/VisualStudioTestExplorer.md).
- Validate the adapter through VSTest and a real MC4-R KasaTapo workflow: 148 individual tests passed, actual driver updated, test host removed and lease released.
- Document self-hosted GitHub Actions hardware testing for other developers, with a private-repository example and checkout-relative private plan wrapper.
- Share processor lease protection with standalone CLI test runs, the Windows runner, updated Home tile execution and DevTools mutations/build deployment. Add bounded workflow busy waits and explicit CLI reservation/release commands for manual Configure sessions; reject release during active tests.
- Validate desktop/tile exclusion on MC4-R with 69 processor and 35 tile lifecycle test passes, temporary-instance removal, and a build deployment that refused a held reservation then verified import without changing installed instances.

## 1.1.0 — 2026-09-14

- Use Home's configuration reboot operation after a confirmed V1 swap. An immediate SSH console reboot did not retain the staged driver version during validation; standalone SSH reboot remains a separate command.

- Fix V1 update sequencing: wait for the exact driver-swap completion event, validate reboot/reconfiguration requirements, and request reboot once. A missing event or lost update response never implies permission to reboot. Found during hardware validation.

### Development workflow

- Add opt-in processor reboot authorization, durable reboot evidence, fresh authenticated connections, discovery refresh and lease revalidation for V1 development. Never replay updates or use reboot as a timeout fallback. Add explicit per-package installation/removal reboot policies and simulated regression coverage; the Apple TV V1 update/reboot cycle has now passed unattended hardware validation; initial-install/removal reboot paths remain hardware-unverified.

- Document the complete CI development cycle, LAN agents, private configuration, retained artifacts, live-test gates, installation/update readiness and cleanup, with reusable example plans in [ContinuousIntegration.md](docs/ContinuousIntegration.md).

- Added the optional CLI workflow backend: local tests, immutable Debug packages, SFTP import, install/update readiness, processor/live gates, actual-driver update and read-only installed-device checks.
- Preserve per-stage evidence and retain uncertain executions, activations and requested cleanup under a processor workflow lease.
- Close the deployment gate if stage evidence cannot be saved.
- Validate a complete gated KasaTapo processor workflow, including automatic test-instance removal, without manual intervention.


- CLI readiness waits for a newly installed/restarted test service and rediscovers changed ports before connecting, without replaying test runs.

- Add a .NET 10 processor-test CLI and shared authentication client, with CI result files, explicit manual-suite selection and incomplete-run exit codes.

## [1.0.2] — 2026-09-12

### Fixed

- Share runner test inputs across suites in the same processor package, preserving the selection when switching between unit, read-only live and control tests. Keep other processors and packages isolated, migrate compatible saved selections, and preserve explicit clearing.

## [1.0.1] — 2026-09-12

### Fixed

- Refresh discovered package addresses and ports before connecting, and recover dropped connections after package restarts or redeployments.
- Update connected packages through Find packages without retaining a duplicate stale endpoint. Preserve previous results and never replay tests during reconnection.
- Prevent ILRepack from combining unrelated private resource helpers or compiler-generated anonymous types across dependencies, which could corrupt JSON payloads and error handling on the processor.

### Validation and documentation

- Add regression coverage for endpoint changes and recovery, including ambiguous/missing packages, retained results and explicit disconnect.
- Validate the merge corrections with 233 offline and six live OverkizClient tests passing on a processor.
- Update runner behavior and upgrade instructions; include this changelog and validation history in the documentation archive.

## [1.0.0] — 2026-09-11

- Initial Windows runner with mDNS package discovery, processor authentication, suite/test selection, private inputs, results and saved preferences.
- Self-contained .NET 10 Windows distribution and net472 NUnit Test Host with standalone Home tiles in the Utility category.
- Shared SDK for packaging NUnit test assemblies, Visual Studio build/deployment support, and documentation and third-party license notices.

[1.0.1]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.1
[1.0.0]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.0

[1.0.2]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.2