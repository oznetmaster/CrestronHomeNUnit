# Crestron Home NUnit v1.3.0

Add optional automatic storage cleanup for successful CI test runs, while preserving manual deployments.

- `removeTestPackageAfterSuccessfulRun` removes only this workflow's uploaded test archive after all requested stages pass and its test instance is confirmed removed. It requires `removeTestInstanceAfterRun` and defaults to false.
- Capture pre-existing storage paths before upload; preserve those paths and every package still referenced by an installed model or alias. Verify exact package identity, version and SHA-256 against the retained build, and save a backup and operation evidence before deletion.
- Hold the shared processor reservation and execution marker throughout removal and catalogue refresh. Uncertain cleanup retains the reservation for inspection; no command is automatically replayed.
- Free storage and remove persisted catalogue references without rebooting. Home may retain a cached catalogue entry until its next planned reboot; workflow results report this explicitly.
- Retain original package filenames in separate processor/actual artifact folders, so deployments no longer create generic `processor.pkg` or `actual.pkg` names.
- Management requests use the configured workflow stage deadline, avoiding the shorter default HTTP timeout during slow activation.

Validation: 100 desktop workflow regressions passed. A complete MC4-R test-only run passed 47 local tests, 47 processor tests and three read-only live tests, then automatically removed its instance and hash-verified archive and released the reservation. All 14 pre-existing storage paths were protected. No actual driver was updated and no reboot was requested. An earlier activation timeout was reconciled separately and is not counted as a successful end-to-end run.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.3.0** or the matching complete runner/CLI ZIP. Existing compatible processor test hosts do not require replacement solely for this desktop workflow feature. DevTools remains 1.1.0 and NUnit remains official 4.6.1. Only the desktop adapter is published to NuGet; processor packages remain GitHub-only.

For CI, enable both cleanup options in the private workflow plan. The hardware-bridge template requires the storage-cleanup stage to be reported successfully, so an older adapter cannot silently ignore the new setting. Leave the package-cleanup option false for retained manual deployments. Keep private plans and recovery artifacts off public repositories.

See the [CI guide](docs/ContinuousIntegration.md), [hardware CI setup](docs/GitHubHardwareCI.md) and [Test Explorer guide](docs/VisualStudioTestExplorer.md). Generic installed-device control/restoration, automatic rollback and cross-run artifact reuse remain separate work.