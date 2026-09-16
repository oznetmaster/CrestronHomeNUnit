# Android UI testing for driver submissions

This is source development for the [Crestron submission workflow](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/CrestronSubmission.md). It is not included in the published 1.6.0 tools and does not yet add UI stages to the CLI or Test Explorer workflow.

`CrestronHomeNUnit.Android` is a .NET 10 library that uses an existing ADB installation and an explicit Android device serial. `CrestronHomeNUnit.Android.Tests` contains offline NUnit tests; discovering or running that project does not contact Android, install drivers or operate physical devices. The new library has no Android emulator or Crestron APK bundled with it and is not currently a separate NuGet package.

## Verified development behavior

The existing BlueStacks 5 Pie64 instance and Crestron Home Android app were used for a read-only proof on the development processor. An ADB script opened WeatherLink, checked the current-conditions title, section labels and a numeric temperature, then closed the detail screen and verified return to Home. The same proof passed after the BlueStacks window was minimized. Desktop input is not required for Android navigation or screenshots.

The documented BlueStacks hide shortcut did not hide the window in this setup. Normal minimization worked and the user confirmed that BlueStacks remained running minimized. This establishes operation while minimized in a logged-in Windows session, not operation after logout, from session 0 or after reboot. Unattended startup and recovery remain a separate worker-validation task.

The proof used a private script. The new .NET primitives have offline coverage but still need an end-to-end Android run before their hardware behavior can be claimed verified. No hardware/UI operation runs as part of their current test project.

## Library foundation

```csharp
var transport = new AdbCommandTransport(adbExecutable, deviceSerial, TimeSpan.FromSeconds(25));
var device = new AndroidDevice(transport, "com.crestron.phoenix.app");
var hierarchy = await device.CaptureAsync(cancellationToken);
```

Selectors use resource ID, exact text or accessibility description within the configured application package. Taps capture a new hierarchy, run a caller-supplied page assertion, require one enabled target with valid bounds, then send one input. An uncertain input outcome is never automatically retried. The caller must observe the resulting page and decide what follows.

The initial interface intentionally has no credential entry or physical-device test sequence. `TapAsync` is a low-level primitive: a tile can itself trigger a physical command. Driver-specific fixtures must choose reviewed navigation controls and enforce the test policy before calling it.

Hierarchy output masks text and accessibility descriptions of password fields. Other personal/device information can remain. Screenshots are unredacted and must go to private evidence storage. The interface checks the PNG header, not the full image. Visual validation, redaction and evidence retention remain responsibilities of the workflow.

## Work remaining before CI operation

1. Add private device/session profiles, exact processor/home/installed-instance verification and bounded application readiness checks.
2. Integrate the existing processor lease and a separate Android-session lock, acquired in a consistent order. No UI controls may run alongside conflicting hardware work.
3. Add opt-in NUnit driver fixtures with page models, image checks, live feedback verification and starting-state restoration. Require results from all discovered applicable checks rather than fixed expected counts.
4. Validate the .NET implementation on the minimized emulator, then prove supervised startup/recovery from the installed GitHub runner service. A controlled interactive worker or an emulator with supported headless operation may be needed.
5. Retain candidate-bound evidence and feed it into the submission gate. Accessibility text alone does not prove icons, layout or timely device response.
6. Cover Configure/Setup dialogs separately from the end-user Android app. Extend to outage, endurance and multiple-instance requirements only with the necessary equipment and device bindings.

ADB and the emulator must be privately configured by the developer. Use local access where possible; the development proof left remote ADB disabled and verified loopback listeners. Do not expose an ADB port publicly. All driver-control tests must follow the existing [state restoration and deployment policy](ContinuousIntegration.md).

Build and run the offline project:

```powershell
dotnet test CrestronHomeNUnit.Android.Tests/CrestronHomeNUnit.Android.Tests.csproj -c Release
```
