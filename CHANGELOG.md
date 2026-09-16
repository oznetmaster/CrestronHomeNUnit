# Changelog

## 1.8.1 - 2026-09-16

Fix room extension inspection when a named tile is below the initial viewport. The UI automation helper searches the observed service area with bounded scrolling and recognizes the compact room title that replaces the large heading after scrolling. Tile taps remain inside the visible area, clear of the toolbar and bottom navigation.

The search stops on an unchanged or repeated viewport, an ambiguous or disabled tile, an unexpected room title, or an uncertain gesture result. Scrolls are never retried after a lost response. A missing tile fails the inspection and still triggers observed Home restoration.

Validation: the complete Android regression suite passed against both source and an isolated adapter package. Package restore, workflow discovery and execution guards also passed. In the minimized Google emulator, a visible room extension was inspected successfully; a missing tile stopped the search and restored Home under both large and compact heading layouts. The checked device state and inventory were preserved and reservations released. Physical control tests and end-to-end submission acceptance remain separate work; these checks do not establish certification.

## 1.8.0 - 2026-09-16

Add room and nested-page inspection to the Crestron Home NUnit UI automation library included in the test adapter. Tests can open a named room extension, inspect nested extension pages and verify complete selection lists without choosing an option. Page names, navigation controls and expected values are supplied by each driver's test fixture. Controls are scoped to the front page even when the app retains background pages with identical resource IDs.

The navigation helpers restore the original Home screen after successful checks or assertion failures. Nested pages use explicitly supplied close/cancel controls. Unknown layouts stop navigation; uncertain taps, Back commands and scrolls are never replayed. Selection inspection handles clipped viewport-edge rows while preserving the strict coordinate checks used for input.

Validation: all 91 Android regression tests passed, including against a private packaged adapter. Read-only checks in the minimized Google emulator verified complete selection lists and their selected values for the exercised fixture. Editing was cancelled, Home and checked device settings were preserved, both reservations were released, and all 13 accepted capture pairs matched their retained hashes. Earlier controlled failures also restored Home and preserved checked state. This validates the helpers against an already-installed Debug driver; it is not full submission acceptance or certification.

## 1.7.1 - 2026-09-16

Fix saved-connection inspection on portrait Android screens where the local-port field is below the visible area. The navigator first verifies and records the Home name and local address, makes one guarded scroll, then verifies the port. It closes the editor and verifies return to Home even when inspection fails. An uncertain scroll is never repeated.

The setup guide now covers Visual Studio's Android SDK Manager, Windows acceleration, and installing the Crestron Home APK without a Google account. It also explains how to avoid conflicting ADB versions when BlueStacks and Google's emulator share a computer.

Validation: all 67 local Android tests passed. Both read-only sample driver gateway cases passed through a private packaged adapter candidate with Google's Pixel 7 Android emulator minimized. Eight screenshot/hierarchy pairs were verified; gateway state was unchanged, Home was restored and both reservations were released. These results cover a logged-in Windows session and an already-installed Debug driver, not service-session operation or complete submission acceptance.

## 1.7.0 - 2026-09-16

This is the **first release of Android UI testing support**. A development workflow can now run NUnit tests against the real Crestron Home Android app after its desktop tests, processor tests and gated driver update.

The new **Crestron Home NUnit UI automation library** is included in the `CrestronHomeNUnit.TestAdapter` package. Its test code runs on Windows and requires .NET 10; it communicates with the Android app through ADB. It provides Home and extension-page navigation, checks against visible controls, private screenshots and test results, and verified return to the starting Home screen. The processor and Android session are reserved together so cooperating workflows cannot overlap. Developers supply their own ADB installation, emulator and Crestron Home app.

This release also introduces an optional way to test an already-built Release driver package. The workflow verifies its package hash, driver identity, version and source commit, then uses those exact bytes without rebuilding them. Ordinary workflows continue to build Debug packages.

An existing Debug-version reconciliation problem is fixed: driver manifests named after the output package are now recognized alongside the project-name convention. Custom or ambiguous layouts can specify `manifestPath` explicitly.

