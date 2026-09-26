# Development and validation history
## 2026-09-26 — NuGet documentation packaging

Audited the downloaded public NuGet archive rather than relying on the GitHub release page. Package README links now use absolute repository URLs where needed. Release packaging requires nonempty NuGet release-notes metadata and checks the finished archive for missing README content, relative links and malformed URLs before publication. Driver manual releases fall back to the checked-in release notes.

Validated newly packed local archives; the original defective published archives fail the new gate. This changes documentation and packaging only. These validation archives were not published and existing NuGet versions remain unchanged. Runtime/hardware tests were not repeated for this metadata change.