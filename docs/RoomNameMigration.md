# Room-name migration for all test consumers

## Contract

The 3.0.0 workflow adapter requires DevTools 1.27.0. Author-facing workflow plans, installed-driver targets, managed-child requests and app-fixture bindings use `RoomName`, never a native room selector. For example, `testPackage.roomName` may be `Equipment Room`, while `actualDriver.roomName` may be `Office`. These names are selected intentionally for the actual test; they are not universal defaults.

Remove `locationId`/`roomId` from those inputs. Do not translate a numeric selector in CI, serialize a resolved plan or emit a room-binding report. Only processor-facing test support resolves a name, using the current processor's Room locations. Ordinary name matching ignores case; multiple matches (including case variants), missing names and non-Room locations are rejected before mutation. Existing device, parent, catalogue and managed-device identifiers are unaffected.

`PackageBuildPlan`, `InstalledDriverTestTarget`, `AndroidManagedChildPlan`, `AndroidManagedDeviceBinding` and `NetworkLinkObservationPackage` now expose the room name. Source consumers of their previous numeric constructors must recompile. Lower native configuration operations still use native identifiers internally; retaining those low-level APIs does not provide a workflow fallback. Native processor-private commissioning/restoration journals can contain the original device data required for safe cleanup; they are not author-facing workflow plans or name-to-ID reports.

## Normal publication order

1. Publish DevTools 1.27.0 through its existing release workflow. Its additive core can coexist with the existing Automation consumer of adapter 2.3.0. Do not migrate that consumer prematurely in the bootstrap release.
2. Verify the public core package in a fresh cache. Both NUnit Workflow and TestAdapter defaults require 1.27.0. Release adapter 3.0.0 through the normal release workflow, which runs packaging and clean-consumer checks.
3. Update maintained workflow/fixture package references and name-only private plans together. Verify the actual package bytes and discovery before normal resource admission. Frozen historical evidence is unchanged. An independent library only needs its own compatible test consumer and ordinary gate; unrelated driver releases are not required.
4. Migrate the DevTools Automation/SubmissionTests consumer and publish its normal follow-up release after the adapter is public. Do not add a core-only publishing workflow or an ID compatibility bridge.

The release workflows own version/changelog commits. Preparing release notes does not publish a version; do not pre-bump the checked-in version or pre-insert the workflow-generated changelog entry.

## Validation

The maintained room-contract suite covers the same named plan on two processors with different native identifiers, missing and ambiguous names before commands, wrong assignment, cancellation while lookup is pending, all managed rooms verified before the first child mutation, and names-only public serialization. The package acceptance script compiles and runs its actual generated named managed-child fixture against packaged assemblies. No processor is contacted by those checks.

Run the normal complete release validation and fresh-cache adapter acceptance after the public DevTools dependency exists. Local project-override builds are development evidence only. Do not label a local candidate or same-version cached package as the verified public release.
