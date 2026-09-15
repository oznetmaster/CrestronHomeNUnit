// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Net;
using System.Reflection;
using System.Text.Json;

using CrestronHomeDevTools;

using CrestronHomeNUnit.Workflow;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class RollbackSwapTests
	{
	[TestCase ("valid", 1, true)]
	[TestCase ("multiple", 0, false)]
	[TestCase ("other-id", 0, false)]
	[TestCase ("wrong-installed", 0, false)]
	[TestCase ("wrong-available", 0, false)]
	[TestCase ("reboot", 0, false)]
	[TestCase ("unsupported", 0, false)]
	[TestCase ("drift", 0, false)]
	[TestCase ("lost-submit", 1, false)]
	[TestCase ("failed", 1, false)]
	[TestCase ("missing-completion", 1, false)]
	[TestCase ("reconfigure", 1, false)]
	[TestCase ("unexpected-reboot", 1, false)]
	public async Task RealSwapPathRechecksScopeAndRequiresSpecificCompletionWithoutRetry (string mode, int submissions, bool succeeds)
		{
		var directory = Path.Combine (Path.GetTempPath (), "rollback-swap-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (directory);
		try
			{
			var path = Path.Combine (directory, "previous.pkg");
			File.WriteAllText (path, "unused by swap-only test");
			var connection = new Connection (mode);
			await using var client = new ConfigurationClient (connection);
			var old = new DriverPackageInfo (Guid.NewGuid ().ToString (), "Example", "Example", "1.0.0.1");
			var next = old with
				{
				Version = "1.0.0.2"
				};
			var plan = new DriverRollbackPlan (path, new string ('A', 64), new (path, directory, []), []);
			var type = typeof (WorkflowRollback.Prepared);
			await using var prepared = (WorkflowRollback.Prepared)Activator.CreateInstance (type, BindingFlags.Instance | BindingFlags.NonPublic,
				null, [client, plan, new PackageBuildPlan (path, path, "Tests", 5, 12), old, next, directory, path, "unused.invalid", new NetworkCredential (), "pin", TimeSpan.FromSeconds (1), new FileStream (path, FileMode.Open, FileAccess.Read, FileShare.Read)], null)!;
			type.GetField ("_imported", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (prepared, new DriverDeploymentResult (old, plan.Sha256, "previous-id", "Complete", true));
			if (succeeds)
				await prepared.SwapOnceAsync (CancellationToken.None);
			else
				Assert.CatchAsync (async () => await prepared.SwapOnceAsync (CancellationToken.None));
			Assert.That (connection.Submissions, Is.EqualTo (submissions));
			Assert.That (File.Exists (Path.Combine (directory, "SwapCompleted.json")), Is.EqualTo (succeeds));
			if (succeeds)
				Assert.That (connection.CompletionWaited, Is.True, "Generic Ended status alone cannot confirm a swap.");
			}
		finally { Directory.Delete (directory, true); }
		}

	private sealed class Connection (string mode) : IConfigurationConnection
		{
		public int Submissions
			{
			get; private set;
			}
		public bool CompletionWaited
			{
			get; private set;
			}
		private int _reads;
		public Task<T?> ExecuteAsync<T> (int deviceId, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Assert.That (deviceId, Is.EqualTo (-6));
			if (command == "cp.platformDriverController:getDevicesEligibleForDriverUpdate")
				{
				_reads++;
				var eligibility = new DriverUpdateEligibility
					{
					InstalledDriverVersion = mode == "wrong-installed" ? "1.0.0.9" : "1.0.0.2",
					AvailableDriverVersion = mode == "wrong-available" ? "1.0.0.0" : "1.0.0.1",
					IsSupportsSwapDriver = mode != "unsupported",
					IsSwapDriverRequiresReboot = mode == "reboot",
					EligibleDeviceIds = mode == "multiple" || mode == "drift" && _reads > 1 ? [12, 13] : mode == "other-id" ? [13] : [12]
					};
				return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (eligibility)));
				}
			Assert.That (command, Is.EqualTo ("cp.platformDriverController:beginSwapDriverForAllEligibleDevices"));
			Submissions++;
			if (mode == "lost-submit")
				throw new IOException ("Synthetic lost response");
			return Task.FromResult (JsonSerializer.Deserialize<T> ("\"operation\""));
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default)
			=> Task.FromResult (new OperationResult (operationId, mode == "failed" ? "Failed" : "Ended", null));
		public Task<DriverSwapResult> WaitForDriverSwapAsync (string operationId, string driverId, TimeSpan timeout, CancellationToken cancellationToken = default)
			{
			CompletionWaited = true;
			if (mode == "missing-completion")
				throw new TimeoutException ();
			return Task.FromResult (new DriverSwapResult (operationId, driverId, mode == "unexpected-reboot", mode == "reconfigure" ? [12] : []));
			}
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}