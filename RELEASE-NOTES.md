# Crestron Home NUnit v2.1.0

Android workflows can now switch between explicitly permitted saved Homes before running a test phase. Set `AllowedStartingHomes` in each Android profile when sequential phases share one emulator. The workflow selects the expected Home, checks its saved local processor address and port, and returns to Home before exposing the session to tests.

Unknown screens, unapproved starting Homes and ambiguous destinations stop without speculative input. Uncertain input is not replayed or reported as restored. Both profiles must use the same emulator reservation. A matching saved address is connection-settings evidence; it does not replace installed-driver or live-route checks.

Android profile equality now compares permitted Home names by value, so independent JSON reads preserve session and final-cleanup checks.

Validation: 159 Android tests passed. Live navigation switched between two saved processor Homes, verified both saved addresses and ports, and returned to the original Home. The downstream DevTools regression suite passed all 1,680 tests. These checks establish navigation and integration behavior, not a completed driver submission rehearsal. Processor execution remains on NUnit 5.0.0; this release does not change the processor runner.

## Updating

Update the Windows CLI/runner distribution or CrestronHomeNUnit.TestAdapter to 2.1.0. Existing profiles need no changes: without `AllowedStartingHomes`, session opening remains read-only and requires the expected Home already displayed. See https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/AndroidUiTesting.md for profile configuration. Visual Studio and VS Code continue to use the same VSTest adapter.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
