// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Text.Json;

using CrestronHomeDevTools;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class InstalledControlIdentityTests
	{
	[TestCase ("correct", true)]
	[TestCase ("model", false)]
	[TestCase ("identity", false)]
	[TestCase ("command", false)]
	[TestCase ("ancestry", false)]
	[TestCase ("cycle", false)]
	[TestCase ("version", false)]
	[TestCase ("correct", true, true, true)]
	[TestCase ("correct", true, true, false)]
	[TestCase ("command", false, true, true)]
	public async Task OnlyTheExactDescendantCanReceiveACommand (string change, bool allowed, bool pair = false, bool requested = true)
		{
		var plan = new InstalledControlPlan
			{
			Name = "Outlet",
			DeviceId = 2,
			Model = "Outlet",
			PhysicalIdentity = "device-1",
			Command = pair ? null : "setPower",
			Parameter = pair ? null : "value",
			BooleanCommands = pair ? new ("powerOn", "powerOff") : null,
			StateProperty = "power",
			InvertBoolean = true,
			Probe = new (typeof (InstalledControlIdentityTests).Assembly.Location, AppContext.BaseDirectory, [])
			};
		var connection = new Connection (new Dictionary<int, DeviceInfo>
			{
			[1] = new () { Id = 1, Model = "Root", PropertyValues = new () { ["cp.driverInformation:version"] = JsonSerializer.SerializeToElement (change == "version" ? "other" : "1.0.0.1") } },
			[2] = new ()
				{
				Id = 2,
				ParentDeviceId = change == "cycle" ? 2 : change == "ancestry" ? 3 : 1,
				Model = change == "model" ? "other" : "Outlet",
				Commands = change == "command" ? [] : ["setPower", "powerOn", "powerOff"],
				PropertyValues = new ()
					{
					["controlDeviceId"] = JsonSerializer.SerializeToElement (change == "identity" ? "another-device" : "device-1")
					}
				}
			});
		var session = new InstalledControlRunner.Session (new (connection), new (1, "Root", "1.0.0.1", "Existing"), plan, AppContext.BaseDirectory);
		if (allowed)
			await session.SubmitAsync (JsonSerializer.SerializeToElement (requested), default);
		else
			Assert.CatchAsync<InvalidOperationException> (() => session.SubmitAsync (JsonSerializer.SerializeToElement (requested), default));
		Assert.That (connection.Writes, Is.EqualTo (allowed ? 1 : 0));
		if (allowed)
			{
			Assert.That (connection.Command, Is.EqualTo (pair ? requested ? "powerOn" : "powerOff" : "setPower"));
			if (pair)
				Assert.That (connection.Parameters.ValueKind, Is.EqualTo (JsonValueKind.Null));
			else
				Assert.That (connection.Parameters.GetProperty ("value").GetBoolean (), Is.EqualTo (requested));
			}
		}

	private sealed class Connection (Dictionary<int, DeviceInfo> devices) : IConfigurationConnection
		{
		public int Writes;
		public string? Command;
		public JsonElement Parameters;
		public Task<T?> GetAsync<T> (string path, CancellationToken cancellationToken = default)
			{
			devices.TryGetValue (int.Parse (path.Split ('/').Last ()), out var device);
			return Task.FromResult (JsonSerializer.Deserialize<T> (JsonSerializer.Serialize (device)));
			}
		public Task<T?> ExecuteAsync<T> (int id, string command, object? parameters = null, CancellationToken cancellationToken = default)
			{
			Writes++;
			Assert.That (id, Is.EqualTo (2));
			Command = command;
			Parameters = JsonSerializer.SerializeToElement (parameters);
			return Task.FromResult (default (T));
			}
		public Task<OperationResult> WaitForOperationAsync (string operationId, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException ();
		public ValueTask DisposeAsync () => ValueTask.CompletedTask;
		}
	}