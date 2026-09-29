# Crestron Home NUnit v2.1.1

Release workflows can opt in to reusing a stored, uninstalled release package with `releaseCandidate.reuseVerifiedStoredPackage`. The runner verifies its SHA-256 against the pinned candidate, its local manifest identity, and one exact local catalogue entry before installing a new instance. It rejects newer or ambiguous entries, different bytes, changing catalogue state, and any installed package model or alias. Verification runs under the existing processor lease using pinned SSH; it does not upload, delete, renumber or rebuild the release, or reboot the processor.

The retained `actual-reuse.json` records how the package was verified. The existing deployment receipt explicitly distinguishes verified storage reuse from a new import. Reuse is disabled by default; ordinary Debug builds and release deployments retain their existing behavior. CLI failures now include the exception type without exposing arbitrary exception text.

Validation: all 348 offline workflow tests passed, including 20 stored-release checks covering exact bytes, mismatched identity, ambiguous catalogue entries, unsafe paths, concurrent changes and existing instances. Live activation through the new reuse path is pending; these results do not establish a completed submission rehearsal. NUnit remains 5.0.0, and processor runner binaries are unchanged by this workflow fix.

## Updating

Update the Windows CLI/runner distribution or CrestronHomeNUnit.TestAdapter to 2.1.1. Existing plans need no changes. Enable reuse only for a verified, uninstalled stored release; see https://github.com/oznetmaster/CrestronHomeNUnit/blob/main/docs/ReleaseCandidateTesting.md . Visual Studio and VS Code use the same adapter.

Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
