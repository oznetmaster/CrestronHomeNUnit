# Crestron Home NUnit v1.12.0

Run selected Android NUnit fixtures against an already-installed driver without rebuilding or redeploying it. The new `installed-tests` CLI command and `InstalledDriverTests.RunAsync` C# entry point verify the selected driver and its extracted package files before and after the test phase.

- Add optional `androidTests.requiredTests` to select exact discovered NUnit case names, including parameter values. Retain the full discovery list and selected/excluded cases; missing, extra, skipped or failed required results fail the stage. Omitting the option continues to require the complete project.
- Verify retained selection settings against hashes captured before execution. Selected runs use producer receipt schema 2; evidence consumers must support and independently verify that selection rather than treating a passing subset as complete-project coverage.
- Reserve the processor and Android session for the dedicated installed-driver phase. Validate the reviewed driver identity, version, catalogue association and pinned package contents, and manage only explicitly declared temporary children.
- Report test outcome, restoration, child cleanup, candidate verification and reservation release separately. Uncertain restoration or cleanup retains reservations for inspection; physical commands are not automatically replayed.
- Raise the adapter/workflow DevTools dependency to 1.9.0 for package-file comparison. NUnit remains 4.6.1 and processor host behavior is unchanged.

Validation: the offline workflow suite and isolated NuGet/CLI acceptance passed, covering selection integrity, installed-phase handling, plan validation and rejection before hardware access. Package checks compared executed assemblies with their archives and used no source-project dependency override. Complete hardware validation of the new dedicated phase remains pending. File and identity checks do not attest running process memory or an uninterrupted driver lifetime.

## Updating

Update **CrestronHomeNUnit.TestAdapter** and the desktop workflow tools to **1.12.0**. The adapter restores **CrestronHomeDevTools 1.9.0** as a dependency; the Windows CLI download includes it. Existing processor test hosts do not need redeployment for these desktop workflow additions.

See [testing an unchanged installed driver](docs/InstalledDriverTests.md), [declared case selection](docs/AndroidUiTesting.md#declared-case-selection-1120-and-later), and [workflow stages](docs/ProcessorTestWorkflow.md).
