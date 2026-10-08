# Crestron Home NUnit v2.4.0

Adds Mac UI testing helpers to the existing CrestronHomeNUnit.TestAdapter package. The new CrestronHomeNUnit.Mac assembly provides Appium Mac2 transport, exact accessibility selectors, Home/room navigation, retained screenshot and hierarchy evidence, and binary-control testing with independent device-state verification and restoration.

Includes an explicit NUnit 5 sample for Visual Studio, VS Code and CI, a job-owned Mac host wrapper, and setup, permissions, ownership, cleanup and troubleshooting documentation. Mac tests are ordinary phase-two NUnit fixtures; document preparation and submission remain separate. Existing Android, processor and workflow APIs are unchanged.

Validation includes 42 offline Mac regressions and a fresh-package consumer, plus live Demo-outlet control and restoration and the public room sample on an Apple Silicon Mac running Crestron Home 4.12.11. The live tests used a Windows test runner and an Apple Silicon Mac, with the driver installed on a Crestron MC4-R. They verified independent physical state, retained evidence and automation shutdown. See the [validation record](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/MacUiValidation.md) and [Mac testing guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/MacUiTesting.md).

## Updating

Update CrestronHomeNUnit.TestAdapter to 2.4.0 to use the additional Mac APIs. Existing consumers need no API migration. Mac tests require the separately installed Crestron Home app, Appium Mac2 and an unlocked Apple Silicon Mac desktop. Driver-specific bindings and independent state checks remain in the driver's test project. No new driver/client release is needed for these additive test helpers.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
