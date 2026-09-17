# Crestron Home NUnit v1.8.2

Fix final verification and cleanup after long Android UI test stages. The workflow opens a fresh processor configuration connection after confirmed UI restoration and before removing its temporary test instance, instead of depending on a configuration session that may have become idle during the tests.

Processor reservation ownership is checked before and after connecting. A failed connection or ownership check stops that step; device commands are not automatically replayed. Existing candidate identity, source checks, test evidence and restoration requirements remain in force.

Validation: all 245 workflow regression tests passed. A complete development workflow passed desktop tests, processor tests, live reads, the driver update, three Android inspection cases, final driver verification and temporary-instance cleanup. Both reservations were released. This verifies the exercised development workflow, not complete driver acceptance or certification.

## Updating

Update the CLI or the workflow project's **CrestronHomeNUnit.TestAdapter** reference to **1.8.2**. Processor test packages do not need redeployment for this Windows-side workflow fix. NUnit and DevTools dependency versions are unchanged.

See [Android UI testing](docs/AndroidUiTesting.md) and [continuous integration](docs/ContinuousIntegration.md).