Validation: the complete sample driver development workflow passed with the packaged UI automation library, including desktop and processor tests, live reads, the actual-driver update, installed health checks and both Android UI cases. Gateway and room state was preserved, Home was restored and reservations were released. Automated workflow, Android, adapter and isolated-package checks also passed; see [validation details](Validation.md).

Android hardware validation currently covers read-only gateway checks with minimized BlueStacks in a logged-in Windows session. Physical UI controls, service-session operation, exact Release-candidate hardware validation and the final Crestron submission workflow remain work in progress.

## 1.6.0 - 2026-09-16

Add explicit shared-driver reboot scope for removing temporary V1 instances. Existing plans retain their previous behavior.

- Allow `testPackage.additionalRemovalRebootDeviceIds` only with explicitly authorized removal reboots. The reported scope must exactly match the selected instance and reviewed additional IDs.
- Remove only the selected temporary instance. Require the additional instances to retain their identities, room assignments, versions, loading state and reported configuration after reboot.
- Use published DevTools 1.4.0 for the workflow and Test Explorer adapter.
- Document successful sample driver installed-room Auto/Manual/Auto restoration and the complete deliberately failing production-driver workflow with verified previous-code restoration. The original failed result remains failed.

Validation: plan validation, serialization and preservation guards have automated coverage. V1 driver initial installation and removal were verified on the development MC4-R with two configuration-aware reboots and existing instances preserved. Initial startup verification was resumed read-only after a timeout; no install or reboot command was repeated. This is hardware evidence with assisted verification, not a claim that that complete cycle ran unattended.

## 1.5.0 - 2026-09-15

Add an opt-in, guarded code rollback policy for completed failed installed-driver checks. Current configuration is preserved; saved settings and refresh tokens are never replayed.

- Require an exact existing driver target, known previous package and SHA-256, a driver-specific read-only configuration-compatibility verifier, and post-rollback health checks. Initial configuration and reboot workflows cannot enable rollback.
- Capture previous bytes and the complete driver/child identity scope before upgrade. Verify the upgraded version, unchanged identities and configuration compatibility before importing and again before swapping.
- Require a single eligible instance and an explicit reboot-free swap completion. Missing responses, extra targets, changed configuration, unknown execution, incomplete physical restoration and failed cleanup prevent automatic recovery. Commands are never automatically repeated.
- Keep the shared processor reservation and exclusive execution marker until the previous version, configuration identity, scope and health are all verified. Preserve the original failed workflow result even after successful restoration.

Validation: workflow tests cover stage selection, durable-intent failures, cancellation, execution-marker retention, compatibility/identity changes, eligibility drift, lost responses and required swap completion. The real backend passed on a temporary MC4-R Entity V2 host: prior-package/configuration capture, upgrade, older-code restoration, configuration and health verification, host/archive removal and lease release. No production driver or token was rolled back. The complete deliberately failing production-driver workflow was not exercised; each production driver requires its own reviewed verifier.

## 1.4.0 - 2026-09-15

Add optional installed-device control testing with independent physical observation and restoration, and explicit reuse of retained workflow packages.

- `deployedControls` captures a device's current state through a private read-only probe, issues an absolute command, verifies the driver's command-completion counter and independently observes the physical result. Cleanup restores and verifies the original state within a separate deadline.
- Require exact device/model/physical identity, root-driver ancestry and version. Uncertain command completion, overlap, restart or restoration retains the processor reservation for investigation. Boolean devices can use distinct absolute On/Off commands; arbitrary parameterized setters require driver-specific validation.
- Save private control intent and restoration evidence before mutation. New shared execution guards keep cooperating test and management tools from overlapping the control cycle.
- `artifactReuse` verifies a successful previous run, released lease, source identity, build inputs, driver identity and retained package SHA-256. Every required test stage runs again. An equal/newer catalogue version causes a fresh Debug build; corrupt evidence stops the run.
- Resolve DevTools 1.3.0 from NuGet. Official NUnit 4.6.1 remains unchanged.

