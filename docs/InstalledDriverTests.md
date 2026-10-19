# Test an unchanged installed driver

## Prepared human readiness (2.2.0)

An installed-driver plan may set `OperatorReadiness` with an absolute private `Directory`, exact `RunKey`, unique `Step`, and `Instructions`. This requires exactly one explicitly selected test. The runner completes candidate verification, compilation, discovery and app selection before entering that fixture. A cooperating fixture finishes navigation, then calls `SubmissionPreparedReadiness.WaitAsync` from DevTools 1.24.0 or later. The runner passes the binding in `CRESTRON_SUBMISSION_PREPARED_READINESS` only to execution, never discovery; this variable contains routing metadata, not credentials.

While that exact readiness request remains unanswered, the active-work timeout pauses indefinitely. Cancellation and malformed-request detection remain effective. The test host and processor/emulator reservations stay held to protect the prepared screen; this mode trades resource availability for immediate handoff after Ready. It is opt-in. Existing plans keep their normal bounded timeout.

Every enclosing process and transport must preserve this lifetime contract. A fixed wall-clock SSH command timeout, job execution limit or parent process deadline includes the human wait and can terminate the test after Ready even when the runner correctly pauses its own active-work timer. For prepared-readiness execution, remove that enclosing wall-clock command limit and let the workflow own its bounded active-work deadline, cancellation and cleanup. For example, SSH.NET callers set `SshCommand.CommandTimeout = System.Threading.Timeout.InfiniteTimeSpan` for this opt-in invocation; ordinary commands retain their finite timeout. This does not make the transport durable against disconnection or host failure. If the enclosing process ends, inspect original results and reconcile owned resources; never replay physical actions automatically. Increasing a fixed timeout does not satisfy the unlimited-wait contract.

After Ready, refresh event baselines before arming observation and publishing the action request. Do not repeat compilation or navigation. A reply alone never proves a device event. The runner requires the retained readiness acknowledgement as well as passing tests, restoration, candidate verification and reservation release. A interrupted fixture is not automatically replayed, even though an unanswered readiness record itself remains durable.

Use **Crestron Home NUnit 1.12.1** and **CrestronHomeDevTools 1.13.1** or later. The adapter declares the DevTools dependency; the Windows CLI download includes it. The initial 1.12.0 implementation could reject a valid installed candidate because the payload checker treated its full configuration catalogue ID as a storage-folder name. The corrected checker resolves the unversioned driver key and separate version directory while retaining candidate-version and file-integrity checks.

Isolated NuGet and CLI acceptance covers plan validation, selected-case execution rules, retained evidence and refusal of invalid inputs. A hardware run of the corrected workflow passed selected read-only Android fixtures, candidate verification before and after, declared temporary-child creation/removal, Home restoration and reservation release. This validates those exercised paths; it does not establish that every driver fixture, control operation or recovery scenario has been tested.

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
    "roomName": "Equipment Room",
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

Preparation and phase failures retain `Failure.Phase`, `Failure.ExceptionType`
and a bounded message in `InstalledDriverTests.json`. Processor credentials are
redacted. A failure before a fixture starts is not replaced by a missing operator
readiness acknowledgement. Successful prepared fixtures still require that
acknowledgement. Failure detail does not authorize retrying physical operations;
inspect the phase journal and reservation state first.

## Calculate the enclosing transport budget

Use `WorkflowTransportBudget(preflight, activeWork, evidence, verification, cleanup, margin, preparedHumanReadiness)` to compute `Timeout`, then `ValidateEnclosingTimeout` before launching the remote command. Phase values are explicit operational bounds, not new test criteria. Ordinary execution requires a finite envelope covering every phase; a prepared no-expiry human readiness run requires an indefinite enclosing timeout with cancellation and active-work limits intact. Zero cleanup or margin is rejected. Do not silently reuse the active test timeout as the outer SSH timeout.

For post-update setup, the DevTools `DriverReadinessWait.WaitAsync` helper retains each original readiness observation before evaluating convergence. Verify unchanged settings and payload after readiness; a completed swap followed by transient startup is not permission to swap again. Create fresh scoped clients after human waits/interruption and for independent cleanup.

### Child output and cancellation

