# Installed-driver control tests

An optional `deployedControls` array extends the private workflow plan. These checks run after the actual driver update and read-only installed-driver checks, through the same CLI and Visual Studio workflow. Existing plans remain read-only. Use only devices explicitly selected for development testing.

Each check captures the physical state, submits one absolute command through the installed driver, waits for the driver's background operation to finish, and verifies both physical state and Home's reported state. It then restores and independently verifies the original state. The independent probe is read-only and uses the underlying device API, not Home's cached properties.

The processor reservation and exclusive execution marker cover the complete control/restoration sequence. A command with an uncertain outcome is not repeated. Cancellation starts a separate bounded restoration window; the runner waits for confirmed command completion before restoring. If completion, identity or restoration cannot be confirmed, it retains the reservation and original-state evidence for deliberate recovery. Control cleanup does not reboot or roll back the driver. A separate optional [rollback policy](DriverRollback.md) can run only after physical-state restoration has been confirmed.

## Required driver diagnostics

The selected child must expose its stable physical identity and a single JSON-string activity property containing:

```json
{"Epoch":"unique-per-entity-lifetime","Completed":12,"Pending":0}
```

`Pending` must increment synchronously before dispatching asynchronous work and remain positive through every retry. `Completed` increments exactly once after that work ends, whether it succeeds or fails. Publish the counters together atomically. A new entity/process gets a new epoch. The workflow requires precisely one new completion and no pending work for each submitted command. A restart or another overlapping command prevents successful attribution. These diagnostics establish completion; only the independent probe establishes physical success.

KasaTapo outlet entities provide `controlDeviceId` and `controlStatus`. The identity is the discovered device ID, followed by `/childDeviceId` for a power-strip outlet. Use the absolute `outletOn` and `outletOff` commands; `outletIsOn` supplies the additional Home check. This pair has passed physical control/restoration validation on an MC4-R development processor. The same processor rejected the parameterized `setOutletIsOn` request internally despite returning an HTTP success response, so that setter is not the validated KasaTapo route. The completion and independent observation checks catch this distinction.

## Private plan example

The IDs below are placeholders. Resolve the current installed device, model and ancestry under the workflow's actual driver before filling them in. The runner rechecks those identities before every command and observation.

```json
"deployedControls": [
  {
    "name": "Outlet power and restoration",
    "deviceId": 12345,
    "model": "EXACT_INSTALLED_MODEL",
    "physicalIdentity": "DEVICE_ID/CHILD_ID",
    "booleanCommands": {"true": "outletOn", "false": "outletOff"},
    "stateProperty": "outletIsOn",
    "invertBoolean": true,
    "timeoutSeconds": 120,
    "restoreTimeoutSeconds": 120,
    "probe": {
      "executable": "C:/Program Files/dotnet/dotnet.exe",
      "workingDirectory": "C:/Private/TestTools",
      "arguments": ["C:/Private/TestTools/KasaTapoCrestronDriver.ControlProbe.dll",
                    "--settings", "C:/Private/LiveTestSettings.json", "--role", "strip"]
    }
  }
]
```

Choose `invertBoolean` or a scalar `testValue`, never both. For boolean state, `booleanCommands` names separate absolute true/false commands with no parameters. Both must be advertised by the same selected device. Alternatively, `command` and `parameter` describe one absolute scalar setter; do not combine the two forms. Parameterized setters need device-specific hardware validation before use. The runner never assumes that HTTP success proves execution. A fixed test value equal to the captured state fails before sending a command: an unchanged device does not prove control. The initial implementation requires observed, restored and Home values in the setter's units. Normalize units explicitly inside the probe; do not assume brightness or temperature scales match. This contract is for absolute scalar setters, not toggle/pulse commands or devices that cannot report a state suitable for restoration.

## Independent probe protocol

The runner launches the configured executable without a shell, appending `--request <file>` and `--response <file>`. The request contains a fresh `RequestId` and the planned `PhysicalIdentity`. The probe must freshly read that exact device and create the response file with:

```json
{"RequestId":"request-nonce","PhysicalIdentity":"exact-identity","Value":true,"RestoreValue":true}
```

Values must be scalars in command units; for this contract `Value` and `RestoreValue` must agree. The response is limited to 64 KiB, its nonce and identity must match, and the process must exit successfully. A previous response cannot satisfy another observation. Two consecutive independent readings plus matching Home readings are required. The probe must honor process termination and must never send control commands or background work.

Store plans, settings, request/response files and probe logs privately outside tracked source and public artifacts. Credentials belong in private settings files, not command-line arguments. The public NUnit result includes the named test and fixed diagnostics; private transition files preserve original state, control intent, restore intent, last observed activity counters, command mapping and confirmed restoration. A `RecoveryRequired` result or an interrupted intent requires inspection before releasing the processor lock.
## Additional validated route

The Wiser driver now has a read-only room probe for Auto → Manual → Auto. Hardware validation used the existing installed room tile, confirmed the hub’s `FromManualMode` origin and unchanged manual setpoint, then independently verified restoration to its assigned schedule with guarded settings unchanged. Rooms without an existing restorable manual setpoint are refused before control. See [Wiser installed-room controls](https://github.com/oznetmaster/WiserHeatCrestronDriver/blob/master/docs/InstalledRoomControls.md). Existing user tiles are retained; commissioning a platform room does not create an independent test thermostat.
