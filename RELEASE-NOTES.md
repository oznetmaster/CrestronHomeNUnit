# Crestron Home NUnit v3.0.0

Workflow plans and app-fixture bindings now select processor rooms by `RoomName`. Package provisioning, installed-target checks, managed-child setup and network observers resolve names inside processor-facing test support. Missing or ambiguous names stop before mutation. Workflow plans and reports do not carry native room IDs; existing device identifiers retain their existing meaning.

This is a breaking source and private-plan change. Recompile consumers, replace numeric room selectors with the intended room name, and remove the numeric selector. There is no ID fallback or CI resolver. DevTools 1.27.0 supplies the named processor operations.

Android scenarios share connected-to-the-selected-processor setup and one session with final Home restoration. Recognized app startup connection screens remain unavailable observations until a usable page is observed; they are not gateway status or timing evidence. Cancellable room-extension observations and explicit caller cancellation remain available. Attended physical scenarios can wait without an enclosing elapsed deadline; this does not remove scenario criteria or authorize a physical action.

Workflow transport handling retains bounded child/output settlement and the original process failure diagnostics. Execution and cleanup reserves remain explicit for finite automation phases. Release validation is automated; human-intervention scenarios belong to applicable submission validation, not a publication requirement.

## Updating

Use the published DevTools 1.27.0 dependency. Migrate workflow inputs and recompile test fixtures before activating name-only plans. See the [room-name migration guide](https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/RoomNameMigration.md). Frozen historical runs retain their original pinned tools and evidence.

The normal release checks cover workflow, adapter, Android, client, Mac and transport regressions, packaged runtime compatibility and fresh-package consumers. These automated software checks are distinct from a driver's hardware or Crestron submission evidence. Existing Mac APIs and driver/client runtime package versions are unchanged.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
