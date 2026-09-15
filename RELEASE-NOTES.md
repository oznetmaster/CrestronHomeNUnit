# Crestron Home NUnit v1.2.2

Patch release fixing Debug version collisions in the CLI and Visual Studio Test Explorer workflow when a newer build has been deployed manually.

- Reconcile standard project manifests with the highest matching catalogue revision before building, under the shared processor lease. Preserve the source major/minor/patch version; the normal build script allocates the next Debug revision.
- Verify the built driver's identity and fresh version before upload, and check again for conflicting catalogue versions before deployment. Newer release versions, unreadable matching versions and exhausted counters require deliberate correction.
- Preserve manifest formatting, UTF-8 BOM and unrelated values. Custom manifest layouts continue to require explicit counter management.
- Include the reusable private hardware-CI bridge template and setup documentation published since 1.2.1. These cover exact-source GitHub App checks, approved source selection, release preflight and adding projects.

Validation: 73 desktop workflow regressions passed. A complete MC4-R test-only run started with source revision zero, reconciled catalogue baseline 1.1.1.5, built 1.1.1.6 and passed 69 local plus 69 processor tests. Its temporary test instance was removed and the processor lease released. No actual-driver update was attempted in this validation.

## Updating

Update the desktop workflow project to **CrestronHomeNUnit.TestAdapter 1.2.2**, or extract the complete runner/CLI ZIP. The workflow continues to use **CrestronHomeDevTools 1.1.0** and official **NUnit 4.6.1**. Processor test packages remain GitHub assets only; the desktop adapter is the only NuGet package in this release.

The correction runs in the desktop workflow backend. Existing compatible processor test hosts do not need redeployment solely for this fix. Private CI revision counters remain useful after catalogue packages have been removed. Upgrade pinned CI tooling deliberately; publishing this release does not change another repository's pinned dependencies or enable its hardware jobs.

See the [Test Explorer guide](docs/VisualStudioTestExplorer.md), [CI workflow guide](docs/ContinuousIntegration.md) and [hardware-CI setup](docs/GitHubHardwareCI.md). Public release checks certify the tested source and retained Debug artifacts, not independently rebuilt Release bytes. Cross-run artifact reuse, generic installed-device control/restoration and rollback remain separate work.
