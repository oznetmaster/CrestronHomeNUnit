# Crestron Home NUnit UI automation library

New to Google Android Emulator, BlueStacks or Android test setup? Start with [setting up an Android emulator on Windows](AndroidEmulatorSetup.md), including the copyable NUnit sample and first-run checks.

The optional Android stage requires **CrestronHomeNUnit 1.7.0 or later**. It extends the shared CLI/Test Explorer development workflow for ordinary driver regression testing, whether or not the driver will ever be submitted to Crestron. A combined sample driver development workflow passed on real hardware on 16 September 2026.

Client and library projects can use desktop and processor tests without an Android stage. These helpers also support ordinary driver development. Optional Crestron submission requirements and delivery are documented separately in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/CrestronSubmission.md).

The **Crestron Home NUnit UI automation library** (`CrestronHomeNUnit.Android`) runs on the Windows test computer and communicates with the Crestron Home Android app through ADB. It is part of this project. Its runtime requirement is .NET 10, plus an existing ADB installation and an explicit Android device serial. `CrestronHomeNUnit.Android.Tests` contains offline NUnit tests; discovering or running that project does not contact Android, install drivers or operate physical devices. The new library has no Android emulator or Crestron APK bundled with it and is not currently a separate NuGet package.

## Verified development behavior

Representative Debug workflows have exercised desktop and processor tests, an actual-driver update, installed health checks and Android inspection through the published packages. Retained discovery and TRX inventories matched, capture digests were checked, starting navigation and checked device settings were restored, temporary test storage was removed and reservations were released.

Read-only inspection also passed with Google Android Emulator and BlueStacks minimized in a logged-in Windows session. A Windows runner service executed inspection fixtures against an already-running Google emulator as NETWORK SERVICE. That service test did not deploy a driver or operate physical controls. Full service-driven deployment, emulator startup after logout/reboot and interrupted-run recovery remain separate validation work.

These are bounded integration checks of the shared helpers. They do not establish every driver's behavior, exact Release-candidate acceptance, complete visual correctness or certification. Page names, control expectations, fixture settings and per-driver results belong in the consuming project's documentation.

## Library foundation

```csharp
var transport = new AdbCommandTransport(adbExecutable, deviceSerial, TimeSpan.FromSeconds(25));
var device = new AndroidDevice(transport, "com.crestron.phoenix.app");
var hierarchy = await device.CaptureAsync(cancellationToken);
```

Selectors use resource ID, exact text or accessibility description within the configured application package. Taps capture a new hierarchy, run a caller-supplied page assertion, require one enabled target with valid bounds, then send one input. An uncertain input outcome is never automatically retried. The caller must observe the resulting page and decide what follows.

Set `AndroidSelector.AncestorResourceId` to scope a repeated control ID to its containing field. The app uses the same edit-control ID for the friendly name, local address, remote address and ports; a global text match can read the wrong field. `CrestronHomePages.RequireSavedLocalEndpoint` checks the friendly name and scoped local host/port without editing them. It compares literal IP addresses canonically and hostnames case-insensitively; it does not invent a DNS mapping between a hostname and an IP address. `RequireHome` rejects an otherwise matching Home name behind an open driver panel, menu or settings dialog. Crestron Home workflow session opening and the read-only sample use this unobstructed-page check.

`CrestronHomeNavigation.VerifySavedEndpointAsync` follows the verified address-inspection route: Home menu, My Systems, the selected card's menu, Edit, inspect local fields, Back, then reconnect to that same named card. The sample NUnit fixture executes this route twice after opening its session; it is not an implicit operation of `OpenFromEnvironmentAsync`. Card/control selection must be unique. No input is sent to connection fields and Connect on the editor is never pressed. Device Health requires the separate administration password and is not needed for this check.

The navigator retries only page reads within a bounded readiness interval. It tracks a pending input until departure from its previous page is observed; a timeout or another cleanup attempt cannot silently replay that input or declare the old Home restored. A rejected selector is distinguished from an input that might have been sent. Cleanup navigates only recognized pages belonging to the expected Home, uses a separate bounded cancellation window, and refuses to dismiss an unknown dialog. The fixture verifies Home again before writing its restoration completion record. This restores navigation state only; future physical control fixtures must also restore their device state.

