// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Transport;

if (args.FirstOrDefault () is "workflow" or "installed-tests")
	{
#if PROCESSOR_WORKFLOW
	return await WorkflowCommand.RunAsync (args.Skip (1).ToArray (), args[0] == "installed-tests");
#else
	Console.Error.WriteLine ("Workflow support was disabled in this build. Build with EnableProcessorWorkflow=true; see docs/ProcessorTestWorkflow.md.");
	return 2;
#endif
	}
try { return await RunAsync (args); }
catch (IOException exception)
	{
	Console.Error.WriteLine (exception.Message);
	return 3;
	}

static async Task<int> RunAsync (string[] arguments)
	{
	if (arguments.FirstOrDefault () is "reserve" or "release") return await ReservationCommand.RunAsync (arguments);
	if (arguments.Length == 0 || arguments[0] is "help" or "--help")
		{
		Console.WriteLine ("""
            CrestronHomeNUnit CLI
              packages                      Find available processor test packages
              reserve --settings private.json --receipt private-receipt.json
                                            Reserve a processor for manual development
              release --settings private.json --receipt private-receipt.json
                                            Release that exact manual reservation
              workflow --plan private.json --results directory [--settings private.json]
                                            Test, deploy, activate and gate a Debug driver update
              installed-tests --plan private.json --results directory [--settings private.json]
                                            Verify an existing candidate and run Android fixtures
                                            without deploying, updating or reloading its driver
              suites --processor IP-or-name --package name
              discover --processor IP-or-name --package name --suite id --results directory
              run --processor IP-or-name --package name --suite id --results directory

            Options:
              --settings private.json     Optional connection settings (Host, UserName,
                                          Password, SshFingerprint)
              --timeout seconds           Overall run timeout, default 600
              --wait-ready seconds        Discovery/connection readiness limit, default 120
              --test full-name            Select an exact test; repeat for more tests
              --input file                Transfer a private input file; repeat as needed
              --allow-manual              Explicitly authorize a manual/live suite

            Credentials: CRESTRON_HOME_USER, CRESTRON_HOME_PASSWORD,
            CRESTRON_HOME_SSH_FINGERPRINT. Environment overrides file values.
            Processor may also come from CRESTRON_HOME_HOST or settings Host.
            Supply the verified SSH SHA-256 host-key fingerprint; passwords are not arguments.
            Every invocation rediscovers package ports. Workflow deploys and can reboot only with explicit plan authorization.
            Results: TestTree.xml (discovery), TestResult.xml, Summary.json, Progress.jsonl.
            Exit: 0 passed, 1 test failure, 2 configuration/connection error,
                  3 incomplete/timeout, 4 zero selected tests, 130 user cancellation.
            A timeout requests cooperative cancellation; it cannot force a hung fixture to stop.
            """);
		return 0;
		}
	using var cancellation = new CancellationTokenSource ();
	ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel (); };
	Console.CancelKeyPress += handler;
	RemoteTestClient? client = null;
	ProcessorLease? lease = null;
	bool executionStopped = true;
	string? connectedHost = null;
	string? requestId = null;
	string? resultDirectory = null;
	try
		{
		var command = arguments[0];
		if (command is not ("packages" or "suites" or "discover" or "run"))
			throw new ArgumentException ("Unknown command. Use --help.");
		var options = new Dictionary<string, string> ();
		var tests = new List<string> ();
		var inputPaths = new List<string> ();
		var allowManual = false;
		for (var index = 1; index < arguments.Length; index++)
			{
			var option = arguments[index];
			if (option == "--allow-manual")
				{
				if (allowManual)
					throw new ArgumentException ("Duplicate --allow-manual.");
				allowManual = true;
				continue;
				}
			if (option is not ("--processor" or "--package" or "--suite" or "--results" or "--settings" or "--timeout" or "--wait-ready" or "--test" or "--input") || ++index >= arguments.Length)
				throw new ArgumentException ("Invalid option. Use --help.");
			var value = arguments[index];
			if (option == "--test")
				tests.Add (value);
			else if (option == "--input")
				inputPaths.Add (value);
			else if (!options.TryAdd (option, value))
				throw new ArgumentException ("Duplicate option.");
			}
		string Required (string name) => options.TryGetValue (name, out var value) && !string.IsNullOrWhiteSpace (value) ? value : throw new ArgumentException ($"Missing {name}.");
		var timeoutSeconds = 600;
		if (options.TryGetValue ("--timeout", out var timeoutText) && (!int.TryParse (timeoutText, out timeoutSeconds) || timeoutSeconds is < 1 or > 86400))
			throw new ArgumentException ("Timeout must be between 1 and 86400 seconds.");
		var readinessSeconds = 120;
		if (options.TryGetValue ("--wait-ready", out var readinessText) && (!int.TryParse (readinessText, out readinessSeconds) || readinessSeconds is < 1 or > 86400))
			throw new ArgumentException ("Readiness timeout must be between 1 and 86400 seconds.");
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource (cancellation.Token);
		deadline.CancelAfter (TimeSpan.FromSeconds (timeoutSeconds));
		var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
		if (command == "packages")
			{
			Console.WriteLine (JsonSerializer.Serialize (await PackageDiscovery.FindAsync (deadline.Token), jsonOptions));
			return 0;
			}
		var settings = options.TryGetValue ("--settings", out var path)
			 ? JsonSerializer.Deserialize<CliSettings> (await File.ReadAllTextAsync (path, deadline.Token), jsonOptions) ?? new () : new CliSettings ();
		string? EnvironmentOr (string name, string? fallback) => Environment.GetEnvironmentVariable (name) ?? fallback;
		var selector = options.GetValueOrDefault ("--processor") ?? EnvironmentOr ("CRESTRON_HOME_HOST", settings.Host)
			 ?? throw new ArgumentException ("Specify --processor or CRESTRON_HOME_HOST.");
		var packageName = Required ("--package");
		var user = EnvironmentOr ("CRESTRON_HOME_USER", settings.UserName) ?? throw new ArgumentException ("Processor username is required.");
		var password = EnvironmentOr ("CRESTRON_HOME_PASSWORD", settings.Password) ?? throw new ArgumentException ("Processor password is required.");
		var fingerprint = EnvironmentOr ("CRESTRON_HOME_SSH_FINGERPRINT", settings.SshFingerprint) ?? throw new ArgumentException ("A verified processor SSH fingerprint is required.");
		if (string.IsNullOrWhiteSpace (fingerprint))
			throw new ArgumentException ("A verified processor SSH fingerprint is required.");
		using (var readiness = CancellationTokenSource.CreateLinkedTokenSource (deadline.Token))
			{
			readiness.CancelAfter (TimeSpan.FromSeconds (readinessSeconds));
			var connected = await new PackageReadiness ().WaitAsync (selector, packageName, async (selected, token) =>
			{
				var identity = await ProcessorAuthentication.AuthenticateAsync (selected.Host, user, password,
						 selected.ProcessorId, _ => fingerprint, (_, _) => { }).WaitAsync (token);
				return await RemoteTestClient.ConnectAsync (selected.Host, selected.Port, identity.Token);
			}, readiness.Token);
			client = connected.Connection;
			connectedHost = connected.Package.Host;
			}
		if (command == "suites")
			{
			Console.WriteLine (JsonSerializer.Serialize (client.Suites, jsonOptions));
			return 0;
			}
		var suiteId = Required ("--suite");
		var suite = client.Suites.SingleOrDefault (s => s.Id == suiteId) ?? throw new ArgumentException ("The selected suite does not exist.");
		if (command == "run" && suite.ManualOnly && !allowManual)
			throw new ArgumentException ("This manual/live suite requires --allow-manual.");
		var inputs = new List<TestInputFile> ();
		foreach (var inputPath in inputPaths)
			{
			if (new FileInfo (inputPath).Length > SecureTestData.MaximumFileBytes)
				throw new ArgumentException ("A private test input exceeds the size limit.");
			inputs.Add (new TestInputFile { Name = Path.GetFileName (inputPath), Content = await File.ReadAllBytesAsync (inputPath, deadline.Token) });
			}
		if (inputs.Count != 0)
			SecureTestData.ValidateFiles (inputs);
		resultDirectory = Path.GetFullPath (Required ("--results"));
		Directory.CreateDirectory (resultDirectory);
		if (File.Exists (Path.Combine (resultDirectory, "Summary.json")))
			throw new ArgumentException ("This result directory already contains a run; choose a new directory.");
		await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Summary.json"), JsonSerializer.Serialize (new TestResultSummary (false, 0, 0, 0, 0, "Running"), jsonOptions), deadline.Token);
		using var progress = new StreamWriter (Path.Combine (resultDirectory, "Progress.jsonl")) { AutoFlush = true };
		var progressLock = new object ();
		Exception? progressFailure = null;
		client.Progress += message =>
		{
			try
				{
				// Test output may contain local device data. Artifacts remain local until explicitly uploaded.
				lock (progressLock)
					progress.WriteLine (JsonSerializer.Serialize (new
						{
						message.Kind,
						message.Text,
						message.Xml
						}));
				}
			catch (Exception exception) { progressFailure = exception; }
		};
		requestId = Guid.NewGuid ().ToString ("N");
		lease = await ProcessorLease.AcquireAsync (connectedHost!, new System.Net.NetworkCredential (user, password), fingerprint, requestId, deadline.Token);
		await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Lease.json"), JsonSerializer.Serialize (new { Host = connectedHost, Owner = lease.Owner, State = "Held" }), deadline.Token);
		var request = new WireMessage
			{
			Kind = command == "discover" ? "discover" : "run",
			Suite = suite.Id,
			RequestId = requestId,
			LeaseOwner = lease.Owner,
			EnableLiveTests = suite.ManualOnly && allowManual,
			TestNames = tests,
			TestInputs = inputs.Count == 0 ? null : inputs
			};
		executionStopped = false;
		var response = await client.SendAsync (request).WaitAsync (deadline.Token);
		executionStopped = response.Kind == "complete";
		if (progressFailure != null)
			throw new IOException ("Could not preserve test progress.");
		if (command == "discover")
			{
			await File.WriteAllTextAsync (Path.Combine (resultDirectory, "TestTree.xml"), response.Xml, deadline.Token);
			var complete = response.Kind == "complete" && response.ExitCode == 0;
			await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Summary.json"), JsonSerializer.Serialize (new
				{
				Complete = complete,
				Operation = "Discovery"
				}, jsonOptions), deadline.Token);
			Console.WriteLine (JsonSerializer.Serialize (new
				{
				Complete = complete,
				Operation = "Discovery"
				}, jsonOptions));
			return complete ? 0 : 3;
			}
		await File.WriteAllTextAsync (Path.Combine (resultDirectory, "TestResult.xml"), response.Xml, deadline.Token);
		var summary = TestResultSummary.FromResponse (response);
		await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Summary.json"), JsonSerializer.Serialize (summary, jsonOptions), deadline.Token);
		Console.WriteLine (JsonSerializer.Serialize (summary, jsonOptions));
		return summary.ExitCode;
		}
	catch (Exception exception)
		{
		var cancelled = cancellation.IsCancellationRequested;
		var incomplete = requestId != null;
		if (incomplete && client != null)
			{
			try
				{
				await client.SendAsync (new WireMessage { Kind = "cancel", TargetId = requestId! }).WaitAsync (TimeSpan.FromSeconds (3));
				}
			catch { }
			}
		if (resultDirectory != null && requestId != null)
			{
			try
				{
				await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Summary.json"), JsonSerializer.Serialize (new TestResultSummary (false, 0, 0, 0, 0, cancelled ? "Cancelled" : "Incomplete")));
				}
			catch { }
			}
		Console.Error.WriteLine (exception is ArgumentException or ProcessorBusyException ? exception.Message : cancelled ? "Cancelled; processor cancellation is cooperative." : incomplete ? "Run incomplete. Inspect saved progress; do not count this as a test pass." : "Unable to connect or configure the run. Check discovery, credentials, SSH fingerprint and local files.");
		return cancelled ? 130 : incomplete || exception is TimeoutException or OperationCanceledException ? 3 : 2;
		}
	finally
		{
		try
			{
			if (lease != null && executionStopped)
				{
				using var cleanup = new CancellationTokenSource (TimeSpan.FromSeconds (20));
				await lease.ReleaseAsync (cleanup.Token);
				if (resultDirectory != null) await File.WriteAllTextAsync (Path.Combine (resultDirectory, "Lease.json"), JsonSerializer.Serialize (new { Host = connectedHost, Owner = lease.Owner, State = "Released" }));
				}
			else if (lease != null) Console.Error.WriteLine ("Processor lease retained because execution has not been confirmed stopped. Inspect results before another run.");
			}
		catch { throw new IOException ("Processor lease release could not be confirmed. Inspect Lease.json before another run."); }
		finally { lease?.Dispose (); client?.Dispose (); Console.CancelKeyPress -= handler; }
		}
	}

internal sealed record CliSettings
	{
	public string? Host
		{
		get; init;
		}
	public string? UserName
		{
		get; init;
		}
	public string? Password
		{
		get; init;
		}
	public string? SshFingerprint
		{
		get; init;
		}
	}