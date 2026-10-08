# Crestron Home UI tests on an Apple Silicon Mac

## Publication status

**Unreleased candidate. Do not use TestAdapter 2.3.0 for these APIs.** The local
package version is `2.4.0-mac-preview.2`; this is a development artifact, not an
announced release. Publication waits for a complete live device-control and
restoration test. See [validation](MacUiValidation.md).

`CrestronHomeNUnit.Mac` is included in the existing TestAdapter package, alongside
the Android helpers. It targets .NET 10, uses the W3C WebDriver interface supplied
by Appium Mac2, and has no NUnit dependency itself. Consuming tests use NUnit
**5.0.0**, including awaited `Assert.ThrowsAsync` assertions. This is phase-two
testing support: it does not prepare documents, sign, send email or submit to
Crestron. It does not add a requirement to test both Android and Mac.

The Crestron Home iPhone/iPad app on Apple Silicon is the tested application.
Results identify that surface explicitly. They are not physical iPhone/iPad
results. The package does not include Crestron apps, Appium, Xcode or an emulator.
Configure Pro automation is outside this implementation.

## Prerequisites and permissions

Use an Apple Silicon Mac with the Crestron Home app installed and the intended
Home already connected. Setup previously used Home 4.12.11, macOS 27.0, Xcode
27.0, Node 24.21.0, Appium 3.8.0 and Mac2 4.3.5. These are observed versions, not
minimum requirements or promises of compatibility with other versions.

