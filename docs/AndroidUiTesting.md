# Crestron Home NUnit UI automation library

The optional Android stage requires **CrestronHomeNUnit 1.7.0 or later**. It extends the shared CLI/Test Explorer development workflow and is a foundation for the Crestron submission workflow described below. A combined Wiser development workflow passed on real hardware on 16 September 2026; Release-candidate submission integration remains unfinished.

The **Crestron Home NUnit UI automation library** (`CrestronHomeNUnit.Android`) runs on the Windows test computer and communicates with the Crestron Home Android app through ADB. It is part of this project. Its runtime requirement is .NET 10, plus an existing ADB installation and an explicit Android device serial. `CrestronHomeNUnit.Android.Tests` contains offline NUnit tests; discovering or running that project does not contact Android, install drivers or operate physical devices. The new library has no Android emulator or Crestron APK bundled with it and is not currently a separate NuGet package.

## Verified development behavior

The existing BlueStacks 5 Pie64 instance and Crestron Home Android app were used for a read-only proof on the development processor. An ADB script opened WeatherLink, checked the current-conditions title, section labels and a numeric temperature, then closed the detail screen and verified return to Home. The same proof passed after the BlueStacks window was minimized. Desktop input is not required for Android navigation or screenshots.

The documented BlueStacks hide shortcut did not hide the window in this setup. Normal minimization worked and the user confirmed that BlueStacks remained running minimized. This establishes operation while minimized in a logged-in Windows session, not operation after logout, from session 0 or after reboot. Unattended startup and recovery remain a separate worker-validation task.

The original weather proof used a private script. On 16 September 2026 a private .NET diagnostic also used the public Android library against the minimized emulator under the shared processor lease and a separate Android reservation. It closed the read-only Wiser panel, opened the selected system's saved connection details, verified the local address and port, returned to the system list, reconnected to the same Home, and verified the unobstructed Home screen. The connection/navigation sequence passed twice. No setting was changed and no physical-device command was sent; both reservations were released afterward.

That diagnostic verified .NET capture, guarded taps, scoped field assertions and page navigation in a logged-in desktop session. A subsequent complete CLI development workflow passed 96 desktop tests, 53 processor tests and three read-only live hub tests, verified the gated Wiser driver update, then passed all three discovered Android NUnit tests. The Android tests checked Home readiness and repeated saved-endpoint inspection/Home restoration twice. Their TRX, seven capture observations, discovery coverage and matching restoration record were retained under the actual workflow run/package identity. Both reservations were released. The temporary test instance and package file were removed; Home retained a cached catalogue entry until its next planned reboot.

A subsequent isolated NuGet-consumer workflow used the driver-specific Wiser Android project with no UI automation library source reference. It passed 96 desktop, 53 processor and three live tests, updated the actual Debug driver, passed three installed health checks and both discovered gateway UI cases, and confirmed Home restoration. The tests compared Hot Water and Away row status, action labels and enabled state with fresh management observations. The workflow removed its temporary instance and released both reservations; an archive preserved from the earlier failed validation was separately removed after verifying the run receipts, identity and package hash. Gateway and room identities, schedules and setpoints were preserved.

This validates the integrated development workflow, not an exact Release candidate, all driver UI controls, the app's active network route, or its binding to a specific installed driver instance. Ordinary offline test projects still use simulated Android transports only. The real Android fixture sends navigation inputs but no configuration edits or physical-device commands.

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

Saved connection settings and a matching visible Home are useful target evidence, not independent proof of the app's active transport, DNS resolution or a specific driver instance. Before adding physical controls, bind the intended installed instance and room to management API data and observed UI, and reject ambiguous names. Configured remote access or changing DNS needs additional active-route verification. These cases remain open; a label match must not be used to waive them.

The initial interface intentionally has no credential entry or physical-device test sequence. `TapAsync` is a low-level primitive: a tile can itself trigger a physical command. Driver-specific fixtures must choose reviewed navigation controls and enforce the test policy before calling it.

Hierarchy output masks text and accessibility descriptions of password fields. Other personal/device information can remain. Screenshots are unredacted and must go to private evidence storage. The interface checks the PNG header, not the full image. Visual validation, redaction and evidence retention remain responsibilities of the workflow.

## Opt-in workflow setup

For your own .NET 10 NUnit UI-test project, consume the Android assembly from `CrestronHomeNUnit.TestAdapter` 1.7.0 or later alongside `NUnit`, `NUnit3TestAdapter` and `Microsoft.NET.Test.Sdk`. Mark test-tool dependencies `PrivateAssets="all"` and do not add a `Workflows.xml` to that UI-test project. The existing workflow container remains a separate project. The bundled sample uses a source reference to exercise the UI automation library being developed; consuming driver repositories do not need a second checkout. Publish the required test adapter version before pushing dependent driver-project updates.

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

The workflow currently records an aggregate Android stage plus the private detailed TRX and captures. Its development source digest is not a release commit identity. The opt-in [prebuilt Release handoff](ReleaseCandidateTesting.md) also carries the verified source commit in the Android context, capture observations and coverage record. That handoff has automated coverage but has not yet passed Release hardware validation. The current tests and captures do not satisfy the final submission gate: the approved requirement mapping, exact Release candidate installation and additional environment provenance remain to be integrated. Matching discovery proves execution coverage of the configured project; it does not establish that the project implements every official submission requirement.

## Work remaining before CI operation

1. Extend saved-endpoint verification to the app's active route and exact installed-instance binding.
2. Validate the combined processor and Android reservations with the installed runner service and crash recovery. The logged-in CLI workflow has passed with both reservations and confirmed release.
3. Add opt-in NUnit driver fixtures with page models, image checks, live feedback verification and starting-state restoration. All discovered cases are now required by the stage; mapping those cases to all applicable official requirements remains open.
4. Prove supervised startup/recovery from the installed GitHub runner service. Direct player startup and ADB capture were separately demonstrated without Windows input, but this does not establish headless, logged-out or session-0 operation. A controlled interactive worker or an emulator with supported headless operation may be needed.
5. Retain candidate-bound evidence and feed it into the submission gate. Accessibility text alone does not prove icons, layout or timely device response.
6. Cover Configure/Setup dialogs separately from the end-user Android app. Extend to outage, endurance and multiple-instance requirements only with the necessary equipment and device bindings.

ADB and the emulator must be privately configured by the developer. Use local access where possible; the development proof left remote ADB disabled and verified loopback listeners. Do not expose an ADB port publicly. All driver-control tests must follow the existing [state restoration and deployment policy](ContinuousIntegration.md).

The UI automation library also provides `CrestronHomeNavigation.InspectHomeExtensionAsync` for read-only inspection of a uniquely named Home tile. It verifies the extension page title, invokes the fixture's assertions and restores Home even after a failed assertion. `CrestronHomePages.ReadStatusAndButton` reads a labelled row without confusing repeated button IDs. A fresh hierarchy read may be attempted up to three times after a transient capture failure; neither taps nor Back commands are retried. These helpers require version 1.7.0 or later.

The Wiser driver's separate Android test project uses these helpers to compare gateway controls with fresh management state. A unique name plus matching saved endpoint does not establish the active Android network route, so these initial fixtures send no physical device commands. Room controls, stronger instance binding and submission-contract integration remain required work.

Build and run the offline project:

```powershell
dotnet test CrestronHomeNUnit.Android.Tests/CrestronHomeNUnit.Android.Tests.csproj -c Release
```
