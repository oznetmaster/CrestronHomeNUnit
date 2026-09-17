# Crestron Home NUnit v1.10.0

The combined CLI/Test Explorer workflow can now create temporary managed children for an Android test project, pass their actual IDs to fixtures by alias, and remove the children after independently confirmed restoration. Setup and cleanup use the published CrestronHomeDevTools 1.6.0 library under the workflow's existing processor and Android reservations.

- Add optional `androidTests.managedChildren` targets and `AndroidRunContext.RequireManagedDevice(alias)`. The actual driver selected by the workflow supplies the parent identity. Missing, duplicate or unrelated child identities stop the stage; no manually installed child is adopted.
- Preserve failed test results after successful cleanup. Pending configuration, uncertain restoration, lost ownership and incomplete cleanup retain journals and reservations for reconciliation. Existing plans without temporary children keep their previous context format so older fixture packages do not receive an unknown field.
- Reveal room choices with bounded scrolling and require a fully visible choice before tapping. A clipped title behind the navigation bar cannot be used as a successful room selection; uncertain inputs are never automatically repeated.
- Read processor-package metadata from the actual package archive, including ManifestUtil 29 output layouts. Reject missing, misplaced or ambiguous metadata instead of trusting a stale sidecar file.
- Expand isolated adapter acceptance to run all discovered Android regression fixtures using the installed package and compare executed assembly bytes with the archive. The adapter and workflow now depend on DevTools 1.6.0; NUnit remains 4.6.1.

Validation: full workflow and Android regression suites passed. A private adapter candidate passed isolated installation, ordinary NUnit API use, workflow discovery and execution guards. A normal combined CLI workflow passed on a CP4-R with local tests, processor tests, read-only hub tests, an actual Debug driver update, temporary-child commissioning, one selected Android editor Cancel test, independently checked restoration and cleanup. Fresh inventory confirmed the original device identities and rooms were preserved, the owned child/test instance were absent, uploaded test-package storage was removed and reservations were released. Home retained cached catalogue metadata until a later planned reboot. The selected UI test sent no heating-control or schedule-save command. This is development integration evidence, not final driver submission-candidate acceptance, complete visual coverage or certification.

## Updating

Update the Windows runner/CLI and UI-test project's **CrestronHomeNUnit.TestAdapter** reference to **1.10.0** to use managed-child plans and bindings. Existing plans can continue without `managedChildren`. Fixtures that use aliases must explicitly map them to their own private device expectations and independently verify current identity/state before controls. No driver-specific settings are inferred or rewritten by the shared tools.

Existing processor test hosts do not need redeployment for the Windows workflow additions. New package builds use the corrected archive metadata verification. The separately downloadable NUnit self-test package is versioned with this release.

See [temporary managed-child tests](docs/AndroidUiTesting.md#temporary-managed-children-for-a-test-run), [workflow stages](docs/ProcessorTestWorkflow.md), [emulator setup](docs/AndroidEmulatorSetup.md) and [continuous integration](docs/ContinuousIntegration.md).
