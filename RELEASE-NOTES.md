# Crestron Home NUnit v1.8.1

Fix room extension inspection when a named tile is below the initial viewport. The UI automation helper searches the observed service area with bounded scrolling and recognizes the compact room title that replaces the large heading after scrolling. Tile taps remain inside the visible area, clear of the toolbar and bottom navigation.

The search stops on an unchanged or repeated viewport, an ambiguous or disabled tile, an unexpected room title, or an uncertain gesture result. Scrolls are never retried after a lost response. A missing tile fails the inspection and still triggers observed Home restoration.

Validation: the complete Android regression suite passed against both source and an isolated adapter package. Package restore, workflow discovery and execution guards also passed. In the minimized Google emulator, a visible room extension was inspected successfully; a missing tile stopped the search and restored Home under both large and compact heading layouts. The checked device state and inventory were preserved and reservations released. Physical control tests and end-to-end submission acceptance remain separate work; these checks do not establish certification.

## Updating

These are prepared notes for the next patch release; use the GitHub release and NuGet version to determine publication status. Upgrade the separate .NET 10 Android test project to **CrestronHomeNUnit.TestAdapter 1.8.1** when published. Processor test packages do not need redeployment for this Windows-side fix. NUnit and DevTools dependency versions are unchanged.

See [room and nested-page testing](docs/AndroidUiTesting.md#room-and-nested-page-inspection) and [emulator setup](docs/AndroidEmulatorSetup.md).
