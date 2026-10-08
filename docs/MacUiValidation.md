# Mac UI helper validation for 2.4.0

Status: **validated and approved for the 2.4.0 release**.

## Implemented

* Loopback-only, bounded Mac2 transport with no input retries.
* Unique active-window accessibility selection, escaped literal selectors.
* Retained XML/PNG and hashes; input intent precedes each click.
* Home/visible-room navigation, including the app restoring its previous room.
* Fixture-defined binary controls, independent device-state callbacks, and
  cancellation-safe restoration preserving original and cleanup failures.
* NUnit 5 sample, package integration, offline CI and fresh-package validation.
* Setup, ownership, permissions, IDE/CI, evidence and troubleshooting documentation.
* Job-scoped idle-sleep prevention and verified owned-service shutdown.

## Evidence, 8 October 2026

42 offline NUnit 5 regressions passed on Windows, and the same 42 passed in a
fresh-cache consumer of the actual preview package. All 11 existing adapter
regressions passed. The explicit room sample discovered without contacting Appium
or hardware. The new macOS offline CI job is prepared but has not run remotely.

Pre-release validation candidate: `CrestronHomeNUnit.TestAdapter.2.4.0-mac-preview.4.nupkg`.
SHA-256: `86230f1ab4f73de5be1e7ee3fbfafd7f489ff72962869e3fcb9e915d7bdbb70a`.
It contains `CrestronHomeNUnit.Mac.dll` and its XML documentation.

### Live control

At 20:16 BST / 19:16 UTC, the NUnit fixture running on HPNEIL passed against the
preview package and the Crestron Home app on the Apple Silicon Mac. It used the
existing Kasa/Tapo 2.1.2 installation on the development MC4-R (.244), bound the
Demo KP115 child to its independently queried physical device identity, changed
ON to OFF, and restored ON. Direct plug readings and Mac UI values agreed. The
app returned Home. Processor reservations were released and the owned Mac
Appium/XCTest service shut down with its listeners closed. No processor restart,
network interruption or driver deployment was required.

Private evidence retains the NUnit/TRX result, initial state, input intents,
XML/PNG hashes, changed/restored state and session-close records. The live run
identity is `424e82b3c75d401b9b22eaf27eb9fa3a`. This validates these UI helpers; it
is not a new complete driver submission suite or byte-for-byte installed-driver
certification. Installed version and child identity were queried; the recorded
candidate hash refers to the existing release artifact.

Observed environment: Home 4.12.11, macOS 27.0, Xcode 27.0, Appium 3.8.0,
Mac2 4.3.5 and Node 24.21.0. Tests ran with a logged-in, unlocked Mac console.

### Public sample

At 20:18 BST / 19:18 UTC, the unchanged public `RoomTests.cs` sample also passed
as an explicitly selected NUnit 5 test against the actual package on HPNEIL,
with the Mac UI and .244 processor reserved. It inspected Office and returned
Home; session disposal, reservation release and owned-service shutdown passed.
Its run identity is `d500c52060e2481e9c5c771caa621025`.

Both successful NUnit/TRX results and all 38 retained capture inventories
(including result attachments) were checked against their XML/PNG SHA-256 values.
A private local archive preserves the two runs; screenshots and household/device
identifiers are excluded from the public repository.

### Retained failures and fixes

The first startup attempt encountered a locked Mac console; no control ran.
A later control attempt successfully restored the plug but the enclosing test
failed because Rooms reopened its previous detail page, and the private fixture
disposed its HTTP transport before its session. Those original results remain
retained. Navigation now handles both the requested previous room and another
previous room, covered by two new regressions; the fixture lifetime was corrected.
The complete repaired control/navigation run then passed.

## Publication scope

Minor release: **2.4.0**, approved by the owner on 8 October 2026. Normal release
checks also validate the stable package before publication. These helpers belong to phase two. Document preparation, signing and
submission remain in phase three. No driver/client release is required for this
additive tooling change.

Physical iPhone/iPad, Configure Pro, unattended locked-screen operation, automatic
saved-Home switching, arbitrary room scrolling and Mac-specific submission-stage
integration are not claimed by this candidate. The existing Android workflow
manifest is unchanged; consumers use an explicit NUnit project for Mac tests.
