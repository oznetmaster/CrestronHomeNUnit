# NUnit 5 migration

Crestron Home NUnit 2.0.0 adopts NUnit 5 after Windows and processor validation. Version 1.12.4 remains the rollback option for the previous tooling generation.

The tooling defaults to the released NUnit 5.0.0 framework. Test projects continue to target .NET Framework 4.7.2 for processor execution; NUnit 5.0.0 supplies a compatible net462 binary. A future NUnit move to a net472 minimum would align with these targets, but still requires processor runtime validation.

## Existing test projects

1. Update the test project to NUnit 5.0.0 and keep it aligned with the processor package's `ProcessorNUnitVersion`. Do not mix major framework versions in a merged test package.
2. Await `Assert.ThrowsAsync`, `Assert.CatchAsync` and `Assert.DoesNotThrowAsync`. Methods that use them generally become `async Task`. Await a returned exception before inspecting it. NUnit.Analyzers 4.15.0 helps identify missing awaits; review automated changes and compile afterward.
3. Replace removed `TestDelegate` and `ActualValueDelegate<T>` uses with `Action` and `Func<T>` as appropriate. Review NUnit's other breaking changes, including platform identifiers and obsolete ordering attributes.
4. Run local tests and package validation, then repeat on the target processor. The runner, CLI and VSTest adapter continue to use the existing wire protocol. No separate VS Code adapter is required.

```csharp
[Test]
public async Task RejectsInvalidInput()
{
    var error = await Assert.ThrowsAsync<ArgumentException>(
        () => client.SendAsync(invalidInput));
    Assert.That(error!.ParamName, Is.EqualTo("input"));
}
```

The embedded host uses NUnit 5's cooperative `StopRun()` API. An explicit NUnit 4 processor package can still select `ProcessorNUnitVersion=4.6.1`; the shared source selects the corresponding cancellation API at build time. This does not promise compatibility with an arbitrary combination of framework versions.

## Dependency selection

NUnit 5 can expand a test selection to include prerequisites declared with `DependsOnTest` or `DependsOnFixture`. That expansion must not cross this host's suite, category or selected-test boundaries. The host checks NUnit's execution plan before setup and refuses any additional test outside the requested filter. Select the prerequisite explicitly within an authorized suite. An ordinary suite cannot acquire an excluded Live test through a dependency.

## Self-test provenance

The production self-test suite retains documented platform adaptations: writable test-file locations, fresh mutable constraint data on rediscovery, and the original public NUnit binary as compiler metadata inside a merged host. Compiler assertions remain unchanged; the metadata reference is not loaded as a second executing framework. The disposed-event fix now comes from NUnit itself.

The separate [unmodified framework diagnostic package](../NUnit5/README.md) retains upstream failures and platform limitations. Its diagnostic results must not be reported as all passing. [Source provenance](../vendor/nunit/PROVENANCE.md) distinguishes the adapted production fixtures from that unchanged upstream copy.

Official references: [NUnit release notes](https://docs.nunit.org/articles/nunit/release-notes/framework.html), [NUnit 5.0.0 package](https://www.nuget.org/packages/NUnit/5.0.0).
