// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class ConfigurationTests
	{
	private static readonly DriverInstanceReady TARGET = new (17, "Example", "1.0.000.0001", "Installed");

	[TestCase (0, true, true)]
	[TestCase (17, false, true)]
	[TestCase (0, false, false)]
	[TestCase (17, true, false)]
	public void CheckTargetMustChooseAnAssignedIdOrTheActualDriver (int id, bool actual, bool valid)
		{
		var project = typeof (ConfigurationTests).Assembly.Location;
		var plan = new WorkflowPlan
			{
			Host = "example.invalid",
			CertificateSha256 = "pin",
			SshFingerprint = "pin",
			SourceRoots = ["source"],
			LocalTests = [new (project, 1)],
			TestPackage = new (project, Path.GetFullPath ("test.pkg"), "Tests", 1),
			ActualDriver = new (project, Path.GetFullPath ("actual.pkg"), "Actual", 1),
			ProcessorSuites = [new ("unit", 1, [])],
			LiveSuites = [new ("live", 1, [])],
			DeployedChecks = [new ("Ready", id, "Example", "ready", JsonSerializer.SerializeToElement (true)) { UseActualDriver = actual }]
			};
		if (valid)
			Assert.DoesNotThrow (plan.Validate);
		else
			Assert.Throws<ArgumentException> (plan.Validate);
		}

	[Test]
	public async Task ExistingConfiguredDriverIsNeverReconfigured ()
		{
		var connection = new Connection (Device (true), null);
		bool applied = await WorkflowConfiguration.ApplyAsync (new (connection), TARGET,
			new Dictionary<string, string> { ["Secret"] = "private-value" }, default);
		Assert.That (applied, Is.False);
		Assert.That (connection.Submissions, Is.Zero);
		}

	[TestCase ("complete", 2)]
	[TestCase ("wrongFirst", 0)]
	[TestCase ("repeated", 1)]
	[TestCase ("error", 1)]
	[TestCase ("extra", 2)]
	public async Task WizardSubmitsOnlyExactPlannedSteps (string scenario, int expectedWrites)
		{
		JsonElement Step (string id, bool error = false) => JsonSerializer.SerializeToElement (new
			{
			Id = id,
			ConfigurationErrors = error ? new
				{
				Message = "private-value"
				} : null,
			Items = new[] { new { Id = "Secret", Value = new { ReadOnly = false } } }
			});
		var connection = new WizardConnection (Device (false) with
			{
			Commands = ["cp.driverConfiguration:getFirstConfigurationStep", "cp.driverConfiguration:applyConfigurationStep"]
			}, [Step (scenario == "wrongFirst" ? "Unexpected" : "Connection"),
			Step (scenario == "repeated" ? "Connection" : "Settings", scenario == "error"), scenario == "extra" ? Step ("Unexpected") : null]);
		var input = new WorkflowConfiguration.Inputs (null,
			[new ("Connection", new Dictionary<string, string> { ["Secret"] = "private-value" }), new ("Settings", new Dictionary<string, string> ())]);
		if (scenario == "complete")
			Assert.That (await WorkflowConfiguration.ApplyAsync (new (connection), TARGET, input, default), Is.True);
		else
			{
			var error = Assert.CatchAsync<InvalidOperationException> (async () => await WorkflowConfiguration.ApplyAsync (new (connection), TARGET, input, default));
			Assert.That (error!.ToString (), Does.Not.Contain ("private-value"));
			}
		Assert.That (connection.Writes, Is.EqualTo (expectedWrites));
		}

	private sealed class WizardConnection (DeviceInfo device, JsonElement?[] responses) : IConfigurationConnection
		{
		private readonly Queue<JsonElement?> _responses = new (responses);
		public int Writes
			{
			get; private set;
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (device)));
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			if (command == "cp.driverConfiguration:applyConfigurationStep")
				Writes++;
			else
				Assert.That (command, Is.EqualTo ("cp.driverConfiguration:getFirstConfigurationStep"));
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (_responses.Dequeue ())));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}

	[Test]
	public async Task UnconfiguredDriverReceivesNamedStringValuesOnce ()
		{
		var connection = new Connection (Device (false), JsonSerializer.SerializeToElement (Array.Empty<object> ()));
		Assert.That (await WorkflowConfiguration.ApplyAsync (new (connection), TARGET,
			new Dictionary<string, string> { ["Secret"] = "private-value" }, default), Is.True);
		Assert.That (connection.Submissions, Is.EqualTo (1));
		Assert.That (connection.Parameters.GetProperty ("configurationItemValues").GetProperty ("Secret").GetString (), Is.EqualTo ("private-value"));
		}

	[TestCase ("model")]
	[TestCase ("version")]
	[TestCase ("readonly")]
	[TestCase ("unknown")]
	public void ChangedOrUnsupportedTargetSubmitsNothing (string failure)
		{
		var device = Device (false);
		if (failure == "model")
			device = device with
				{
				Model = "Other"
				};
		if (failure == "version")
			device.PropertyValues["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("2.0");
		if (failure == "readonly")
			device.PropertyValues["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement (new[] { new { Id = "Secret", Value = new { ReadOnly = true } } });
		var connection = new Connection (device, null);
		Assert.CatchAsync<InvalidOperationException> (async () => await WorkflowConfiguration.ApplyAsync (new (connection), TARGET,
			new Dictionary<string, string> { [failure == "unknown" ? "Unknown" : "Secret"] = "private-value" }, default));
		Assert.That (connection.Submissions, Is.Zero);
		}

	[Test]
	public void ValidationErrorsDoNotExposeSecretsOrReplayCommand ()
		{
		var connection = new Connection (Device (false), JsonSerializer.SerializeToElement (new[] { new { ItemId = "Secret", ErrorMessage = "private-value" } }));
		var exception = Assert.CatchAsync<InvalidOperationException> (async () => await WorkflowConfiguration.ApplyAsync (new (connection), TARGET,
			new Dictionary<string, string> { ["Secret"] = "private-value" }, default));
		Assert.That (exception!.ToString (), Does.Not.Contain ("private-value"));
		Assert.That (connection.Submissions, Is.EqualTo (1));
		}

	[TestCase ("{\"Secret\":1}")]
	[TestCase ("{\"Secret\":\"first\",\"Secret\":\"second\"}")]
	[TestCase ("{}")]
	[TestCase ("{\"Secret\":private-value}")]
	public void InvalidPrivateFilesFailWithoutExposingContents (string json)
		{
		var path = Path.GetTempFileName ();
		try
			{
			File.WriteAllText (path, json);
			var exception = Assert.Throws<InvalidDataException> (() => WorkflowConfiguration.ReadInputs (path));
			Assert.That (exception!.ToString (), Does.Not.Contain ("private-value").And.Not.Contain (path));
			}
		finally { File.Delete (path); }
		}

	private static DeviceInfo Device (bool configured) => new ()
		{
		Id = 17,
		Model = "Example",
		Commands = ["cp.driverConfiguration:applyConfiguration"],
		PropertyValues = new ()
			{
			["cp.driverInformation:version"] = JsonSerializer.SerializeToElement ("1.0.0.1"),
			["cp.driverConfiguration:isConfigured"] = JsonSerializer.SerializeToElement (configured),
			["cp.driverConfiguration:configurationItems"] = JsonSerializer.SerializeToElement (new[] { new { Id = "Secret", Value = new { ReadOnly = false } } })
			}
		};

	private sealed class Connection (DeviceInfo device, JsonElement? response) : IConfigurationConnection
		{
		public int Submissions
			{
			get; private set;
			}
		public JsonElement Parameters
			{
			get; private set;
			}
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default) => Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (device)));
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Submissions++;
			Assert.That (id, Is.EqualTo (17));
			Assert.That (command, Is.EqualTo ("cp.driverConfiguration:applyConfiguration"));
			Parameters = JsonSerializer.SerializeToElement (parameters);
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (response)));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}
