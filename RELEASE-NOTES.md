# Crestron Home NUnit v1.12.1

Fix installed-driver test phases that stopped at candidate verification despite a matching installed package. The workflow, CLI and Visual Studio adapter now consume CrestronHomeDevTools 1.13.1, which resolves full processor catalogue IDs to the correct extracted-payload directory and checks their version against the candidate.

Validation includes the workflow and adapter regression suites, a build against the published dependency package, and a hardware run of selected read-only Android fixtures. The hardware run verified the unchanged candidate before and after testing, restored Home, removed its declared temporary child and released both reservations. It does not establish coverage of every control or recovery path.

NUnit remains 4.6.1. No processor host behavior or driver under test is changed by this patch. See [installed-driver testing](docs/InstalledDriverTests.md) for the supported scope.

## Updating

Update the Windows CLI/runner distribution or the CrestronHomeNUnit.TestAdapter NuGet package. Existing test plans remain valid. No processor test-package redeployment is required for this dependency fix.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