Saved connection settings and a matching visible Home are useful target evidence, not independent proof of the app's active transport, DNS resolution or a specific driver instance. An optional temporary-name challenge provides stronger instance association; see [DevTools UI binding](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/DriverUiBinding.md). Before adding physical controls, also bind the intended room and physical device and implement their restoration. Configured remote access or changing DNS needs additional route verification; a familiar label alone must not waive those checks.

The initial interface intentionally has no credential entry or physical-device test sequence. `TapAsync` is a low-level primitive: a tile can itself trigger a physical command. Driver-specific fixtures must choose reviewed navigation controls and enforce the test policy before calling it.

Hierarchy output masks text and accessibility descriptions of password fields. Other personal/device information can remain. Screenshots are unredacted and must go to private evidence storage. The interface checks the PNG header, not the full image. Visual validation, redaction and evidence retention remain responsibilities of the workflow.

## Opt-in workflow setup

Use **1.7.1 or later for portrait screens**. If the local-port field is below the visible editor area, the navigator records the name/address first, scrolls once within the observed editor, then verifies the port. It does not edit fields or press Connect. Missing or incorrect fields after that bounded scroll still fail, and cleanup closes the editor before verifying Home restoration. Read-only inspection passed with the Google Pixel emulator minimized using this fix. See the service-session limits above; logged-out emulator operation remains unvalidated.

For your own .NET 10 NUnit UI-test project, consume the Android assembly from `CrestronHomeNUnit.TestAdapter` 1.7.0 or later alongside `NUnit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk`. Mark test-tool dependencies `PrivateAssets="all"` and do not add a `Workflows.xml` to that UI-test project. The existing workflow container remains a separate project. The [copyable sample](../samples/AndroidWorkflowTests/AndroidWorkflowTests.csproj) uses the published package by default. Tool contributors can set `UseSourceAndroid=true` to test the source library in this repository; consuming driver repositories do not need a second checkout. Publish the required test adapter version before pushing dependent driver-project updates.

Add `androidTests` to the private workflow plan, alongside the existing actual-driver target, processor live suites and installed-driver checks:

```json
"androidTests": {
  "project": "ABSOLUTE_PATH_TO_ANDROID_TEST_PROJECT",
  "profilePath": "ABSOLUTE_PATH_TO_PRIVATE_ANDROID_PROFILE"
}
```

The project must be inside a declared `sourceRoots` directory. Include its dependencies in those roots as well. Copy [the profile example](../examples/android-session.example.json) into private storage and supply an existing ADB executable, explicit serial, application package, exact visible home name, local application port and absolute lock filename in an existing private directory. `localPort` defaults to 50001 and must be in the range 1-65535. All cooperating plans for one Android session must use the same lock file. This reservation coordinates workers on one computer, not independent machines or manual Android use.

The workflow acquires the processor lease first and the Android reservation second, before building or deploying. If Android is reserved, it releases the untouched processor lease and stops. It runs the UI project after successful installed-driver checks and controls, while both reservations remain held. Plans without `androidTests` keep their existing behavior.

The [read-only sample](../samples/AndroidWorkflowTests/HomeReadinessTests.cs) owns one session for the whole project. Ordinary test discovery does not connect to Android, and execution without the workflow context skips the sample. The project is deliberately outside ordinary solution tests. An opted-in workflow supplies `CRESTRON_HOME_ANDROID_CONTEXT` only to its child test process. Opening the session verifies the local coordinator's process identity and reservation, then requires the configured home text in the current app hierarchy. It does not start an emulator, select a Home system or sign in. The visible name is a readiness guard; it does not independently prove the app's processor address or installed driver identity. Driver-specific verification is still required before any controls.