`WorkflowEvidence.ProcessAsync` retains original combined output and a sibling `.process.json` receipt containing the child exit, termination confirmation, output completeness and separate primary/cleanup error categories. Child termination and output draining have bounded cleanup waits. A descendant holding an inherited pipe cannot block forever after the direct process exits. Partial output is retained and fails the operation; inspect it before any retry. For nested packaging tests, disable build-server reuse so a successful publish does not leave servers holding output handles.


## Failed startup and observed restoration (local correction)

A startup failure with no navigation attempted is not proof that the configured
Home was observed or restored. AndroidWorkflowSession now retains startup-failure.json
with separate NoNavigationAttempted and ConfiguredHomeRestorationObserved facts.
Failed ordinary opening writes RestorationConfirmed=false. Failed opted-in saved
endpoint inspection may still report true only when its navigation actually
retained verified configured Home restoration. Successful opening behavior and
SavedEndpointVerifiedOnOpen remain unchanged.

Compatibility: a failed read-only opening that previously reported safe solely
because it sent no input now retains ownership. Inspect actual state and use
supported owner-scoped reconciliation; never reinterpret old immutable receipts
or overwrite completion.json. No-input safety and restored-state evidence are
distinct. Cross-Home fixtures must keep the original root session and separate
nested selection evidence. A nested selected-Home completion must not stand in
for actual original-Home restoration before public release.

NUnit TearDown alone does not guarantee cleanup after failed SetUp. Catch setup
failure explicitly and await independently bounded restoration before reporting
the root completion. Do not replay uncertain input. The coordinator must allow
the fixture active period plus all independent SDK/fixture cleanup; cancellation
of WorkflowEvidence.ProcessAsync kills the child tree. An outer SSH-only change
cannot cure a shorter coordinator deadline.

Status: local source correction and offline validation; not pushed or published.

## Admit the Android child within the remaining active budget

For installed-driver Android execution, set AndroidTestPlan.ExecutionReserveSeconds
to the reviewed minimum allowance for the child and its independent fixture/SDK
cleanup. The coordinator checks this reserve at the execution boundary, after
build/discovery, selection and retained producer preparation. It uses the same
monotonic WorkflowActiveDeadline.Remaining allowance; it does not reset or extend
the deadline. Evidence writes are followed by a fresh admission check, so a
stored allowance sample alone is not permission to execute. Insufficient reserve,
cancellation or invalid readiness prevents the execution child from starting.

The default is zero, preserving legacy behavior. Explicit reserves are currently
supported by InstalledDriverTestPlan; generic WorkflowPlan execution rejects a
positive reserve before acquisition because it has no equivalent deadline binding.
A declared reserve must be positive, no greater than one day, and fit within the
installed plan's total active timeout. This is an operational admission guard,
not a new response-time criterion or a guarantee that a fixture will finish.

Keep verification, control/session release, termination/output-drain bounds and
transport margins in the enclosing ledger as well. Include every independent
nested SDK finally and original-Home restoration; a nested selected-Home
completion does not establish restoration of the original Home. Only a validated
pending prepared-readiness request retains the existing pause semantics. Cleanup
or arbitrary waits cannot pause the timer. A watchdog expiry is not cleanup proof.


## Attended physical interaction or observation: no elapsed session deadline

Set `AttendedPhysicalObservation=true`, `TimeoutSeconds=0`, select exactly one
explicit test and leave `ExecutionReserveSeconds=0`. This explicit mode uses no
elapsed timer or readiness-pause monitor; caller cancellation remains active.
Legacy plans keep their finite defaults and continue rejecting zero timeouts.
An unlimited lifetime exposes no finite Remaining allowance and rejects finite
reserve admission. It is not a large finite timeout or a timestamp adjustment.

Every enclosing fixture/human restoration, adapter, worker, SSH and controller
must also have no elapsed session watchdog; an arbitrarily large duration
is not unlimited. Use the transport's supported unlimited mode and an explicit
cancellation handoff that reaches the public caller token. Per-operation ADB/HTTP/SFTP failure bounds remain
separate from the attended session. Explicit cancellation or actual errors
must retain primary/nested diagnostics and uncertain ownership/restoration;
closing an SSH/controller process does not prove a remote worker stopped.

This does not grant physical-action authority or create readiness. Retain the
exact actual human question/reply/provenance and the required baseline/current
state/restoration observations; never answer automatically or replay uncertain
inputs. Release actual unused task-owned resources only after fresh proof.