Validation: 151 workflow regressions passed, including a retained-artifact path that succeeds with a deliberately unbuildable project. The sample outlet driver hardware workflow passed 71 local tests, 71 processor tests, three processor live tests and four installed checks, including physical outlet control and restoration. The temporary instance and archive were removed and the reservation released. Cross-processor reuse has not yet been hardware-validated. Automatic rollback is not enabled by this release.

## Offline release workflow support - 2026-09-15 (no binary release)

- Add an explicit manual hardware-check override with a required reason and exact-source workflow evidence, covering an unavailable processor or local GitHub runner.
- Independently require configured GitHub-hosted validation and preserve other release checks. No runner, host or NuGet version changes.

## Discovery-based source tooling - 2026-09-15 (no binary release)

- Generate package suites without duplicated expected-count fields by default, while preserving an explicitly requested positive count.
- Add reusable source-to-execution and source-to-package coverage checks, including multiple test assemblies, custom suite names, distinct live/control categories and duplicate parameter display names.
- These source tools preserve live-test exclusion and existing Windows/processor execution boundaries. No runner, host or NuGet version changes.
- Correct hardware-CI template cleanup requirements and document release preflight gates without requiring new branch rules.

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
- Validate the adapter through VSTest and a real MC4-R sample outlet driver workflow: 148 individual tests passed, actual driver updated, test host removed and lease released.
- Document self-hosted GitHub Actions hardware testing for other developers, with a private-repository example and checkout-relative private plan wrapper.
- Share processor lease protection with standalone CLI test runs, the Windows runner, updated Home tile execution and DevTools mutations/build deployment. Add bounded workflow busy waits and explicit CLI reservation/release commands for manual Configure sessions; reject release during active tests.
- Validate desktop/tile exclusion on MC4-R with 69 processor and 35 tile lifecycle test passes, temporary-instance removal, and a build deployment that refused a held reservation then verified import without changing installed instances.

## 1.1.0 — 2026-09-14

- Use Home's configuration reboot operation after a confirmed V1 swap. An immediate SSH console reboot did not retain the staged driver version during validation; standalone SSH reboot remains a separate command.

- Fix V1 update sequencing: wait for the exact driver-swap completion event, validate reboot/reconfiguration requirements, and request reboot once. A missing event or lost update response never implies permission to reboot. Found during hardware validation.

### Development workflow

- Add opt-in processor reboot authorization, durable reboot evidence, fresh authenticated connections, discovery refresh and lease revalidation for V1 development. Never replay updates or use reboot as a timeout fallback. Add explicit per-package installation/removal reboot policies and simulated regression coverage; the V1 driver update/reboot cycle has now passed unattended hardware validation; initial-install/removal reboot paths remain hardware-unverified.

- Document the complete CI development cycle, LAN agents, private configuration, retained artifacts, live-test gates, installation/update readiness and cleanup, with reusable example plans in [ContinuousIntegration.md](docs/ContinuousIntegration.md).

- Added the optional CLI workflow backend: local tests, immutable Debug packages, SFTP import, install/update readiness, processor/live gates, actual-driver update and read-only installed-device checks.
- Preserve per-stage evidence and retain uncertain executions, activations and requested cleanup under a processor workflow lease.
- Close the deployment gate if stage evidence cannot be saved.
- Validate a complete gated sample outlet driver processor workflow, including automatic test-instance removal, without manual intervention.

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
- Validate the merge corrections with 233 offline and six live a client library tests passing on a processor.
- Update runner behavior and upgrade instructions; include this changelog and validation history in the documentation archive.

## [1.0.0] — 2026-09-11

- Initial Windows runner with mDNS package discovery, processor authentication, suite/test selection, private inputs, results and saved preferences.
- Self-contained .NET 10 Windows distribution and net472 NUnit Test Host with standalone Home tiles in the Utility category.
- Shared SDK for packaging NUnit test assemblies, Visual Studio build/deployment support, and documentation and third-party license notices.

[1.0.1]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.1
[1.0.0]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.0

[1.0.2]: https://github.com/oznetmaster/CrestronHomeNUnit/releases/tag/v1.0.2
