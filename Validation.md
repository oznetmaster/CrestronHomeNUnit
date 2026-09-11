# Desktop validation

Validated 10 September 2026 on Windows .NET Framework and on the Crestron Home processor. Processor run 0.1.000.0017 completed the selected framework self-test batch with 2,407 passed, zero failed, 55 skipped, and four timing warnings in 45.57 seconds. All 34 compatibility tests passed on processor run 0.1.000.0013.

- Processor projects use `net472` with `LangVersion=latest` and .NET SDK 10.0.401. The Windows runner moved to .NET 10 in the validation recorded below.
- Original NUnit helper static-extension calls compile successfully without rewriting them.
- Build, merge, namespace/metadata patch and Crestron ManifestUtil package construction succeed.
- Tests run through the NUnit framework API against the DLL extracted from the actual `.pkg`.
- NUnit framework self-test batch: **2,406 passed, 60 skipped, 0 failed** out of 2,466 cases.
- Compatibility suite: **34 passed, 0 skipped, 0 failed**.
- Packaged test discovery succeeds.
- Package contains no local `System.*` type definitions, no external NUnit assembly references, and no NUnitLite AutoRun type.
- Root EditorConfig is byte-for-byte identical to the Apple TV driver project's EditorConfig. Whitespace formatting verification passes.

The 60 self-test skips comprise 55 tests requiring UNIX, two existing upstream ignores, and three explicitly marked demonstrations. They are retained for reporting; the UNIX tests can become runnable on the processor. The first suite covers the imported assertion, constraint and syntax tests, not the full NUnit repository.

The tests exposed two packaging/runtime details addressed by the post-merge script: NUnit's facade-qualified AsyncStateMachineAttribute lookup and ILRepack's loss of a custom-named indexer overload's property metadata. The latter also reproduces independently with ILRepack 2.0.45 and 2.0.48; the reproduction and report draft are separate deliverables.

Run `Verify.ps1` to repeat the package checks and capture fresh XML/text evidence. Processor run 0.1.000.0015 reproduced the same two failures. Diagnostics showed that temporary files under `/temp/` were readable (65,600 bytes) while file existence and attribute checks reported them missing. The self-test helper now creates its generated files in the suite work directory inside driver data, with assertions unchanged. Processor run 0.1.000.0017 verified this adaptation: both previously failing tests passed, with existence, attribute, read, and enumeration checks agreeing. The four remaining warnings are upstream performance checks; no assertions failed.

## TCP development checks

The new transport and Windows runner build with Visual Studio MSBuild. Local socket checks pass for fragmented/adjacent frames, Unicode, invalid frame lengths, truncated messages, rejected pairing keys, discovery, selected test execution, live events and output, failed-test XML, no stale results after an empty selection, rejection of overlapping Home/TCP operations, cooperative cancellation, subsequent runs, disconnect cleanup and reconnect. The Windows form constructs successfully. Packaged Windows validation still passes the original 2,406 framework self-tests plus 34 compatibility tests.

The validated baseline is local commit `dbab2a2`. End-to-end TCP validation of deployed driver 0.1.000.0021 passed on 10 September 2026. Discovery and execution returned XML to the desktop: compatibility 34 passed, zero failed, with 34 streamed starts and finishes; framework self-tests 2,407 passed, zero failed, 55 skipped, four timing warnings, with 2,418 start events and 2,466 finish events. NUnit emits finish events for skipped tests that may have no start event. Processor TCP cancellation has not been exercised; cancellation and reconnect behavior were validated locally. Runner connection settings were prepared in ignored local files.
## Repeated execution and interrupted-run investigation

Package 0.1.000.0023 builds successfully. Verification now executes the framework suite twice in the same loaded assembly. The first execution passed 2,406 tests with 60 skipped; the second completed with 2,402 passed, four failed, and 60 skipped. The compatibility suite passed all 34 tests against the packaged DLL. The four remaining failures are stream-equality tests: an independent console reproduction against the unmodified NUnit 4.6.1 net462 NuGet DLL confirms that stale pooled-buffer bytes can make identical short streams compare unequal. Full two-pass verification therefore remains failing. No tests are suppressed, and NUnit's binaries have not been patched. Version 0.1.000.0023 has not been deployed; the last deployed package remains 0.1.000.0021.

