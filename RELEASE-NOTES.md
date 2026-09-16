# Crestron Home NUnit v1.7.0

Add optional Android UI testing to the shared CLI and Visual Studio workflow, and support testing a supplied Release driver package without rebuilding it.

- Include the .NET 10 Android toolkit in the TestAdapter NuGet package. Driver repositories can create ordinary NUnit UI projects without a second source checkout; no emulator or Crestron APK is bundled.
- Hold the processor and Android reservations together, require every discovered UI test to pass, and retain private package-bound results, captures and starting-state restoration evidence.
- Provide guarded Home navigation, saved-endpoint inspection and read-only extension checks. Scope repeated controls to their labelled rows. Retry bounded hierarchy reads, but never automatically repeat taps or Back commands.
- Validate an optional prebuilt Release candidate against its SHA-256, driver GUID, version and clean source commit. Preserve its original filename and bytes throughout the workflow.
- Reconcile Debug revisions when a manifest uses the output package's basename instead of the project name. Ambiguous or custom layouts can set `manifestPath` explicitly.
- Extend release acceptance to run the Android regressions and exercise the packaged Android API alongside normal NUnit and workflow adapters in an isolated consumer.

Validation: 240 workflow, 65 Android and 11 adapter regressions passed, together with isolated package acceptance. The packaged Wiser workflow passed 96 desktop tests, 53 processor tests, three read-only live tests, the gated Debug driver update, three installed health checks and both discovered Android cases. UI state was restored and reservations released. The temporary test instance was removed; a preserved archive from the preceding failed validation was subsequently removed with identity/hash checks and retained backups. Existing gateway and room identities, schedules and setpoints were preserved.

The Android hardware validation used minimized BlueStacks in a logged-in Windows session. It does not establish service-session operation, the app's active network route, physical UI control coverage or Crestron certification. The pinned Release handoff has automated coverage but still needs exact-candidate hardware validation. Final submission requirements, forms and delivery remain separate work.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.7.0** or the matching runner/CLI ZIP. Existing plans remain valid; Android and Release-candidate testing are opt-in. Official NUnit 4.6.1 and DevTools 1.4.0 remain unchanged. Processor test packages remain GitHub-only.

See [Android setup and limitations](docs/AndroidUiTesting.md), [Release candidate testing](docs/ReleaseCandidateTesting.md), and [version reconciliation](docs/ContinuousIntegration.md#readiness-and-version-identity). Keep credentials, worker settings and all device evidence private.
