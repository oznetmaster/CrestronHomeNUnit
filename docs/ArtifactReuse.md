# Retaining and reusing workflow packages

Every workflow retains its exact `.pkg` files under `packages/processor` and `packages/actual` in its private results directory. The upload keeps the original filename. Keep the complete directory if you may need the packages and their evidence again.

The optional `artifactReuse` setting permits a later run to reuse those bytes. It never reuses test results: local, processor, live and installed-driver stages run again according to the current plan.

## Prepare reusable evidence

Add this to the private plan before the first run:

```json
"artifactReuse": {
  "previousResults": null,
  "buildInputFiles": [
    "C:/PrivateBuildInputs/DriverSdk.dll",
    "C:/PrivateBuildInputs/ManifestUtil.exe",
    "C:/PrivateBuildInputs/BuildSettings.props"
  ]
}
```

These are placeholders. List the actual external SDK assemblies, build utilities and private compilation settings used by your project. Use an empty array only if all build inputs are covered by declared source roots, the selected .NET SDK and immutable restored packages. Credentials and deployment paths are not compilation inputs and should not be included. All paths must be absolute.

Reuse is an explicit assertion that these inputs cover your build. Arbitrary build scripts can read other files or environment variables; the workflow cannot infer those dependencies. Declare such inputs in a file and include it, or leave reuse disabled. Do not use reuse with floating package versions, mutable dependency feeds or environment-dependent compilation. Include referenced source projects and build scripts in `sourceRoots`.

After a successful run, point `previousResults` at that private results directory. Choose a **new** results directory for the next run. Keep prior evidence locally trusted and private; this is not a mechanism for accepting downloaded packages or untrusted build receipts.

## Verification and fresh-build fallback

The workflow requires a completed successful previous run, actual local and processor test outcomes, and a confirmed released processor lease. Reusing an actual-driver package additionally requires its previous update and installed-driver checks to have passed.

It compares:

- Declared source hashes, including dirty source edits. Generated Debug counters and manifest dates are normalized, but release version components remain significant.
- The .NET SDK selected in the project's directory, runtime/platform, workflow backend binary, restored dependency graphs for the package and its referenced projects, and the external files listed above.
- SHA-256 of the retained package and its driver GUID, model and release version against the current manifest.

The selected package is copied into the new run under a read lock, hashed again, and locked for the remainder of that run. The new package receipt records `ReusedFromRunId`; it does not copy old test results. Build inputs are checked again before deployment.

A changed source/toolchain/dependency identity, older receipt without build-input evidence, or unsupported custom manifest/intermediate-output layout falls back to a normal build. Inconsistent or corrupt evidence stops the run. Evidence files and package sizes are bounded during verification.

The retained version must be newer than every matching model version in the target processor catalogue. Otherwise the workflow builds a fresh Debug revision. An existing catalogue entry alone does not establish which bytes an installed process loaded. Consequently repeated runs on the same processor will normally build again, even after successful archive cleanup, because Home can retain catalogue metadata. Reuse is most useful when moving a verified build to another development processor. It does not remove catalogue records or reboot a processor to force a cache hit.

## Limits

This facility reuses Debug workflow packages; it does not turn them into production Release builds or certify a separately rebuilt release. It does not roll back an installed driver. The first implementation has automated coverage for identity, corruption, incomplete evidence, dependency changes and catalogue conflicts; a cross-processor reuse deployment has not yet been validated on hardware.

Raw workflow directories can contain private test inputs, device identifiers, configuration and diagnostic logs. Do not publish them as GitHub artifacts. See [CI publication boundaries and cleanup](ContinuousIntegration.md).
