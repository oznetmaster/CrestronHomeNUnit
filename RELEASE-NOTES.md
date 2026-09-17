# Crestron Home NUnit v1.9.0

Identify repeated Android controls by the label beside them. `AndroidSelector.SiblingText` selects a button or value only when exactly one non-password sibling under the same immediate parent has the requested text and belongs to the configured application. It can be combined with an ancestor resource ID.

Missing, duplicate, nested or foreign-application labels cannot select a different row. Multiple matching rows remain ambiguous. Taps still capture fresh bounds, run the fixture's page assertion and send at most one input. Fixture-specific labels, physical actions and restoration remain the consuming project's responsibility.

Validation: the complete Android regression suite passed, including labelled-row selection, invalid or ambiguous targets and uncertain-input handling. An isolated consumer restored the private adapter candidate, used the new API in an ordinary NUnit fixture, discovered the workflow and verified its execution guards. These checks do not establish every consuming fixture's physical behavior or driver certification.

## Updating

Update the UI-test project's **CrestronHomeNUnit.TestAdapter** reference to **1.9.0** to use the new selector property. Existing selectors remain compatible. Processor test packages do not need redeployment for this Windows-side addition. NUnit and DevTools dependencies are unchanged.

See [Android UI testing](docs/AndroidUiTesting.md) and [continuous integration](docs/ContinuousIntegration.md).
