# Crestron Home NUnit v1.12.4

Android workflow discovery now accepts NUnit Explicit cases when the plan supplies exact `androidTests.requiredTests` names. Previously, discovery rejected these cases before the explicitly selected tests could execute. Runs without an exact selection continue to reject Explicit cases; ignored and invalid discovery entries remain errors.

The full discovery inventory and selected/excluded partition remain retained. Results must still match the selected cases exactly and pass without skipped cases. This change does not turn a selected subset into complete-project coverage or authorize physical device actions.

Validation: all 328 offline workflow tests passed, including selected Explicit execution and rejection without exact selection. No new physical-device validation is claimed. NUnit remains 4.6.1.

This release also retains the corrected NuGet README links and package release-note metadata published in the 1.12.3 documentation-only adapter patch.

## Updating

Update the Windows CLI/runner distribution or the CrestronHomeNUnit.TestAdapter NuGet package to 1.12.4. Existing Android profiles remain valid. No processor package redeployment is required for this host-side workflow fix.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