DelayedConstraintTests now initializes its static wait event in OneTimeSetUp, paired with its existing teardown disposal. CollectionOrderedConstraintTests creates fresh constraint case data for each discovery instead of retaining mutable constraint expressions in static arrays. The NUnit test assertions remain unchanged. The former disposed event was safely reproduced against the old packaged assembly; the processor log does not establish the precise cause of the observed TCP disconnect.

The Windows runner saves diagnostics for incomplete operations, preserves completed test results, changes active rows to Incomplete and displays the last active tests. Received progress is drained before processing a terminal response or disconnect. Regression checks against actual local TCP disconnects and host-error responses pass, as do the existing transport checks. Processor log output is now flushed per write.

The current TCP server and test execution share a process. Fatal process failures cannot preserve its TCP connection. Separate driver packages are not sufficient to guarantee process isolation under Crestron's documented driver grouping rules. Automatic reconnection has not been added.

## Driver processor package and package switching — 11 September 2026

The user reports that both KasaTapoCrestronDriver suites passed on the Crestron Home processor: Unit Tests (34 cases) and Processor lifecycle (20 cases). This is user-reported processor validation; the runner result XML was not collected in this task.

Windows regression validation passed for switching between two local TCP test packages while connected, refreshing discovery without losing an active connection, replacing the suite catalog, clearing stale selections, and disabling package switching during an active operation. Existing runner recovery, restart preferences, secure inputs, transport, cancellation and reconnect checks also passed.

## Windows runner .NET 10 migration — 11 September 2026

The Windows runner and UI/transport regression harness now target `net10.0-windows`. Shared transport and host libraries multi-target `net472;net10.0`. Processor packages and the validator that directly loads merged processor assemblies remain on `net472`; loading those Framework assemblies under .NET 10 is not used as a substitute for Framework validation.

- Debug runner/regression and Release processor/Framework-validator builds completed with zero warnings and errors. The processor Release build preserved `0.1.000.0027` and was not deployed.
- All local runner regressions passed: settings migration, read-only legacy files, preservation of newer settings, restart restoration, changed endpoints, package switching, incomplete results, secure input transfer, authentication, discovery, cancellation, rerun and reconnect.
- The manually pumped Windows Forms validation harness now explicitly retains its UI synchronization context across successive forms on .NET 10.
- Local mDNS checks passed for two packages using dynamically assigned ports, authentication and independent advertisement shutdown.
- The .NET 10 client discovered and authenticated with an existing processor over SFTP, discovered the KasaTapo driver unit suite and ran all **34 tests successfully**, with 34 streamed starts and finishes. No processor update was required. Captured result XML remains local and is not included in release assets.
- Framework validation of an existing merged KasaTapoClient package passed input reload/clear/opt-in checks and all **97 ordinary unit tests** over local TCP. Live physical-device tests were not run. This check now belongs to `CrestronHomeNUnit.DesktopValidation --package-inputs <merged-assembly> <work-directory>`.
- The self-contained Windows x64 release includes Microsoft.NETCore.App and Microsoft.WindowsDesktop.App **10.0.12**. Its executable constructed its Windows Forms window and closed normally. The ZIP contains the runtime, application dependencies and full runtime/dependency notices; private settings are absent.
- Connection defaults and input-file selections now use LocalAppData. Legacy import preserves original files and newer destination settings; the existing protected credential and restart-preference stores remain in place.

This migration does not fix or suppress the previously documented upstream NUnit repeated-run stream-comparison issue. GitHub publication remains pending README approval.
## Window placement persistence — 11 September 2026

The runner now saves normal bounds and maximized state immediately on location/size changes, independently of saved test selections. Minimized transitions and minimized shutdown do not change the saved placement. Writes use a temporary file followed by replacement, so an interrupted write retains the previous complete settings. The private file is `Runner.window.local.json` under the runner's LocalAppData directory.

The full runner regression harness passed, including new checks for persistence before close, immediate movement, ignored minimization, normal/maximized/minimized restart, original normal bounds after maximizing, invalid JSON, a removed monitor, negative monitor coordinates, smaller displays and working-area clipping. Builds completed with zero warnings and errors.