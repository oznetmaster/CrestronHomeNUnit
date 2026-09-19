# NUnit 5 snapshot testing on Crestron Home

This optional diagnostic package tests a **fixed development snapshot**, independently of the stable NUnit 4.6.1 packages. It exists to report results to NUnit, not to maintain a fork or repair upstream failures. It uses the normal Windows runner or CLI and has its own standalone Home tile in the **Utility** category.

The first retained [Windows/Mono comparison report](reports/2026-09-19-beta.1.52.md) includes repeated execution, language compatibility and known packaging limitations.

## Snapshot and scope

- Official MyGet package: **NUnit 5.0.0-beta.1.52**, using its `net462` assembly from `net472` projects.
- Upstream source: [`a45c70a519784f73bba4a685cb30e28077273bbe`](https://github.com/nunit/nunit/tree/a45c70a519784f73bba4a685cb30e28077273bbe).
- NUnit package SHA-256: `ddc4e65d667bdae8c6f9d853995704a82b30b2383d9f9a46ec48372beb824bb8`.
- Compiler setting: `LangVersion=latest`, including the existing C# 13 compatibility fixtures and required net472 shims. This does not imply that every language/runtime combination is supported by Mono.
- Selected upstream **Assertions, Constraints and Syntax** fixtures, their helpers and test data. This is not the whole upstream NUnit test suite. The partial-trust `LowTrustFixture` and unused `CallbackEventHandler` helper are excluded from compilation. Other upstream test projects are outside this package's scope.
- A separate **C# Compatibility** suite links this repository's existing language and async-lifecycle tests.

The scoped `NuGet.Config` obtains NUnit from the [official development feed](https://docs.nunit.org/articles/nunit/getting-started/downloading.html). Other dependencies come from nuget.org. Restoring does not select a newer snapshot. Copied upstream files are unchanged and enumerated in [the provenance record](vendor/nunit/PROVENANCE.md).

## Build and run

Open `CrestronHomeNUnit.sln`. Its **NUnit 5 Snapshot** solution folder contains the test library, compatibility tests, desktop validator and processor project. Build `CrestronHomeNUnit.NUnit5.ProcessorTests` in Visual Studio using the same private SDK and SFTP settings as other processor test projects. It targets **net472 only**. Do not publish this diagnostic package to NuGet.

From a terminal at the repository root:

```powershell
dotnet build NUnit5/CrestronHomeNUnit.NUnit5.ProcessorTests -c Debug -p:BuildProcessorTestPackages=true -p:DeployAfterBuild=false
```

Supply a working ManifestUtil installation through the existing `ManifestUtilExe` property when necessary. Credentials, workstation paths and `.Local.targets`/`.csproj.user` files belong in private local configuration and must not be committed. See [processor build instructions](../docs/ProcessorTestPackages.md) for prerequisites and deployment.

After deploying and installing the package, select **NUnit 5 Snapshot Tests (beta.1.52)** in the Windows runner. Run **C# Compatibility** first, then **Framework Self-Tests**. Run each twice without updating/reloading the package to test repeated execution in the same process. Both suites are also available from the standalone Home tile. No household device inputs are required.

The package deliberately preserves upstream discovery errors. `ProcessorReportInvalidTests=true` retains invalid nodes in discovery XML and `InvalidTests.txt` instead of rejecting this diagnostic build. This is **not a passing test result**. Normal processor packages retain the strict default. Zero discovered tests, loader errors and incorrect expected counts still fail validation.

## Compare Windows and Mono

Build `CrestronHomeNUnit.NUnit5.DesktopValidation`, then run its executable:

```powershell
& ./NUnit5/CrestronHomeNUnit.NUnit5.DesktopValidation/bin/Debug/net472/CrestronHomeNUnit.NUnit5.DesktopValidation.exe ./results/unmerged self-tests --repeat
```

Use `compatibility` for the other suite, `--explore` for discovery only, or append `--merged <path-to-patched-processor-dll>` to exercise the merged package on Windows. Choose a fresh results directory for each comparison. The validator retains the second run even if the first reports failures and returns a nonzero exit code if either fails.

Retain the snapshot/source identity, package hash, compiler version, processor firmware/Mono version, discovery trees, result XML and logs. Separate failures in the unmerged Windows baseline, additional merging/desktop dependency failures, and additional processor failures.

The host serializes execution (`NumberOfTestWorkers=0`). Packaging merges application dependencies, internalizes types and applies existing namespace/metadata repairs. The primary assembly preserves NUnit's upstream `Parallelizable` attribute. Compiler-based tests may encounter platform dependency or internalization limitations after merging; these are not automatically NUnit or Mono defects. Runtime identity is included in diagnostic output.

Do not edit upstream fixtures to make results green. Report verified findings in [nunit/nunit issues](https://github.com/nunit/nunit/issues), checking for existing reports first and explaining these scope and packaging differences. No NUnit or Crestron certification/support is implied. The root [Crestron disclaimers](../README.md) apply.

## Licenses

Harness code is copyright Neil Colvin under the repository MIT license. NUnit retains its original copyright and [MIT license](vendor/nunit/LICENSE.txt), plus upstream notices. Roslyn 5.9.0 and the .NET support libraries retain their MIT licenses and [additional Roslyn notices](licenses/Roslyn-5.9.0-ThirdPartyNotices.rtf). The processor package includes these notices alongside the shared host's notices. Crestron SDK assemblies remain platform dependencies under Crestron's terms.
