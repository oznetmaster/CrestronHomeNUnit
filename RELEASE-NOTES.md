# Crestron Home NUnit v1.5.0

Add an opt-in, guarded code rollback policy for completed failed installed-driver checks. Current configuration is preserved; saved settings and refresh tokens are never replayed.

- Require an exact existing driver target, known previous package and SHA-256, a driver-specific read-only configuration-compatibility verifier, and post-rollback health checks. Initial configuration and reboot workflows cannot enable rollback.
- Capture previous bytes and the complete driver/child identity scope before upgrade. Verify the upgraded version, unchanged identities and configuration compatibility before importing and again before swapping.
- Require a single eligible instance and an explicit reboot-free swap completion. Missing responses, extra targets, changed configuration, unknown execution, incomplete physical restoration and failed cleanup prevent automatic recovery. Commands are never automatically repeated.
- Keep the shared processor reservation and exclusive execution marker until the previous version, configuration identity, scope and health are all verified. Preserve the original failed workflow result even after successful restoration.

Validation: workflow tests cover stage selection, durable-intent failures, cancellation, execution-marker retention, compatibility/identity changes, eligibility drift, lost responses and required swap completion. The real backend passed on a temporary MC4-R Entity V2 host: prior-package/configuration capture, upgrade, older-code restoration, configuration and health verification, host/archive removal and lease release. No production driver or token was rolled back. The complete deliberately failing production-driver workflow was not exercised; each production driver requires its own reviewed verifier.

## Updating

Use **CrestronHomeNUnit.TestAdapter 1.5.0** or the matching runner/CLI ZIP. Existing plans retain their current behavior; rollback defaults to disabled. Official NUnit 4.6.1 and DevTools 1.3.0 are unchanged. Processor test packages remain GitHub-only.

Read the [rollback contract and limits](docs/DriverRollback.md) before enabling it. It is code rollback with current-configuration preservation, not snapshot restoration or disaster recovery. V1 reboot rollback and initial-install rollback are not implemented. Keep plans, package evidence, verifier inputs and recovery logs private.
