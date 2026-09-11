# Stream equality compares stale bytes beyond the amount read from pooled buffers

NUnit 4.6.1 can report two identical three-byte streams as unequal after an earlier comparison of longer unequal streams. This reproduces with the unmodified NuGet `nunit.framework.dll` net462 asset on Windows .NET Framework, without a test host, Crestron, Mono, ILRepack, or shims.

`StreamPoolRepro.cs` is the complete console reproduction. Reference the published NUnit 4.6.1 package, target net472, and run it in a fresh process.

Observed output:

```text
Unequal streams compare equal: False
Identical three-byte streams compare equal: False
```

Expected: the second line is `True`. The executable returns 1 when the defect is reproduced.

## Cause

In `src/NUnitFramework/framework/Constraints/Comparers/StreamsComparer.cs`, `Equal` rents two 4096-byte buffers. It reads data into them, but compares all 4096 bytes rather than just the valid data. The pool returns previously used buffers without clearing them. A previous mismatch at offset 100 therefore survives a later three-byte read and produces a false inequality beyond the end of the new streams.

Our selected framework suite passes on its first execution, then produces four stream-test failures on a second execution in the same loaded assembly. One message identifies offset 1104 in streams whose lengths are both 9 bytes. The console reproduction reduces this to two comparisons through public NUnit APIs.

## Fix considerations

Compare only valid bytes. Handle EOF, unequal lengths, and different short-read boundaries correctly; merely clearing the pool can hide this reproduction without fixing the underlying algorithm. Add regression coverage for a long unequal comparison followed by short equal streams, streams returning different read sizes, and unequal nonseekable streams with matching prefixes.

The source snapshots inspected for v4.6.1 and main contain the full-buffer comparison. The executed reproduction is specifically against the published 4.6.1 net462 DLL; no claim is made here about execution against other package versions or runtimes.

Draft only; not submitted.
