# TCP protocol 2 and package discovery

Packages bind TCP port zero by default, so the processor assigns an available port. `ProcessorTests.json` can specify a fixed port when necessary. The Home tile shows the actual bound port.

Each package advertises `_crestron-nunit._tcp.local` using standard mDNS on UDP 5353 (IPv4 multicast 224.0.0.251). PTR identifies an instance, SRV supplies its assigned TCP port, A supplies IPv4 addresses, and TXT supplies `name`, `processor`, `host` and `protocol=2`. Neither credentials nor test settings are advertised. The runner uses a five-second browse, merges split DNS responses, groups its sorted package list by processor, and resolves current endpoints afresh on each search. Multicast is local to the network segment unless the network provides an mDNS reflector. Manual IP/port connections remain supported.

## Processor authentication

The Windows runner signs in over SFTP on port 22 using the processor's existing username and password. It retrieves `/user/Data/CrestronHomeNUnit/ProcessorIdentity.txt`, containing a persistent random processor ID and internal connection token. All test packages on that processor share this file. A file lock and atomic publish prevent concurrent package starts from creating different identities. The directory must be writable by the driver and readable through authenticated SFTP. It is outside individual driver/version directories and is not included in any package.

Users do not enter or copy a test pairing key. The runner signs in over SFTP for each new package connection, then uses the retrieved token for the TCP challenge/response. The token is not sent in plaintext or included in mDNS. Credentials are remembered per processor in Windows DPAPI storage under `%LOCALAPPDATA%\CrestronHomeNUnit\ProcessorKeys.dat`. SSH host fingerprints are trusted on first successful use and pinned for later connections, including a known processor discovered at a new address. A changed fingerprint must be investigated; deleting this local vault resets saved credentials and host trust. The token remains valid until the processor identity is rotated and all packages are restarted; changing the SFTP password alone does not invalidate previously obtained tokens.

`PrepareDesktopRunner.ps1` imports the existing locally excluded deployment credentials into this Windows store. Its `%LOCALAPPDATA%/CrestronHomeNUnit/Runner.local.json` contains only host, initial port and username. Settings patterns are in `.git/info/exclude`, not added to tracked `.gitignore` policy.

## Framing and handshake

Every frame has a four-byte unsigned network-order length followed by UTF-8 JSON, with a maximum payload of 16 MiB. Reads handle fragmented and adjacent frames. Oversized, truncated or invalid messages close the connection.

The client sends `hello` with `Version=2` and a random nonce in `Text`. The server replies `hello-challenge` with its random nonce in `Text` and a server proof in `Xml`. Both derive session material from the processor token and both nonces. The client verifies the proof, then sends `hello-auth` with its client proof in `Text`. The server verifies this and returns `hello-ok` with the available `Suites` catalog, including `ManualOnly` metadata. See `SecureTestData.cs` for byte-level derivation and encoding. Protocol-1 handshakes are rejected by updated hosts.

| Client kind | Behavior |
| --- | --- |
| discover | Returns complete with NUnit discovery XML for the requested suite. |
| run | Runs the requested suite; an empty TestNames means the suite, otherwise the listed full names within its configured filter. |
| cancel | TargetId identifies that connection's active request. Returns cancelled as acknowledgment; the original run later completes. |

Messages carry a unique `RequestId` and suite ID. Optional `ProtectedData` contains authenticated encrypted input files. These use AES-CBC with an independent HMAC-SHA256 key; the tag binds ciphertext to the session, request kind, request ID and suite. Maximum inputs: 16 files, 1 MiB per file and 4 MiB total. Plain filenames only; paths, duplicates and reserved names are rejected. An omitted payload preserves existing files. An explicit empty list clears them. Ordinary names, results and output are not encrypted.

During discovery/execution the server emits `started`. Execution emits `test-start` and `test-finish` with NUnit XML, and `test-output` with live text. Each carries the original request identifier. Final `complete` carries XML, a readable summary and an exit code; host failures use `error`. A dropped connection produces an incomplete run, preserving already received results.

Home and desktop operations share one execution guard per package. Cancel and disconnect request cooperative NUnit cancellation. Up to four clients are accepted; writes have a five-second timeout and the initial handshake has a ten-second timeout. Tests have no arbitrary transport deadline. Processor files remain in `TestResults/<suite>`, with settings under `Inputs`; stale result XML is removed before each operation.

Crestron's native SSH advertisements can help find candidate processors without installed test packages. The tested processor advertises `_ssh._tcp.local` and `_sftp-ssh._tcp.local`; these generic services alone do not establish that a device is a Crestron processor. The first runner implementation browses the test-package service.

The Windows runner also queries native Crestron AutoDiscovery on UDP 41794 to obtain configured processor names without credentials. Those names replace generic mDNS hostnames in the package list when available. Unknown processors prompt for their own SFTP credentials; changing the processor address clears unrelated credentials. This first version lists installed test packages, rather than processors with no packages.


Manual suites require the per-request `EnableLiveTests` flag. The runner sets it for the selected manual suite on discovery/run, and the host supplies the matching NUnit parameter only for that operation. Runs without an explicit opt-in are rejected before fixtures execute. Input-payload authentication includes this flag when true; false preserves the previous context encoding. Uploaded settings are not rewritten to enable tests.