# Shared modern driver packaging

Canonical copies of the identical modern ILRepack and Cecil patch workflows used by Easee, RainPoint, Tesla and the processor SDK. The scripts keep their existing parameters, merge-input order, resource/anonymous-type isolation, indexer repair, manifest ownership and NUnit metadata corrections. Inputs and SDK/tool locations remain explicit caller arguments.

Local forwarding entry points preserve MSBuild callers; copies record source commit and hashes in `build/driver-packaging.json`. The shared merger includes the SDK's explicit resolver for enum-valued attributes in other merge inputs, independent of the caller's directory, and disposes the resolver after writing. The Windows cross-assembly fixture tests this case; native metadata fixtures run on Windows, Linux and macOS. Other merge and patch algorithms remain unchanged. Legacy families require separate migration tests and are not replaced merely because their filenames match.

These are build-only tools. They never deploy, publish, operate a device or alter a frozen public/submission package. The original MIT License with Commons Clause remains in LICENSE.
