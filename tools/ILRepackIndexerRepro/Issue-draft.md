# Overloaded indexers with IndexerNameAttribute lose Property metadata after merging

ILRepack silently drops one Property row when a type defines overloaded indexers with a custom metadata name via `[IndexerName("Lookup")]`. Both accessor methods survive, so direct calls still work, but reflection cannot find the second overload.

Reproduced with `dotnet-ilrepack` **2.0.45 and 2.0.48** on Windows, using the .NET Framework C# compiler and ordinary .NET Framework assemblies. No NUnit, Crestron SDK, shims, post-processing, `/internalize`, or duplicate-handling options are needed.

## Reproduction

The attached `Library.cs`, `Program.cs`, and `Repro.ps1` provide a complete reproduction. Run:

```powershell
pwsh -File Repro.ps1
```

The merge itself is simply:

```text
ilrepack /out:Merged.exe Original.exe Library.dll
```

The relevant type is:

```csharp
public class NamedIndexers
{
    [System.Runtime.CompilerServices.IndexerName("Lookup")]
    public string this[int x] { get { return "one"; } }

    [System.Runtime.CompilerServices.IndexerName("Lookup")]
    public string this[int x, int y] { get { return "two"; } }
}
```

Before merging:

```text
Named properties: 2
Named getters: 2
Ordinary properties: 2
Direct two-argument call: two
Reflected two-argument property: two
```

After merging:

```text
Named properties: 1
Named getters: 2
Ordinary properties: 2
Direct two-argument call: two
Reflected two-argument property: MISSING
```

Expected: preserve both custom-named indexer properties, including their accessor associations and attributes. The ordinary `Item`-named indexer control retains both properties.

## Suspected cause

`RepackImporter.IsIndexer(PropertyDefinition)` returns false unless the property is named `Item` or ends in `.Item`. `CloneTo(PropertyDefinition, ...)` then treats the next same-named property as a duplicate and returns without cloning it. Custom indexer names are legal C# and have parameterized property signatures.

Relevant code: https://github.com/gluck/il-repack/blob/master/ILRepack/RepackImporter.cs#L356-L399 and https://github.com/gluck/il-repack/blob/master/ILRepack/RepackImporter.cs#L725-L738.

A fix should recognize parameterized indexers independently of the conventional `Item` name, while retaining signature comparison for overloads. Regression coverage should include custom-named getter/setter overloads, inherited and explicit-interface indexers, and preservation of property-level attributes.

This was found by NUnit's named-indexer reflection self-tests against a merged assembly. The standalone reproduction does not depend on NUnit.

---

Draft only; not submitted. Public tracker search for `indexer` found historical issues/PRs #62, #64 and #70, but no exact custom-name report was identified in that search.
