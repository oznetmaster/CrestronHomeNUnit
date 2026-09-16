# Crestron Home NUnit v1.8.0

Add room and nested-page inspection to the Crestron Home NUnit UI automation library included in the test adapter. Tests can open a named room extension, inspect nested extension pages and verify complete selection lists without choosing an option. Page names, navigation controls and expected values are supplied by each driver's test fixture. Controls are scoped to the front page even when the app retains background pages with identical resource IDs.

The navigation helpers restore the original Home screen after successful checks or assertion failures. Nested pages use explicitly supplied close/cancel controls. Unknown layouts stop navigation; uncertain taps, Back commands and scrolls are never replayed. Selection inspection handles clipped viewport-edge rows while preserving the strict coordinate checks used for input.

Validation: all 91 Android regression tests passed, including against a private packaged adapter. Read-only checks in the minimized Google emulator verified complete selection lists and their selected values for the exercised fixture. Editing was cancelled, Home and checked device settings were preserved, both reservations were released, and all 13 accepted capture pairs matched their retained hashes. Earlier controlled failures also restored Home and preserved checked state. This validates the helpers against an already-installed Debug driver; it is not full submission acceptance or certification.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.8.0** in the separate .NET 10 Android test project. Existing processor test packages do not need redeployment for these Windows-side UI helpers. NUnit 4.6.1 and the adapter's default DevTools 1.4.0 dependency remain unchanged; projects using DevTools 1.5.0 may retain that explicit dependency.

See [room and nested-page testing](docs/AndroidUiTesting.md), [emulator and app setup](docs/AndroidEmulatorSetup.md), and [validation details](Validation.md). The expanded sample driver NUnit fixture also passed its gateway and room checks against the local stable package candidate, with matching discovery coverage and confirmed restoration. The final Crestron submission workflow remains separate work.
