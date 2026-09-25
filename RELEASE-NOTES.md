# Crestron Home NUnit v1.12.2

Saved-endpoint inspection now selects the configured Home's menu in either the grid or list view of My Systems. Previously, list view had no matching menu selector, and multiple grid cards made the global selector ambiguous. The navigator continues to reject duplicate Home names, missing menus and disabled controls.

Validation: 145 Android toolkit tests passed, including grid/list selection and rejection of another Home's menu. A focused hardware check with two saved Homes in list view verified the intended local endpoint and restored Home. This validates endpoint navigation, not physical device controls.

## Updating

Update the Windows CLI/runner distribution or the CrestronHomeNUnit.TestAdapter NuGet package to 1.12.2. Existing Android profiles remain valid. No processor package redeployment is required for this navigation fix.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
