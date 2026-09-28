# NUnit framework self-test source

- Repository: https://github.com/nunit/nunit
- Commit: `110e911227972b606241cd5782a7c8a51d2c8f9d`
- Framework binaries: published NuGet package `NUnit` 5.0.0, net462 assets consumed by net472.
- License: upstream `LICENSE.txt` in this directory. The root project license does not replace it.

`framework-tests` contains the upstream Assertions, Constraints, Syntax and TestUtilities directories plus TestText1.txt. `testdata` contains AssertCountFixture, AssertFailFixture, AssertIgnoreData, AssertMultipleData and WarningFixture. `TestUtilities` contains the shared Fakes, SchemaTestUtils, TestBuilder, TestFile, TestSuiteExtensions, PlatformInconsistency and HelperConstraints files from the corresponding upstream locations. `nunit.snk` is the test signing key distributed by NUnit, used to retain friend access to internal APIs in the published framework.

The project explicitly excludes LowTrustFixture (desktop partial-trust AppDomain behavior), InvalidCodeTests (requires Roslyn and dynamic compiler infrastructure), and the unrelated helper CallbackEventHandler. Source remains present for later evaluation. The loaded suite is restricted to the Assertions, Constraints and Syntax namespaces, and does not opt in to explicit tests.

No NUnit framework source is compiled here. Nullable diagnostics CS8632, CS8602, CS8603, CS8604 and CS8618 are suppressed only in the imported self-test project because it omits upstream's annotated BCL references and analyzer configuration for setup-initialized fixture fields. A local IsExternalInit definition disambiguates the friend-visible definitions in the published framework and legacy assemblies.

The original PlatformInconsistency helper is retained: its static extension calls compile with the solution's `LangVersion=latest` and the selected .NET 10 SDK. Formatting follows the upstream EditorConfig.

Local diagnostic addition: `TestUtilities/TestFile.cs` calls `TestFileDiagnostics.Write` after closing each created file. The helper in `CrestronHomeNUnit.SelfTests/Diagnostics` records the path and independent file-access checks to investigate two file-existence failures on the processor. It does not refresh the original FileInfo, change the file, skip tests, or alter assertions.

Local storage adaptation: the `TestFile(string resourceName)` constructor creates a uniquely named file in `TestContext.CurrentContext.WorkDirectory` instead of using `Path.GetTempFileName()`. Processor run 0.1.000.0015 showed that files under `/temp/` could be opened and read while `File.Exists`, `FileInfo.Exists`, and attribute lookups reported them missing. The host sets WorkDirectory to the suite's directory inside the driver's data directory. Explicit file-name overloads and test assertions are unchanged. Processor run 0.1.000.0017 verified the adaptation: both previously failing tests passed, and file existence, attributes, reads, and directory enumeration agreed.


The old local disposed-event adaptation is no longer needed: NUnit 5 includes the upstream fix.
Local repeated-discovery adaptation: CollectionOrderedConstraintTests exposes OrderedByData and InvalidOrderedByData as factories rather than static arrays, so mutable constraint expressions are recreated for each test discovery. Assertions and expected values remain unchanged.

Publication notice update: locally modified NUnit files carry an additional Neil Colvin modification notice without replacing upstream ownership or MIT terms. ConstraintCastingTests.cs received an upstream NUnit copyright/license header only; no executable code changed. The separately copied compatibility shim licenses are documented in THIRD-PARTY-NOTICES.md.

NUnit 5 migration: renamed upstream AttributeExistsConstraintTests.cs to AttributeConstraintTests.cs. The upstream assembly attributes are retained, including Parallelizable on the primary merged host (execution still uses zero workers). Compiler fixtures use the exact original `nunit.framework.dll` embedded as a metadata reference when executing in a merged/internalized host; the runtime continues to use the merged framework. Their assertions are unchanged. `FILES.json` records both upstream hashes and adapted local hashes. The separate `NUnit5/vendor` diagnostic copy remains unchanged upstream source and retains its failure evidence.
