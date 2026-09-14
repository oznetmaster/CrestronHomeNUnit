// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Runtime;
/// <summary>Coordinates both Home commands and desktop requests for one driver instance.</summary>
public sealed class TestExecutionService (string workDirectory, Func<string, Assembly> assemblyForSuite, IReadOnlyList<TestSuiteDefinition>? suiteDefinitions = null, string? testDataDirectory = null, string? processorLeasePath = null) : ITestExecutionHost, ITestSuiteProvider, IDisposable
	{
	private readonly object _gate = new ();
	private readonly IReadOnlyDictionary<string, TestSuiteDefinition> _suites = (suiteDefinitions ?? TestSuiteDefinition.BuiltIn).ToDictionary (suite => suite.Id, StringComparer.Ordinal);
	public IReadOnlyList<TestSuiteInfo> Suites => _suites.Values.Select (suite => new TestSuiteInfo { Id = suite.Id, Name = suite.Name, ManualOnly = suite.ManualOnly }).ToArray ();
	private CancellationTokenSource? _cancellation;
	private string? _operationId;
	private bool _disposed;
	public event Action<WireMessage>? StateChanged;
	public Task<WireMessage> ExecuteAsync (WireMessage request, Action<WireMessage> publish)
		{
		if (request.Kind is not ("run" or "discover") || (request.Suite is null || !Regex.IsMatch (request.Suite, "^[a-z0-9][a-z0-9-]{0,63}$") || !_suites.ContainsKey (request.Suite)))
			{
			return Task.FromResult (WireMessage.Reply (request, "error", "Unknown operation or suite."));
			}

		if (request.Kind == "run" && _suites[request.Suite].ManualOnly && !request.EnableLiveTests)
			return Task.FromResult (WireMessage.Reply (request, "error", "Select this live suite in the Windows runner to run it. Live tests cannot be started from the Home tile."));

		CancellationTokenSource cancellation;
		lock (_gate)
			{
			if (_disposed || _operationId != null)
				{
				return Task.FromResult (WireMessage.Reply (request, "error", _disposed ? "Host is stopping." : "Another test operation is already running."));
				}

			_operationId = request.RequestId;
			_cancellation = cancellation = new CancellationTokenSource ();
			}

		return Task.Run (() =>
		{
			WireMessage result;
			try
				{
				using var processorLease = processorLeasePath == null ? null : ProcessorExecutionLease.Acquire (processorLeasePath, request.LeaseOwner, request.RequestId);
				var started = WireMessage.Reply (request, "started", request.Kind == "discover" ? "Discovering tests" : "Running tests");
				started.TargetId = request.Kind;
				StateChanged?.Invoke (started);
				publish (started);
				bool explore = request.Kind == "discover";
				string directory = Path.Combine (workDirectory, request.Suite);
				string inputDirectory = Path.Combine (directory, "Inputs");
				if (request.TestInputs != null)
					{
					SecureTestData.ValidateFiles (request.TestInputs);
					Directory.CreateDirectory (inputDirectory);
					foreach (string oldFile in Directory.GetFiles (inputDirectory))
						File.Delete (oldFile);
					foreach (TestInputFile file in request.TestInputs)
						File.WriteAllBytes (Path.Combine (inputDirectory, file.Name), file.Content);
					}
				Directory.CreateDirectory (directory);
				string xmlPath = Path.Combine (directory, explore ? "TestTree.xml" : "TestResult.xml");
				// A failed/cancelled load must never return an earlier run's XML as its result.
				if (File.Exists (xmlPath))
					{
					File.Delete (xmlPath);
					}

				using var output = new StreamWriter (Path.Combine (directory, explore ? "DiscoveryOutput.txt" : "TestOutput.txt"), false)
					{
					AutoFlush = true
					};
				int exitCode = EmbeddedTestHost.RunWithProgress (assemblyForSuite (request.Suite), directory, output, explore, request.Suite, (kind, value) =>
				  {
					  var progress = WireMessage.Reply (request, kind);
					  if (kind == "test-output")
						  {
						  progress.Text = value;
						  }
					  else
						  {
						  progress.Xml = value;
						  }

					  publish (progress);
				  }, request.TestNames, cancellation.Token, _suites[request.Suite].FilterXml, Directory.Exists (inputDirectory) ? inputDirectory : testDataDirectory, request.EnableLiveTests && _suites[request.Suite].ManualOnly);
				result = WireMessage.Reply (request, "complete");
				result.ExitCode = exitCode;
				result.Xml = File.Exists (xmlPath) ? File.ReadAllText (xmlPath) : "";
				result.Text = explore ? (exitCode == 0 ? "Test tree saved" : "Discovery stopped: " + exitCode) : Summarize (result);
				}
			catch (Exception exception)
				{
				result = WireMessage.Reply (request, "error", exception.ToString ());
				}

			result.TargetId = request.Kind;
			try
				{
				StateChanged?.Invoke (result);
				return result;
				}
			finally
				{
				lock (_gate)
					{
					_operationId = null;
					_cancellation = null;
					cancellation.Dispose ();
					}
				}
		});
		}

	private static string Summarize (WireMessage result)
		{
		if (string.IsNullOrEmpty (result.Xml))
			{
			return "Host exit code " + result.ExitCode;
			}

		var document = new XmlDocument
			{
			XmlResolver = null
			};
		document.LoadXml (result.Xml);
		XmlElement run = document.DocumentElement!;
		string prefix = result.ExitCode == 3 ? "Cancelled: " : "";
		return prefix + $"{run.GetAttribute ("passed")} passed, {run.GetAttribute ("failed")} failed, {run.GetAttribute ("skipped")} skipped, {run.GetAttribute ("inconclusive")} inconclusive, {run.GetAttribute ("warnings")} warnings";
		}

	public bool Cancel (string operationId)
		{
		lock (_gate)
			{
			if (_operationId != operationId || _cancellation == null)
				{
				return false;
				}

			_cancellation.Cancel ();
			return true;
			}
		}

	public void Dispose ()
		{
		lock (_gate)
			{
			_disposed = true;
			_cancellation?.Cancel ();
			}
		}
	}