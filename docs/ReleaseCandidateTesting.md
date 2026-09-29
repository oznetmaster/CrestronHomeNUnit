# Test an existing Release package

This opt-in feature requires **CrestronHomeNUnit 1.7.0 or later** and runs the development gates against a supplied actual-driver Release package. Ordinary plans continue to build Debug packages. This handoff has automated coverage; installation and testing of a pinned Release candidate still require hardware validation.

## Prepare the candidate

Build the actual driver once in trusted release CI, retain its original `.pkg` filename and bytes, and record its SHA-256, driver GUID, four-part manifest version and full source commit. The Release manifest version must end in `.0`; Git tags can remain three-part. Obtain these pins from the trusted build record. A hash supplied alongside untrusted bytes does not authenticate their origin.

Check out that exact commit into a clean repository on the hardware worker. Include the repository root itself in `sourceRoots`, plus every other checkout used by the tests. The actual driver project must be tracked inside that repository. Keep the private plan, downloaded artifact and results outside the source checkout. Git and the usual build/test prerequisites must be available to the worker account.

Start with the [driver workflow example](examples/driver-workflow.example.json). Set `actualDriver.packagePath` to the downloaded Release file instead of the Debug output, and add:

```json
"releaseCandidate": {
  "sha256": "REPLACE_WITH_TRUSTED_64_HEX_PACKAGE_SHA256",
  "driverGuid": "REPLACE_WITH_RELEASE_DRIVER_GUID",
  "driverVersion": "1.2.3.0",
  "sourceRepository": "C:/CI/Source/ExampleDriver",
  "sourceCommit": "REPLACE_WITH_FULL_RELEASE_COMMIT"
}
```

The placeholders deliberately fail validation. Keep `actualDriver.project`, instance targeting, live suites and deployed checks: they still identify the source and the installed driver that must pass. Add [Android tests](AndroidUiTesting.md) when UI evidence is required. Use the same `workflow --plan ... --settings ... --results ...` command as a development run.

## Execution and evidence

Before connecting to the processor, the workflow verifies the clean checkout and pinned commit, copies the package into a new `packages/actual` results directory, checks its hash and inspected GUID/version, and holds the retained copy open for reading until the run ends. It preserves the filename and never rebuilds or increases the version of that copy. An existing retained file cannot be overwritten by repeating preparation.

Local tests and the processor test package continue to use Debug builds; project references may compile driver source as part of those tests. They cannot replace the retained Release candidate. The actual Release package is activated only after the required processor gates pass. The workflow rechecks the source commit and its existing source-content digest at subsequent stage boundaries. The initial checkout is checked again around the first digest; subsequent build-generated date/fourth-part Debug revision changes follow the normal digest rules.

`ReleaseCandidate.json` records the verified input identity. `actual-package.json` records its hash, source digest and `ReleaseSourceCommit`. When Android testing is configured, the context, successful capture observations and coverage record also carry the release source commit. Retain these files with the detailed test results and captures in private evidence storage.

An equal or newer version of the same model already in the processor catalogue stops activation. The workflow does not rename, renumber or replace the candidate to bypass this conflict, and it cannot prove that an already loaded same-version driver contains these bytes. Prepare an eligible development processor before testing; do not delete installed production configuration to make a run pass. Repeating a submission uses the retained release artifact with a new results directory, but catalogue eligibility must still be resolved.

### Reusing a verified stored release

CrestronHomeNUnit 2.1.1 adds an explicit `releaseCandidate.reuseVerifiedStoredPackage: true` option. It permits a fresh installation from an existing local package only when its SHA-256 equals the pinned candidate, its local manifest identifies the same GUID/version/model/manufacturer, and exactly one equal-version local catalogue entry exists. Newer versions, ambiguous entries, missing storage, different bytes and catalogue changes during verification stop activation. Any installed instance of the package's model or aliases also stops reuse: stored bytes cannot establish what an already loaded process contains.

Verification runs over pinned SSH while the workflow owns the processor lease. It does not upload, delete, renumber or rebuild the package, and does not request a reboot. Normal instance targeting, configuration and installed-driver tests still run. `actual-reuse.json` records the stored path, package and manifest hashes and catalogue identity; the compatibility `actual-import.json` receipt explicitly reports `VerifiedStoredPackage` rather than claiming a new import. The default remains off. This path has offline regression coverage; live activation validation is still pending.

## Evidence limits

These checks bind a supplied package to declared release pins and tested local source. Trusted CI must establish the pins, source/dependency provenance and worker identity; the supplied hashes alone are not an authenticated build attestation. Optional Crestron submission has additional requirements maintained in [CrestronHomeDevTools](https://github.com/oznetmaster/CrestronHomeDevTools/blob/main/docs/CrestronSubmission.md). Ordinary GitHub/NuGet publication remains possible under the existing hardware-unavailable policy.
