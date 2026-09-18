# Test an unchanged installed driver

This command requires **Crestron Home NUnit 1.12.0** and **CrestronHomeDevTools 1.9.0** or later. The adapter declares the DevTools dependency; the Windows CLI download includes it. Isolated NuGet and CLI acceptance covers plan validation, selected-case execution rules, retained evidence and refusal of invalid inputs. A complete hardware run of this dedicated phase remains to be validated; earlier full-workflow hardware runs do not establish that result.

The `installed-tests` phase runs Android NUnit fixtures against a deliberately selected, already-installed candidate. It does not build, install, update, reload, remove or renumber that parent driver. It complements the full deployment workflow: earlier local, processor and deployment results remain separate evidence. A later UI phase does not retroactively make an earlier gate pass.

```powershell
CrestronHomeNUnit.Cli.exe installed-tests --plan "C:\Private\installed-tests.json" --settings "C:\Private\processor.json" --results "C:\Private\Results\new-phase"
```

The settings file supplies `userName` and `password`; `CRESTRON_HOME_USER` and `CRESTRON_HOME_PASSWORD` override them. Keep all settings, input bindings and results out of source control and public artifacts. Use a fresh results directory for each attempt.

## Select the candidate and fixtures

An example private plan (replace all example identities and paths):

```json
{
  "host": "192.0.2.10",
  "certificateSha256": "REPLACE_WITH_VERIFIED_64_HEX_HTTPS_PIN",
  "sshFingerprint": "REPLACE_WITH_VERIFIED_SSH_FINGERPRINT",
  "packagePath": "C:\\Candidates\\Example.pkg",
  "packageSha256": "REPLACE_WITH_INDEPENDENTLY_RETAINED_PACKAGE_SHA256",
  "sourceRoots": ["C:\\Projects\\ExampleDriver"],
  "target": {
    "deviceId": 42,
    "parentDeviceId": -6,
    "name": "Example",
    "model": "Example Platform",
    "locationId": 3,
    "version": "1.002.0003.0000",
    "catalogueId": "example.platform.tcpclient.developer",
    "developer": "Developer",
    "controlType": "tcpClient"
  },
  "androidTests": {
    "project": "C:\\Projects\\ExampleDriver\\ExampleDriver.AndroidTests\\ExampleDriver.AndroidTests.csproj",
    "profilePath": "C:\\Private\\android-session.json",
    "requiredTests": ["ExampleDriver.AndroidTests.ReadOnlyTests.TileShowsCurrentStatus"]
  },
  "timeoutSeconds": 900,
  "leaseWaitSeconds": 0
}
```

The package hash comes from the independently retained candidate/build receipt. Select the exact installed identity and catalogue association from your reviewed installation record and live inventory; do not choose a device merely by a similar display name. Numeric versions may use Crestron's leading zeros. A newer package in the catalogue does not require changing the selected installed candidate.

`sourceRoots` describe the **fixture source being executed**. They may have advanced since the candidate was built. An optional `packageSourceCommit` records the full original candidate commit from its trusted receipt; the phase does not pretend the current fixture checkout is that old production commit. Keep the candidate's original build/deployment evidence independently.

Omit `requiredTests` to require the complete discovered fixture assembly. A provided list selects exact full names and cannot be empty. Discovery, selected and excluded inventories, execution settings and their hashes are retained. Passing selected cases does not claim excluded cases passed.

`androidTests.managedChildren` accepts the same explicit alias, advertised child ID, name, model and room bindings as the [Android workflow](AndroidUiTesting.md). Only declared temporary children are created and removed. Fixtures obtain their actual IDs from the run context and must independently restore device/hub state. Do not label this phase read-only if the selected fixtures or managed-child plan perform changes.

## Verification and interruption

The phase snapshots the candidate and rejects a hash mismatch before acquiring a processor reservation. It reserves the processor first and Android second, then checks the live device's identity, parent, room, developer, control type, loaded version and selected catalogue metadata. It compares the complete extracted file inventory and bytes with the pinned package. It uses fresh live configuration observations on both sides of the file comparison; saved LastKnownGood configuration is not treated as current state.

The same checks run after successful restoration and owned-child cleanup. Fixture source, private Android profile, reservations, discovered cases and complete compiled test-program inventory are checked during the phase. It never attaches to or steals an endurance reservation. Run it before endurance begins or after that reservation is properly released.

These checks establish selected live identity and matching extracted files, **not an attestation of process memory or a continuous driver lifetime**. The installation association supplied by the reviewed plan remains a trusted input. Endurance still needs its separate process-lifetime and functional observations.

`InstalledDriverTests.json` separates test outcome, restoration, child cleanup, candidate verification and reservation release. `Phases.jsonl` records attempted/completed phases and the reservation owner. `BeforeCandidate.json` and `AfterCandidate.json` retain the limited identity/file receipts without dumping credential-bearing configuration properties. Android discovery, TRX, captures, producer hashes and restoration evidence are retained under `AndroidUI`; child journals are under `AndroidManagedChildren`.

A test assertion may fail while restoration and cleanup succeed: that run stays failed but can release its reservations. Interrupted control, missing completion, uncertain restoration or uncertain child cleanup retain reservations for inspection. Cancellation does not authorize replaying physical commands. Check the receipts, actual state and existing reservations before retrying; use a new results directory. Exit zero requires every result condition, including release, to pass. Exit 1 is a completed failure, 2 a preparation/configuration error, 3 retained or unconfirmed reservations, and 130 user cancellation.

The C# entry point is `InstalledDriverTests.RunAsync(InstalledDriverTestPlan, NetworkCredential, resultsDirectory, cancellationToken)`. Fixture authors use ordinary C# NUnit tests and the existing Android workflow session; no private coordinator program or Python code is required.
