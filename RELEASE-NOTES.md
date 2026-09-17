# Crestron Home NUnit v1.11.0

Android UI fixtures can now inspect extension pages whose controls do not fit on one screen. The shared navigation session exposes guarded scrolling in both directions, and saved-processor endpoint verification can reveal the local port on smaller screens.

- Add `CrestronHomeExtensionNavigation.ScrollDownAsync` and `ScrollUpAsync`. Each validates the expected front page and caller assertions, sends one gesture within its observed viewport, and confirms the expected page afterward. Uncertain gestures are never automatically repeated.
- Reject ambiguous, disabled or unusable front-page viewports, open selection overlays, failed assertions and cancellation before sending a scroll.
- Replace the single-scroll assumption in saved-endpoint verification with an observed search of at most eight gestures. Incorrect or ambiguous fields, no progress, an exhausted search and uncertain input fail the check while retaining bounded Home restoration.
- Document fixture responsibilities for complete coverage, edge-clipped accessibility bounds, non-saving cancellation and independent device-state restoration. Scrolling alone does not prove movement, visual correctness or complete page coverage.

Validation: 139 Android regressions passed from source and through an isolated private adapter package. Package acceptance also checked ordinary NUnit API use, workflow discovery, execution guards and the identity of executed assembly bytes. A focused development run on a headless Google Android emulator used a reduced viewport, verified the saved endpoint through two observed scrolls, and inspected all expected labelled controls across two editor views. Independent device state, Home, emulator size, original inventory, temporary-child removal and reservation release were verified. This is development integration evidence, not final submission-candidate acceptance or certification.

## Updating

Update **CrestronHomeNUnit.TestAdapter** to **1.11.0** in Android fixture projects that need extension-page scrolling or smaller-screen endpoint verification. Use `InspectAsync` to record and check each viewport, bound searches explicitly, and supply reviewed navigation and non-saving close controls. These APIs do not infer physical-device operations or restoration.

The Windows runner and CLI are versioned with the release. Existing processor test hosts do not need redeployment for these desktop Android-navigation changes. NUnit remains 4.6.1 and DevTools remains 1.6.0 for the adapter/workflow dependencies.

See [extension-page scrolling](docs/AndroidUiTesting.md#scrolling-within-an-extension-page), [emulator setup](docs/AndroidEmulatorSetup.md) and [workflow stages](docs/ProcessorTestWorkflow.md).
