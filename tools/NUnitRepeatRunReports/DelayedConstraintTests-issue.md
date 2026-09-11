# DelayedConstraintTests reuses a disposed static wait handle on a second fixture execution

The `DelayedConstraintTests` self-test fixture creates `WaitEvent` once in a static field initializer, but disposes it in `[OneTimeTearDown]`. Running the fixture again without unloading the assembly reuses the disposed handle.

Affected source: NUnit v4.6.1, commit `b9197a6f17635580a3a397f3eb0f28bddba2e0c7`, `src/NUnitFramework/tests/Constraints/DelayedConstraintTests.cs`. The same initialization/teardown pattern is present in the main-branch source inspected on 10 September 2026:
https://github.com/nunit/nunit/blob/main/src/NUnitFramework/tests/Constraints/DelayedConstraintTests.cs

## Reproduction

1. Load the framework self-test assembly once.
2. Run `DelayedConstraintTests` to completion using `NUnitTestAssemblyRunner`.
3. Create a fresh runner and run that fixture again, retaining the same loaded assembly/AppDomain.

The first run disposes the static event. On the second run, `Delay` calls `WaitOne` on the disposed event. Several tests call it from a separately created `Thread`, so that exception is outside the test invocation's catch boundary. Use an isolated process when reproducing the background-thread failure.

A safe reflection probe against our packaged v4.6.1 test assembly confirms the lifetime defect without starting a thread: instantiate the fixture, obtain its private static `WaitEvent`, invoke `OneTimeTearDown`, instantiate a new fixture, and call `WaitOne(0)` on its event. The new instance exposes the identical disposed event and the call throws `ObjectDisposedException`.

Expected: a fresh fixture execution has a valid event.

## Proposed fix

Initialize `WaitEvent` in `[OneTimeSetUp]`, paired with the existing `[OneTimeTearDown]` disposal. This fixture is already nonparallelizable. Our local adaptation makes that change without modifying its tests or assertions.

## Context and evidence limits

We host NUnit in a long-lived embedded application and run suites repeatedly without unloading their assembly. A processor run lost its TCP connection while this fixture's `CanTestContentsOfDelegateReturningList` was displayed as active. The saved processor log does not confirm the exception responsible for that disconnect; the disposed-handle defect is independently confirmed and should be assessed separately.

The main CI workflow invokes the Cake `Test` target, whose implementation calls `dotnet test` once. I did not find a deliberate second execution of the full framework self-test suite in the same loaded assembly. Internal mock-fixture tests and method-level repetition are not sufficient coverage of this fixture's complete setup/run/teardown/recreation lifecycle.

Draft only; not submitted.