`CaptureAsync` saves a masked hierarchy, unredacted screenshot and passing observation only after the supplied assertion succeeds. These records carry the run ID, installed device ID, inspected package GUID/version, package hash, development source digest and capture hashes. Failed assertions retain their captures without a passing observation. Captures are sequential, not an atomic screenshot/hierarchy pair. Keep the entire results directory private.

Call `Complete(true)` once, after all inputs have finished and the original physical state has been verified restored. The sample navigates without changing settings or physical devices and confirms return to Home before completing. A fixture that adds controls must implement and verify physical restoration as well, including after failed assertions. Use `Complete(false)` when restoration cannot be confirmed. A matching completion record confirms restoration only; failed, skipped, empty or nonzero-exit tests still fail the stage. Missing or mismatched completion retains both reservations and blocks dependent recovery actions. Starting a second session in the same run is rejected even after completion.

The stage builds a .NET 10 NUnit test project into a fresh private run directory and captures the adapter's [structured discovery dump](https://docs.nunit.org/articles/vs-test-adapter/Tips-And-Tricks.html#dumpxmltestdiscovery-and-dumpxmltestresults). Execution uses that same build with `--no-build --no-restore`. It compares the full discovered test names with the TRX definitions and results, preserving duplicate-name multiplicity. Every discovered test must pass; no categories, including `Live`, are excluded. Explicit, ignored or invalid discovery entries are rejected before execution. Missing or additional tests, inconsistent counters, duplicated execution records, unknown definitions and non-NUnit results cannot pass. `coverage.json` retains the expected inventory, outcome and discovery hash alongside the original dump and TRX.

Discovery receives no Android session context. Fixture constructors, static initialization and `TestCaseSource` providers must be deterministic and free of device operations; open the session only in setup. Device state and discovery timestamps must not determine test identities. These are requirements for trusted fixture authors, not a sandbox around arbitrary test code. The supported adapter protocol was validated with NUnit3TestAdapter 6.3.0 using its normal TRX naming and discovery dump; alternate adapter naming settings must preserve those identities or the comparison will fail.

An interrupted reservation never expires or gets stolen. Before manually removing its marker, establish that the coordinator, child tests and ADB commands have stopped, reconcile physical state and any uncertain deployment, and follow the processor-lease recovery procedure. Then remove only the corresponding private Android marker. Merely closing the emulator does not prove restoration.

The workflow records an aggregate Android stage plus the private detailed TRX and captures. Its development source digest is not a release commit identity. The opt-in [prebuilt Release handoff](ReleaseCandidateTesting.md) also carries the verified source commit in the Android context, capture observations and coverage record. That handoff has automated coverage but has not yet passed Release hardware validation. Matching discovery proves that the configured project's tests executed; each driver project remains responsible for testing all of its intended behavior.

## Remaining test coverage and worker validation

1. Implement reviewed instance binding in consuming fixtures and bind their physical devices/rooms. Exact Release-candidate provenance and additional route verification remain separate requirements.
2. Validate the combined processor and Android reservations with the installed runner service and crash recovery. The logged-in CLI workflow has passed with both reservations and confirmed release.
3. Extend driver-specific NUnit fixtures with page models, image checks, live feedback verification and starting-state restoration. All discovered cases are required by the stage, but the framework cannot supply missing driver-specific assertions.
4. Prove supervised startup/recovery from the installed GitHub runner service. Direct player startup and ADB capture were separately demonstrated without Windows input, but this does not establish headless, logged-out or session-0 operation. A controlled interactive worker or an emulator with supported headless operation may be needed.
5. Validate rendered icons, clipping and response timing with suitable image and timing checks. Accessibility text alone cannot establish them. Configure/Setup application testing is separate from the end-user Android app covered here.

ADB and the emulator must be privately configured by the developer. Use local access where possible; the development proof left remote ADB disabled and verified loopback listeners. Do not expose an ADB port publicly. All driver-control tests must follow the existing [state restoration and deployment policy](ContinuousIntegration.md).

