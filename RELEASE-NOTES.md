# Crestron Home NUnit v2.1.2

Exact Android test selections can now run a mixture of ordinary and Explicit cases. Previously, NUnit adapter 6.3.0's Current execution path omitted the selected Explicit cases when ordinary cases were included. The workflow coverage check rejected the incomplete results, but manual-event tests never reached their operator prompts.

Selected execution now uses the adapter's documented Legacy discovery mode and Relaxed explicit mode with the same generated, pinned exact-name filter. The initial full inventory discovery stays in Current mode. Unselected ordinary and Explicit tests remain excluded. Runs without an exact selection still reject Explicit cases before execution. No device authority, physical prerequisite, restoration check or coverage requirement is relaxed.

Validation: all 349 offline workflow tests passed. A real-adapter regression reproduced the omission before the fix and passed afterwards for ordinary, all-explicit and mixed selections, including duplicate names, filter-like characters in names, and deliberately failing unselected ordinary and Explicit cases. No physical devices were used by these regression tests. Live manual-event execution and a complete submission rehearsal remain to be verified. Earlier incomplete runs remain failed; this release does not rewrite their evidence. NUnit remains 5.0.0 and the processor runner implementation is unchanged.

## Updating

Update the Windows CLI/runner distribution or CrestronHomeNUnit.TestAdapter to 2.1.2. No plan changes are required. Adopt the release in a new pinned workflow attempt; keep completed and failed attempts on their original tool versions. See https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md for exact selection and evidence requirements. Visual Studio and VS Code use the same adapter.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
