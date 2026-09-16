# Crestron Home NUnit v1.6.0

Add explicit shared-driver reboot scope for removing temporary V1 instances. Existing plans retain their previous behavior.

- Allow `testPackage.additionalRemovalRebootDeviceIds` only with explicitly authorized removal reboots. The reported scope must exactly match the selected instance and reviewed additional IDs.
- Remove only the selected temporary instance. Require the additional instances to retain their identities, room assignments, versions, loading state and reported configuration after reboot.
- Use published DevTools 1.4.0 for the workflow and Test Explorer adapter.
- Document successful Wiser installed-room Auto/Manual/Auto restoration and the complete deliberately failing production-driver workflow with verified previous-code restoration. The original failed result remains failed.

Validation: plan validation, serialization and preservation guards have automated coverage. Apple TV V1 initial installation and removal were verified on the development MC4-R with two configuration-aware reboots and existing instances preserved. Initial startup verification was resumed read-only after a timeout; no install or reboot command was repeated. This is hardware evidence with assisted verification, not a claim that that complete cycle ran unattended.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.6.0** or the matching runner/CLI ZIP. Official NUnit 4.6.1 is unchanged. Processor test packages remain GitHub-only.

See [V1 lifecycle configuration](docs/ContinuousIntegration.md), [installed controls](docs/InstalledDriverControls.md) and [rollback limits](docs/DriverRollback.md). The Wiser rollback result applies to its exact reviewed package pair. Other drivers need their own compatible verifier; V1 rollback and initial-install rollback remain unsupported. Keep plans, credentials and recovery evidence private.
