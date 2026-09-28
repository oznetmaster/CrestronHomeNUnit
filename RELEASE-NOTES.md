# Crestron Home NUnit v2.0.0

Processor test packages now default to released NUnit 5.0.0 while continuing to target .NET Framework 4.7.2. This is a major tooling update because NUnit 5 changes assertion APIs: await ThrowsAsync, CatchAsync and DoesNotThrowAsync, including before reading returned exception properties. Existing NUnit 4 projects can explicitly set ProcessorNUnitVersion=4.6.1; keep the test project's framework version aligned with its package host.

The embedded runner uses NUnit 5's cooperative cancellation API. It checks dependency expansion before setup and rejects any prerequisite outside the requested suite or selected tests, preventing an excluded Live test from being pulled into an ordinary run.

Framework self-tests use the final NUnit 5 source, retaining documented writable-file and repeated-discovery adaptations. Compiler tests use the exact original public NUnit metadata inside merged packages, with their assertions unchanged. The disposed-event and stream-comparison fixes now come from upstream. A separate unmodified diagnostic suite and its failures remain documented.

Validation: all 523 tooling tests passed. Packaged Windows self-tests passed twice (2,480 passed and 58 skipped each round). Processor rounds had no failed tests: 2,477 passed/8 timing warnings/53 skipped, then 2,482 passed/3 warnings/53 skipped. All 34 compatibility tests passed twice; processor cancellation and recovery passed. Generated consumer packages passed twice with NUnit 5 (five tests) and explicitly selected NUnit 4 (two tests). These results are specific to the tested processor/runtime; warnings and skips are not passing tests.

## Updating

Update the Windows CLI/runner distribution or CrestronHomeNUnit.TestAdapter to 2.0.0, and use the matching source/package tooling. Read https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/NUnit5Migration.md before upgrading existing fixtures. Ordinary local NUnit tests still use NUnit3TestAdapter; Visual Studio and VS Code use the existing VSTest workflow adapter. Existing application drivers do not need to be rebuilt merely to update test tooling.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
