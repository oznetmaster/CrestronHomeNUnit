# Crestron Home NUnit v1.7.1

Fix saved-connection inspection on portrait Android screens where the local-port field is below the visible area. The navigator first verifies and records the Home name and local address, makes one guarded scroll, then verifies the port. It closes the editor and verifies return to Home even when inspection fails. An uncertain scroll is never repeated.

The setup guide now covers Visual Studio's Android SDK Manager, Windows acceleration, and installing the Crestron Home APK without a Google account. It also explains how to avoid conflicting ADB versions when BlueStacks and Google's emulator share a computer.

Validation: all 67 local Android tests passed. Both read-only Wiser gateway cases passed through a private packaged adapter candidate with Google's Pixel 7 Android emulator minimized. Eight screenshot/hierarchy pairs were verified; gateway state was unchanged, Home was restored and both reservations were released. These results cover a logged-in Windows session and an already-installed Debug driver, not service-session operation or complete submission acceptance.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.7.1** or the matching runner/CLI archive for the portrait-screen fix. Existing processor test packages do not need redeployment for this Windows-side change. NUnit 4.6.1 and DevTools 1.4.0 remain unchanged.

See [emulator and app setup](docs/AndroidEmulatorSetup.md), [Android UI testing](docs/AndroidUiTesting.md), and [validation details](Validation.md). Crestron submission remains a separate opt-in workflow under development.