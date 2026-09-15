# Crestron Home NUnit v1.4.0

Add optional installed-device control testing with independent physical observation and restoration, and explicit reuse of retained workflow packages.

- `deployedControls` captures a device's current state through a private read-only probe, issues an absolute command, verifies the driver's command-completion counter and independently observes the physical result. Cleanup restores and verifies the original state within a separate deadline.
- Require exact device/model/physical identity, root-driver ancestry and version. Uncertain command completion, overlap, restart or restoration retains the processor reservation for investigation. Boolean devices can use distinct absolute On/Off commands; arbitrary parameterized setters require driver-specific validation.
- Save private control intent and restoration evidence before mutation. New shared execution guards keep cooperating test and management tools from overlapping the control cycle.
- `artifactReuse` verifies a successful previous run, released lease, source identity, build inputs, driver identity and retained package SHA-256. Every required test stage runs again. An equal/newer catalogue version causes a fresh Debug build; corrupt evidence stops the run.
- Resolve DevTools 1.3.0 from NuGet. Official NUnit 4.6.1 remains unchanged.

Validation: 151 workflow regressions passed, including a retained-artifact path that succeeds with a deliberately unbuildable project. The KasaTapo hardware workflow passed 71 local tests, 71 processor tests, three processor live tests and four installed checks, including physical outlet control and restoration. The temporary instance and archive were removed and the reservation released. Cross-processor reuse has not yet been hardware-validated. Automatic rollback is not enabled by this release.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.4.0** or the matching complete runner/CLI ZIP. Existing compatible processor test hosts do not require replacement solely for these desktop features. Only the adapter is published to NuGet; processor packages remain GitHub-only.

Both features are opt-in. Installed controls require a suitable driver identity/completion contract and an independent read-only device probe. Keep plans, inputs and raw evidence private. See [installed-driver controls](docs/InstalledDriverControls.md), [artifact reuse](docs/ArtifactReuse.md), the [CI guide](docs/ContinuousIntegration.md) and [Test Explorer setup](docs/VisualStudioTestExplorer.md).
