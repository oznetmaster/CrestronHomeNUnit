# Command-line processor test runner

For the complete local-to-processor development cycle, see the [continuous integration guide](ContinuousIntegration.md), including private settings, gated actual-driver deployment, evidence and optional test-instance removal.

`CrestronHomeNUnit.Cli` is a .NET 10 console application for unattended CI and local scripts. It has no Windows Forms dependency. The Windows runner and CLI share SFTP authentication through `CrestronHomeNUnit.Client`, and share package discovery, authentication handshake, encrypted input transfer and TCP messaging through `CrestronHomeNUnit.Transport`.

## Build and start

```text
dotnet build CrestronHomeNUnit.Cli/CrestronHomeNUnit.Cli.csproj -c Release
dotnet run --project CrestronHomeNUnit.Cli -- --help
dotnet run --project CrestronHomeNUnit.Cli -- packages
```

Supply private processor credentials using `CRESTRON_HOME_USER`, `CRESTRON_HOME_PASSWORD` and `CRESTRON_HOME_SSH_FINGERPRINT`. The fingerprint is SSH.NET's SHA-256 SSH host-key fingerprint, not the HTTPS certificate fingerprint used by CrestronHomeDevTools. Obtain and verify it during trusted processor setup. Alternatively supply `--settings` with a private JSON file containing `Host`, `UserName`, `Password`, and `SshFingerprint`. Environment variables override file values. Password command-line arguments are deliberately unsupported.

## Select and run

```text
CrestronHomeNUnit.Cli suites --processor "DEVELOPMENT-HOME" --package "ExampleDriver Tests"
CrestronHomeNUnit.Cli discover --processor "DEVELOPMENT-HOME" --package "ExampleDriver Tests" --suite unit --results results/discovery
CrestronHomeNUnit.Cli run --processor "DEVELOPMENT-HOME" --package "ExampleDriver Tests" --suite unit --results results/unit
CrestronHomeNUnit.Cli run --processor "DEVELOPMENT-HOME" --package "ExampleDriver Tests" --suite lifecycle --allow-manual --results results/lifecycle
```

`--processor` accepts an IP address or advertised processor name. Package discovery runs on each invocation and repeats while the test service becomes ready. `--wait-ready` bounds this discovery/connection phase (120 seconds by default), within the overall `--timeout`. A refused or interrupted connection is rediscovered before retrying, so changed ports are not reused. Authentication failures, changed processor identity and ambiguous matches stop immediately. Tests are submitted only after a successful handshake and are never automatically replayed. Discovery requires local mDNS reachability; knowing the IP does not remove that requirement in this first version. Duplicate matching packages fail explicitly.

Manual/live suites require `--allow-manual` as well as an explicit suite ID. Repeat `--test` to select exact test names. Explicit NUnit test cases still require explicit selection; the manual-suite flag does not remove NUnit's Explicit semantics. Use `--input path/to/LiveTestSettings.json` to supply a private input file; repeat for multiple files. These inputs use the same encrypted transport as the Windows runner. Do not include them in source control or CI artifacts.

## Results and failures

- `TestTree.xml`: remote discovery result, when using discover.
- `TestResult.xml`: NUnit result XML from a completed run.
- `Summary.json`: machine-readable completion status and counts; created before execution so interrupted runs remain visibly incomplete.
- `Progress.jsonl`: streamed test progress, preserved when the TCP connection closes.

Use a new result directory for each invocation. Test results and output may contain local device data; review which artifacts your CI uploads. No private input files are copied into the result directory.

Exit codes: **0** completed without reported failures, **1** test failure, **2** configuration or connection error, **3** incomplete run/timeout, **4** no selected tests, **130** Ctrl+C cancellation. An all-skipped suite is reported as skipped with its counts; it is not a zero-test run. `--timeout` sets an overall limit in seconds (default 600). Timeout/cancellation sends a cooperative cancellation request before disconnecting. It cannot forcibly terminate a stuck fixture or recover a crashed host process.

## CI and Visual Studio

The CLI needs no interactive desktop or AI agent. Use a CI machine with network access to the processor. A self-hosted runner on the development LAN can build the package, invoke CrestronHomeDevTools to activate it, then invoke this CLI to run tests. Serialize deployment and test jobs against each processor. Ordinary public PR tests can continue using GitHub-hosted runners.

This is not a Visual Studio test adapter. Visual Studio can invoke the CLI as a tool or build task; processor tests will require a separate adapter to participate directly in Test Explorer.

No release or processor deployment happens simply by building this CLI. Configuration-management operations belong to CrestronHomeDevTools; test execution belongs here.