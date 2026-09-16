# Opt-in rollback after an installed-driver test failure

Version 1.5.0 adds an opt-in rollback backend. It restores a known previous package **while preserving current configuration**. It never reapplies an old settings file or refresh token. Existing plans do not roll back automatically.

Rollback is deliberately narrower than a general recovery command. It runs only after the actual-driver upgrade was verified, the installed-driver test stage completed with a failed result, all other stages completed successfully, and remote test execution and physical-state restoration are confirmed stopped. A successful rollback does not make the failed workflow or deployment gate pass.

## Private plan

```json
"rollback": {
  "previousPackage": "C:/Private/PreviousBuild/ExampleDriver.pkg",
  "sha256": "REPLACE_WITH_THE_EXACT_PREVIOUS_PACKAGE_SHA256",
  "configurationProbe": {
    "executable": "C:/Program Files/dotnet/dotnet.exe",
    "workingDirectory": "C:/Private/ConfigurationVerifier",
    "arguments": ["C:/Private/ConfigurationVerifier/Example.ConfigurationVerifier.dll"]
  },
  "verificationChecks": [
    {
      "name": "Previous driver ready",
      "deviceId": 0,
      "useActualDriver": true,
      "model": "EXACT_DRIVER_MODEL",
      "property": "ready",
      "expected": true
    }
  ]
}
```

This supplements a complete workflow plan; the example is not runnable as supplied. `actualDriver.expectedDeviceId` must identify an existing instance. Initial configuration and processor reboots cannot be combined with rollback. The initial implementation supports configured, Loaded, reboot-free Entity V2 roots only.

Before upgrading, the backend locks and retains the previous package, checks its SHA-256, GUID, model and older version, verifies that this version is currently installed, captures the complete root/child identity and room assignments, and asks the configuration probe to establish a compatible configuration identity. Missing evidence prevents the upgrade.

## Configuration verifier contract

Configuration compatibility is driver-specific. A driver can migrate persisted data during startup; a framework cannot infer whether earlier code understands that data. Supply a separately reviewed, read-only verifier for the exact versions and driver family you permit. There is no default verifier and no blanket “assume compatible” option.

The backend appends `--request <file>` and `--response <file>` to the probe arguments. Each request has a fresh `RequestId`, `Phase`, `DeviceId`, `Model`, `InstalledVersion`, `PreviousVersion`, `PreviousPackageSha256`, and `PreserveCurrentConfiguration: true`. Phases are `BeforeUpdate`, `BeforeRollback`, `AfterRollback` and `RestorationConfirmed`.

The probe must freshly inspect the selected driver's current configuration and relevant persisted-data schema. It must exit successfully and create a response of at most 64 KiB:

```json
{
  "RequestId": "COPY_THE_REQUEST_NONCE",
  "DeviceId": 12345,
  "Model": "EXACT_DRIVER_MODEL",
  "InstalledVersion": "1.2.003.0004",
  "PreviousPackageSha256": "EXACT_64_HEX_CHARACTER_PACKAGE_HASH",
  "CompatibleWithPreviousVersion": true,
  "ConfigurationIdentity": "STABLE_64_HEX_CHARACTER_CONFIGURATION_IDENTITY"
}
```

Identity, version, package hash and nonce must match the request. `ConfigurationIdentity` must remain stable across the operation and detect incompatible configuration changes. It is not a constant or a hash of the request. Include connection/account/device identity and relevant persistent schema/settings. Exclude changing credential values only when the verifier specifically establishes that the previous code can read the current credential format and maintain its existing ownership. Never copy or refresh an installed driver's token to perform this check. If masked settings or inaccessible persistent data prevent verification, reject compatibility.

The probe must not control a device, alter configuration, migrate files, rotate credentials or start background work. Keep credentials in private local files rather than command-line arguments. Raw requests, responses, probe logs and recovery evidence remain private; they must not be included in public CI artifacts.

## Recovery sequence and refusal conditions

An eligible failure starts a separate bounded recovery deadline under the same processor reservation and an exclusive execution marker. Before importing and again before swapping, the backend verifies the expected upgraded version, Loaded/configured state, unchanged root and child identities/names/rooms, and current configuration compatibility.

It imports the retained previous bytes once. The processor must report an exact single-instance update scope, the expected old/new versions and no reboot requirement. Submission is never repeated after a missing response. Completion requires both a non-failed operation outcome and the matching `swapDriverCompleted` event with no reboot or reconfiguration requirements. A generic `Ended` response alone is insufficient.

After swapping, the backend verifies the older version is Loaded, the complete captured identity scope is unchanged, the configuration verifier still agrees, and all configured read-only rollback health checks pass. Only then does it clear the execution marker. The normal workflow releases the reservation after its other cleanup requirements are satisfied.

Unknown execution state, incomplete physical restoration, cancellation, transport errors, failed cleanup, missing compatibility evidence, changed instance scope, extra eligible instances, reboot requirements or failed restoration prevent automatic continuation. Uncertain recovery retains the reservation and private evidence for inspection. The backend neither reboots nor deletes/reinstalls the actual driver as a fallback. Failed workflow archives remain available for investigation under the existing cleanup policy.

This is code rollback with current configuration preservation, not a configuration-snapshot restore or disaster-recovery system. Initial-install rollback and V1 reboot rollback are not implemented.

## Validation status

Automated tests cover failure-stage selection, command ordering, durable-intent failures, cancellation, identity/configuration changes, eligibility drift, multiple-instance refusal, lost responses, failed operations and missing/mismatched swap requirements. The real backend passed on a temporary MC4-R Entity V2 test host: captured prior package/configuration, upgrade, fresh compatibility verification, older-package swap, specific completion, restored version/configuration/health, temporary instance/archive removal and reservation release. Failure-stage selection and execution-marker refusal paths were tested automatically. That temporary-host probe did not roll back a production driver or token.

A complete development workflow deliberately failed an installed-version assertion after an actual-driver update, then restored the reviewed prior package. It verified configuration fingerprints, root/child identity and room scope, restored loaded version and health. The original workflow remained failed; temporary resources were cleaned and the lease released. This proves only the reviewed artifact pair. Each driver repository must maintain its own compatibility review and verifier; no unreviewed pair inherits that result.