Install Xcode, complete its first launch and review its licence interactively.
Follow the upstream [Mac2 setup guide](https://appium.github.io/appium-mac2-driver/)
for required Accessibility/automation permissions. Grant app Local Network access
where macOS requests it. Keep the desktop **logged in and unlocked** throughout a
job. SSH connectivity does not prove that XCTest can use the desktop. Do not
disable screen-lock policies, reset app permissions or accept licences as part of
a test. No monitor-disconnected or reboot-recovery guarantee is currently made.

In a dedicated tooling directory, the known setup can be installed with:

```sh
npm install --save-exact appium@3.8.0 appium-mac2-driver@4.3.5
npx appium driver list --installed
npx appium driver doctor mac2
```

Keep that directory's lock file. Use its Appium installation consistently; an
unrelated `APPIUM_HOME` can hide the installed driver. Start Appium as the logged-in
GUI user, bind it to `127.0.0.1`, and keep its lifetime scoped to one job. Remote
test runners use an authenticated SSH tunnel with a verified host key:

```sh
ssh -N -L 4727:127.0.0.1:4727 test-user@your-mac
```

Do not expose Appium to the LAN. `MacHttpTransport` accepts loopback endpoints
only, disables redirects and bounds each response and command. The test project
can run on Windows with this tunnel or on the Mac with a .NET 10 SDK. Visual
Studio, VS Code and CI run the **same NUnit project**.

## Job ownership and cleanup

Reserve both the Mac UI and target processor using your existing job coordinator
before starting Appium. No other test or person should operate that app during
the job. The session's `verifyOwnership` callback must verify the reservations
before each command. A private token file is demonstrated in the sample; the
coordinator must create it exclusively and hold the real reservations. Merely
writing a token does not acquire a processor reservation.

The sample [with-appium.sh](../tools/mac/with-appium.sh) wraps a **preconfigured,
job-owned** GUI LaunchAgent and a local test command on the Mac. It refuses an
existing service, a locked desktop or a busy automation port. It shuts down only
that service and checks that its ports have closed, even if tests fail. A remote
Windows job needs the same ownership and shutdown arrangement on its Mac host;
deleting the WebDriver session alone may leave XCTest running and block desktop
interaction. Do not kill unrelated XCTest or Appium processes.

Each `MacTestSession` creates a new private evidence directory. It never reuses
previous results. Dispose the session, then stop the owned host service, verify
shutdown and release reservations **only after restoration is confirmed**. An
interrupted job requires reconciliation; do not automatically repeat its clicks.

## Discover and run the sample

Until publication, use the source project:

```powershell
dotnet test samples/MacUiTests/MacUiTests.csproj -c Release -p:UseSourceMac=true --list-tests
```

Discovery reads no settings and opens no connection. Copy
[profile.example.json](../samples/MacUiTests/profile.example.json) to a private
location and replace all example values. Do not commit profiles, credentials,
screenshots, processor data or ownership files. Metadata must match independently
verified versions and driver/candidate identity; writing it into JSON does not
verify the running driver.

The job owner sets `CRESTRON_MAC_PROFILE`. Select the explicit test by its full
name; a category filter alone should not be relied on to select explicit tests:

```powershell
$env:CRESTRON_MAC_PROFILE = 'C:/private/mac-profile.json'
dotnet test samples/MacUiTests/MacUiTests.csproj -c Release -p:UseSourceMac=true --filter 'FullyQualifiedName=MacUiTests.RoomTests.RoomCanBeInspectedAndHomeRestored' --logger trx --results-directory C:/private/results
```

In Visual Studio or VS Code, provide the same environment to the test process and
explicitly run that test. After publication use the published TestAdapter version
through `MacAdapterVersion` and omit `UseSourceMac`. Do not specify the unpublished
preview version against public NuGet.

The room sample inspects a visible room and restores Home. It does not operate a
device or prove submission compliance. It intentionally stops on an unknown,
missing or ambiguous layout; automatic scrolling and saved-Home switching are not
implemented. Select/connect the intended Home before the job.

## Driver-specific control tests

Keep device expectations in the driver's own NUnit project. Before any control:

1. Verify the app's selected Home and saved local endpoint against the reserved
   processor, and independently verify installed driver and child identities.
   A matching room or device label alone is insufficient.
2. Capture the initial device state through an independent driver/device API and
   ensure the app agrees. Bind controls to the exact intended device. Review
   literal selectors against retained source; never infer coordinates.
3. Run one change, check both UI feedback and independent device state, restore
   the initial state and verify it through both paths. Restore Home afterward.

`MacBinaryControl` provides a fixture-defined on/off mapping. Its setter reads
fresh state and only clicks when a change is needed. `VerifyBinaryControlAsync`
coordinates the test and restoration using fixture-owned independent observation
and UI-setting callbacks:

```csharp
await session.VerifyBinaryControlAsync(
    readIndependentState: ReadOutletStateAsync,
    setStateAndVerifyUi: (target, token) => outlet.SetAsync(session, target, token),
    restoreHome: navigation.HomeAsync,
    token: TestContext.CurrentContext.CancellationToken);
```

`ReadOutletStateAsync` must observe the actual bound device or processor. It must
not read the same Mac UI or return the requested state. The fixture must first
check initial UI/device agreement. Use a dedicated on/off outlet for this sample;
lights with brightness or colour require restoration of **all** changed properties.
The helper does not claim physical feedback from a UI label alone.

Unknown state stops the test. Input failures are not retried. Restoration gets a
separate bounded cancellation scope; its setter must observe current state and
set an explicit target, not blindly toggle. Both the original failure and cleanup
failure remain failures. If restoration cannot be established, retain ownership
for operator reconciliation and do not start another test on that device.

## Results and phase boundaries

Normal NUnit/TRX results and attachments are the test result. Private evidence
includes initial state, fresh pre-input XML/PNG, intent before each click,
command-return records, changed/restored captures, and session-close records.
Capture inventories contain SHA-256 digests and UTC observation times. These are
integrity checks, not signatures or proof that a driver is correct. Screenshot
time and hierarchy-observation time are distinct.

The 20-second readiness waits and transport deadlines are tooling budgets, **not
Crestron response-time requirements**. These helpers do not implement a certified
latency measurement. The existing Android workflow manifest does not automatically
recognize a Mac stage: consume the explicit NUnit fixture as a phase-two project
and retain its results through the normal test pipeline. Phase three remains
unchanged and must not accept unvalidated Mac evidence as an Android result.

## Troubleshooting

* **Mac2 startup timeout:** verify the console is unlocked; inspect original
  Appium/Xcode logs. Do not repeatedly restart a locked desktop or increase the
  timeout to hide missing permissions.
* **No SceneWindow/duplicate control:** retain source and screenshot, confirm the
  app version and frontmost page. Hidden duplicate Dialog trees are excluded.
* **Unknown state or UI/device disagreement:** fail and investigate the driver,
  app and binding; do not increase tolerances or convert the result to a pass.
* **Cleanup failed:** retain all failures and reservations. Stop only owned
  automation and verify the app/device state before resuming.
* **Cannot connect to Home:** diagnose the app's network permission, saved address
  and reachability separately; do not reset or redeploy the processor.

Offline regressions require no Mac or device:

```powershell
dotnet test CrestronHomeNUnit.Mac.Tests/CrestronHomeNUnit.Mac.Tests.csproj -c Release
```

The release build also restores a fresh consumer of the packed adapter and runs
the same regressions against its actual assemblies and XML documentation.
