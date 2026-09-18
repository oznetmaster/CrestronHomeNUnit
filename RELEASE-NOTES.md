# Crestron Home NUnit v1.11.1

The Android workflow now preserves and verifies the complete test program used for a run. Previously, evidence consumers could identify the main fixture assembly without detecting a changed dependency or runtime setting.

- Record `producer-manifest.json` and a run-bound `producer-pin.json` after discovery and before execution. The inventory covers all retained assemblies, dependencies, runtime settings, resources and discovery output.
- Compare the retained files, manifest, receipt and discovery against coordinator-held reference hashes after execution. Changed, missing or added files stop the stage before it can report a passing result, including when a dependency and its manifest are replaced together.
- Bound inventory size and reject links, junctions and ambiguous paths. Fixtures must write results to their private evidence directory, leaving the retained `assembly/` directory unchanged.
- Extend isolated NuGet acceptance to execute the dependency-integrity regressions against the packaged Workflow assembly and verify its bytes against the release archive.

Validation: the full offline workflow suite and submission-audit suite passed. Isolated package checks exercised Android regressions, workflow integrity checks, adapter discovery, ordinary NUnit consumption and refusal of missing private configuration. Cross-language verification consumed a manifest from the actual .NET implementation using the DevTools Python auditor and rejected an altered NUnit dependency. These checks used no processor, emulator or physical-device commands.

These are retained-file integrity checks, not worker authentication or proof that arbitrary fixture code executed honestly. Independent submission auditing still requires trusted pre-execution pins, complete applicable test coverage and device-state restoration. Old evidence cannot gain a pre-execution inventory retroactively.

## Updating

Update **CrestronHomeNUnit.TestAdapter** and the desktop workflow tools to **1.11.1**. Existing processor test hosts do not need redeployment for this desktop evidence fix. NUnit remains 4.6.1; the adapter/workflow's minimum DevTools dependency remains 1.6.0.

For the independent offline audit, use `tools/submission/audit_android.py` from the **CrestronHomeDevTools v1.8.0 source tag or later**, including its required `--producer-manifest-sha256` argument. The Python auditor is distributed in that source tag, not in the DevTools NuGet package.

See [retained test program integrity](docs/AndroidUiTesting.md#retained-test-program-integrity) and [workflow stages](docs/ProcessorTestWorkflow.md).