The UI automation library also provides `CrestronHomeNavigation.InspectHomeExtensionAsync` for read-only inspection of a uniquely named Home tile. It verifies the extension page title, invokes the fixture's assertions and restores Home even after a failed assertion. `CrestronHomePages.ReadStatusAndButton` reads a labelled row without confusing repeated button IDs. A fresh hierarchy read may be attempted up to three times after a transient capture failure; neither taps nor Back commands are retried. These helpers require version 1.7.0 or later.

### Room and nested-page inspection

`CrestronHomeNavigation.InspectRoomExtensionAsync(checkId, roomName, tileName, pageTitle, verify)` opens Rooms, selects the exact room title and named service tile, checks the extension page title, runs the supplied read-only assertions, and restores Home. Missing or duplicate names fail without choosing another device. The bottom tabs have no distinct accessibility names in the observed app version, so the helper validates their two-button structure and current bounds before tapping. A changed layout fails without using saved coordinates.

These APIs require `CrestronHomeNUnit.TestAdapter` 1.8.0 or later. The single-page helper inspects the initial room extension page. For nested pages, `InspectRoomExtensionPagesAsync` supplies a `CrestronHomeExtensionNavigation` session: use `OpenPageAsync` with an explicitly reviewed navigation control and a non-saving close/cancel control, `InspectAsync` for assertions scoped to the front page, and `InspectSelectionAsync` to read an entire selection list without choosing an option. The latter checks the complete expected labels and selected state, captures each viewport, and dismisses the picker. Navigation controls must be selected by the fixture author; the library cannot infer whether an arbitrary driver command changes physical state. Version 1.8.0 does not scroll offscreen room tiles; see the 1.8.1 support below. Physical device operations require separate fixtures. A room retained behind an extension does not count as the current page. Failed assertions still trigger bounded Home restoration, and uncertain navigation inputs are never replayed.

Room-tile scrolling requires **1.8.1 or later**; it is not included in 1.8.0. It searches downward within the selected room's observed service area, with a maximum of 12 gestures. Each swipe stays between the room toolbar and bottom navigation. The helper recognizes both the large room heading and its compact scrolled title, and taps a tile only when its full bounds are visible. Repeated viewports, ambiguous or disabled tiles, an unexpected room title, or a lost gesture response stop the search. Read-only observations allow animation to settle; a failed gesture is never repeated. Scrolling the room chooser remains unsupported. A missing tile remains a failed inspection even when Home restoration succeeds. Installing or configuring a driver does not prove that its tile is visible in the app.

The room helper was exercised repeatedly, including an intentional assertion failure, with observed Home restoration and preserved checked state. This validates the tested navigation paths; the consuming fixture must still establish device identity and its own restoration requirements.

A nested read-only inspection can be written as follows. The captions and page title are illustrative; supply those defined by the consuming driver:

```csharp
await navigation.InspectRoomExtensionPagesAsync(
    "details-inspection", roomName, tileName, roomPageTitle,
    async (pages, token) =>
    {
        await pages.OpenPageAsync(
            new(AndroidSelectorKind.Text, "Open details"), "Details",
            CrestronHomePages.Resource("customdevices_toolbarClose"), token);
        await pages.InspectSelectionAsync(
            "device-options", new(AndroidSelectorKind.Text, "SELECT OPTION"),
            expectedOptionLabels, expectedSelectedOption, token);
    }, cancellationToken);
```

The expected labels and selected value should come from fresh state for the identified device. Each check ID must be unique in the workflow evidence directory. The helper validates the entire expected page stack because background fragments retain repeated resource IDs. Unknown pages or uncertain navigation prevent further inputs; cleanup failures retain the original inspection failure. Leaving the callback restores nested pages using the supplied close/cancel controls before restoring Home. This does not itself establish physical-device identity, persistence of changed settings or submission compliance.

Nested-page and selection inspection passed in a development integration, including full list enumeration, selected-value checks, editor cancellation and Home restoration. All 91 offline Android regressions also passed against a private packaged adapter. These checks cover the exercised helper paths, not every consuming fixture or submission requirement.

Build and run the offline project:

```powershell
dotnet test CrestronHomeNUnit.Android.Tests/CrestronHomeNUnit.Android.Tests.csproj -c Release
```
