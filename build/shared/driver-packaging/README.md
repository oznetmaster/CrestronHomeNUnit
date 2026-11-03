# Shared modern driver packaging

Canonical copies of the identical modern ILRepack and Cecil patch workflows used by Easee, RainPoint, Tesla and the processor SDK. The scripts keep their existing parameters, merge-input order, resource/anonymous-type isolation, indexer repair, manifest ownership and NUnit metadata corrections. Inputs and SDK/tool locations remain explicit caller arguments.

The source algorithms are unchanged. Local forwarding entry points preserve MSBuild callers; copies record source commit and hashes in `build/driver-packaging.json`. Legacy merge/patch families require separate migration tests and are not replaced with this implementation merely because their filenames match.

These are build-only tools. They never deploy, publish, operate a device or alter a frozen public/submission package. The original MIT License with Commons Clause remains in LICENSE.
