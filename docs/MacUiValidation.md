# Mac UI helper publication readiness

Status: **not ready to publish**. No package or release has been uploaded.

## Implemented

* Loopback-only, bounded Mac2 transport with no input retries.
* Unique active-window accessibility selection, escaped literal selectors.
* Retained XML/PNG and hashes; input intent precedes each click.
* Home/visible-room navigation and fixture-defined binary-control helpers.
* Independent device-state callback and cancellation-safe restoration that keeps
  original and cleanup failures.
* NUnit 5 sample, package integration, offline CI and fresh-package validation.
* Setup, ownership, permissions, IDE/CI, evidence and troubleshooting documentation.

## Evidence, 8 October 2026

39 offline NUnit 5 regressions passed on Windows, and the same 39 passed in a
fresh-cache consumer of the actual preview package. All 11 existing adapter
regressions passed. The explicit room sample discovered without contacting Appium
or hardware. Host wrapper shell syntax, local guide links and Git whitespace
checks passed. The new macOS CI job is prepared but has not run remotely.

Local candidate: `CrestronHomeNUnit.TestAdapter.2.4.0-mac-preview.2.nupkg`.
SHA-256: `80e11dfce8b86bb9c8dbd437634520cb438021613d3d5c53a163d2753690c891`.
It contains both `CrestronHomeNUnit.Mac.dll` and `CrestronHomeNUnit.Mac.xml`.

The existing Mac at the first live attempt had Home 4.12.11 and an active but
locked console. Mac2 did not start within its configured deadline. Original
startup failure was retained outside this repository. The owned service was
stopped and its Appium/XCTest listeners and processes were absent afterward.
The locked-console condition was independently observed. No device control ran.

## Remaining publication gates

* Run the packaged sample on an unlocked Mac, verifying expected processor/app
  identity and navigation restoration.
* Run one complete bound Demo-outlet control case with independent device state,
  UI feedback, restoration, NUnit results and verified host shutdown.
* Record actual tested versions and limitations, then request publication approval.

Physical iPhone/iPad, Configure Pro, unattended locked-screen operation, automatic
saved-Home switching, arbitrary room scrolling and Mac-specific submission-stage
integration are not claimed by this candidate.
