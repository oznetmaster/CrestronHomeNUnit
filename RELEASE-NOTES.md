# Crestron Home NUnit v2.2.0

Installed-driver UI tests can opt in to preparing the app before asking the operator to be ready. The new `OperatorReadiness` binding selects one exact fixture, private inbox, run and request. The fixture can finish compilation, discovery and navigation before displaying Ready, eliminating the subsequent preparation delay.

The runner measures active-work time monotonically and excludes only that validated, pending readiness wait. An operator can return the next morning without exhausting the action budget. External cancellation still works; malformed or mismatched requests fail closed. The prepared test retains processor and emulator reservations while waiting. Tests must refresh their event baselines after Ready, and the runner requires the retained acknowledgement in addition to all existing evidence and restoration checks. No failed physical attempt is automatically replayed.

The metadata handoff is cleared during discovery and omitted for ordinary plans. Use a cooperating fixture with DevTools 1.24.0 or later for the prepared helper and clock-independent action protocol. Existing plans retain their current behavior. NUnit remains 5.0.0; processor execution and driver code are unchanged.

Validation: the offline workflow suite includes overnight readiness, long scheduler gaps, active-time expiry, cancellation, exact request binding and invalid-record checks. Full hardware rehearsal under this opt-in path remains pending; earlier failed attempts remain unchanged.

## Updating

Update CrestronHomeNUnit.TestAdapter or the Windows runner/CLI distribution to 2.2.0. Adopt prepared readiness only in a new frozen workflow plan. See https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/InstalledDriverTests.md for the contract. Visual Studio and VS Code continue to use the same adapter.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
