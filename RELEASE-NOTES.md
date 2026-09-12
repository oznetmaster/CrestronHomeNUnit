# Crestron Home NUnit v1.0.1

Patch release correcting reconnection after a processor test package restarts or changes port, and preserving dependency types when building processor packages.

## Windows runner

- Rediscover a selected package's current IP address and TCP port before connecting, matching processor identity and package name.
- Automatically attempt rediscovery and reconnection up to three times after a dropped connection. **Find packages** also updates an existing connection when the advertised endpoint changes.
- Preserve previous results during recovery and never rerun an interrupted test automatically. Explicit Disconnect stays disconnected; manually entered endpoints retain their address and port.
- Report missing or ambiguous package identities instead of using a stale discovered endpoint.

## Processor packaging

- Keep each dependency's private `System.SR` resource helper separate, preserving its resource manager and JSON error messages.
- Keep compiler-generated anonymous types separate by assembly, and verify their property names after merging. This prevents unrelated JSON request and response fields from being combined.
- Apply corrections only to temporary merge inputs; original source assemblies and NuGet packages are preserved. The project continues to use official NUnit 4.6.1.

## Updating

Close the Windows runner, extract the complete **CrestronHomeNUnit.Runner-win-x64.zip**, and launch the new copy. The ZIP includes the .NET 10 Windows runtime. Existing settings and credentials remain in the user profile, and the licensed GlyphLab icon is retained in the official build.

The runner reconnection fix works with existing processor packages; it does not require redeployment. To incorporate the merge corrections into your own test packages, update the shared package SDK checkout or pinned revision, rebuild those packages, and redeploy them.

The supplied **CrestronHomeNUnit.Driver.pkg** is the NUnit Test Host in Configure's **Utility** category, version **1.0.001.0000**. Documentation and SHA-256 checksums are supplied as separate assets. The OverkizClient test package belongs to the library-test collection and is not an asset of this release. No NuGet package is published by this release workflow.

## Validation

- Runner and transport regression coverage includes changed ports and addresses, dropped connections, discovery refresh, missing/ambiguous identities, explicit disconnect, preserved results, saved selections, window placement, authentication and protected test inputs.
- The corrected OverkizClient package passed all 233 offline tests twice locally; processor build 1.0.000.0005 passed all 233 offline tests and six live gateway checks, with zero failures or skips, on 2026-09-12.
- Release CI builds the solution, runs the runner/transport checks, and executes the packaged NUnit self-test and language-compatibility suites before publication.

The existing NUnit 4.6.1 repeated self-test stream-comparison limitation remains documented in the user guide.
