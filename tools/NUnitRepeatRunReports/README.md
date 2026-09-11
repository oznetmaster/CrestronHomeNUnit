# Repeated-run investigation — 10 September 2026

The Windows runner has been rebuilt with incomplete-run reporting. Local tests using actual TCP disconnects and host error responses verify that completed tests retain their results, the active test becomes Incomplete, its name remains visible, and output/status diagnostics are saved. No final NUnit XML is invented when the host did not return one. Existing authentication, execution, cancellation and reconnect checks pass.

The self-test fixture's disposed static event is now recreated in OneTimeSetUp. Static constraint test-case arrays now produce fresh data for each discovery. The processor log writer flushes each write. NUnit framework binaries remain the published 4.6.1 package.

Package 0.1.000.0023 builds successfully. Two executions of its selected framework suite in the same loaded assembly on Windows produced:

| Run | Passed | Failed | Skipped |
|---|---:|---:|---:|
| First | 2406 | 0 | 60 |
| Second | 2402 | 4 | 60 |
| C# compatibility, separate process | 34 | 0 | 0 |

The second run completes without the disposed-handle failure. Its four failures are stream equality cases affected by NUnit's pooled-buffer comparison defect. The complete two-pass verification correctly remains failing; these tests have not been suppressed or weakened. This package has not been deployed. The last deployed processor package remains 0.1.000.0021.

The reports were submitted upstream. The files below preserve the original investigation; current discussion is in [issue #5415](https://github.com/nunit/nunit/issues/5415) / [PR #5417](https://github.com/nunit/nunit/pull/5417) for fixture lifetime, and [issue #5414](https://github.com/nunit/nunit/issues/5414) / [PR #5416](https://github.com/nunit/nunit/pull/5416) for stream equality:

- [Disposed event in DelayedConstraintTests](DelayedConstraintTests-issue.md)
- [Stale bytes in StreamsComparer](StreamsComparer-issue.md), with [standalone reproduction](StreamPoolRepro.cs)

The stream reproduction runs against the unmodified NuGet DLL, without Crestron or merging. It incorrectly reports identical three-byte streams as unequal after a previous unequal comparison.

## Connection survival

The current driver hosts test execution and its TCP listener in the same process. Normal exceptions through the test invocation are caught. A fatal process failure necessarily destroys that process's TCP connection; an outer try/catch or AppDomain notification does not provide process isolation.

Crestron's documented driver hosting can group multiple drivers in one host process. Two driver packages therefore do not establish a guaranteed crash boundary. A supported way to isolate the test worker from the TCP server must be established before promising connection survival. Automatic reconnection is a possible recovery feature; it has not been added and would not continue the interrupted run automatically.

The precise cause of the observed processor disconnection remains unconfirmed by its saved log. The disposed event and pooled-buffer defects are independently reproduced.
