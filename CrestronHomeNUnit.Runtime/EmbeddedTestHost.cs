// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;

using NUnit;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace CrestronHomeNUnit.Runtime;
/// <summary>Embeds NUnit directly, independently of any console or TCP transport.</summary>
public static class EmbeddedTestHost
	{
	private static readonly object _gate = new ();
	private static NUnitTestAssemblyRunner? _activeRunner;
	private static bool _busy;
	private static bool _stopRequested;
	/// <summary>Requests cooperative cancellation, allowing an active test to finish.</summary>
	public static bool Cancel ()
		{
		lock (_gate)
			{
			if (!_busy)
				{
				return false;
				}

			_stopRequested = true;
			StopActiveRunner ();
			return true;
			}
		}

	private static void StopActiveRunner ()
		{
#if NUNIT5_SNAPSHOT
		_activeRunner?.StopRun ();
#else
		_activeRunner?.StopRun (false);
#endif
		}

	public static int Run (Assembly testAssembly, string workDirectory, TextWriter output, bool explore = false, string suite = "self-tests") => RunWithProgress (testAssembly, workDirectory, output, explore, suite, null, null, CancellationToken.None);
	public static int RunWithProgress (Assembly testAssembly, string workDirectory, TextWriter output, bool explore, string suite, Action<string, string>? progress, IReadOnlyCollection<string>? selectedTests, CancellationToken cancellationToken, string? suiteFilterXml = null, string? testDataDirectory = null, bool enableLiveTests = false)
		{
		lock (_gate)
			{
			if (_busy)
				{
				throw new InvalidOperationException ("A test operation is already in progress.");
				}

			_busy = true;
			_stopRequested = false;
			}

		try
			{
			workDirectory = Path.GetFullPath (workDirectory);
			Directory.CreateDirectory (workDirectory);
#if NUNIT5_SNAPSHOT
			Type? monoRuntime = Type.GetType ("Mono.Runtime");
			object? monoVersion = monoRuntime?.GetMethod ("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke (null, null);
			output.WriteLine ($"Diagnostic runtime: CLR {Environment.Version}; Mono {monoVersion ?? (object)(monoRuntime != null)}; OS {Environment.OSVersion}");
#endif
			string filterXml = suiteFilterXml ?? suite switch
				{
					"self-tests" => "<filter><namespace re='1'>^NUnit[.]Framework[.]Tests[.](Assertions|Constraints|Syntax)($|[.])</namespace></filter>",
					"compatibility" => "<filter><or><class>LanguageTests</class><class>AsyncLifecycleTests</class></or></filter>",
					_ => throw new ArgumentException ("Unknown suite: " + suite, nameof (suite))
					};
			TestFilter filter = new SuiteFilter (TestFilter.FromXml (filterXml));
			if (selectedTests?.Count > 0)
				{
				var selection = new System.Text.StringBuilder ("<filter><or>");
				foreach (string testName in selectedTests)
					{
					selection.Append ("<test>").Append (SecurityElement.Escape (testName)).Append ("</test>");
					}

				selection.Append ("</or></filter>");
				filter = new SuiteFilter (TestFilter.FromXml ("<filter><and>" + filterXml + selection + "</and></filter>"));
				}

			using CancellationTokenRegistration registration = cancellationToken.Register (() => Cancel ());
			if (cancellationToken.IsCancellationRequested)
				{
				return 3;
				}

			var runner = new NUnitTestAssemblyRunner (new DefaultTestAssemblyBuilder ());
			var settings = new Dictionary<string, object>
				{
				[FrameworkPackageSettings.WorkDirectory] = workDirectory,
				[FrameworkPackageSettings.TestParametersDictionary] = new Dictionary<string, string> { ["TestDataDirectory"] = testDataDirectory ?? AppDomain.CurrentDomain.BaseDirectory, ["EnableLiveTests"] = enableLiveTests.ToString () },
				[FrameworkPackageSettings.NumberOfTestWorkers] = 0
				};
			ITest loaded = runner.Load (testAssembly, settings);
			int count = runner.CountTestCases (filter);
			output.WriteLine ("NUnit " + typeof (NUnit.Framework.Assert).Assembly.GetName ().Version + "; suite: " + suite + "; discovered: " + count);
			if (loaded.RunState == RunState.NotRunnable || count == 0)
				{
				output.WriteLine (loaded.ToXml (true).OuterXml);
				return 2;
				}

			if (explore)
				{
				var tree = new TNode ("test-run");
				tree.AddAttribute ("testcasecount", count.ToString (CultureInfo.InvariantCulture));
				tree.AddChildNode (runner.ExploreTests (filter).ToXml (true));
				File.WriteAllText (Path.Combine (workDirectory, "TestTree.xml"), tree.OuterXml);
				return 0;
				}

			lock (_gate)
				{
				if (_stopRequested)
					{
					return 3;
					}

				_activeRunner = runner;
				}

			ITestResult result = runner.Run (new ProgressListener (output, progress), filter);
			var xml = new TNode ("test-run");
			xml.AddAttribute ("id", "0");
			xml.AddAttribute ("name", result.Name);
			xml.AddAttribute ("fullname", result.FullName);
			xml.AddAttribute ("testcasecount", count.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("result", result.ResultState.Status.ToString ());
			xml.AddAttribute ("total", result.TotalCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("passed", result.PassCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("failed", result.FailCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("skipped", result.SkipCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("inconclusive", result.InconclusiveCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("warnings", result.WarningCount.ToString (CultureInfo.InvariantCulture));
			xml.AddAttribute ("start-time", result.StartTime.ToString ("o", CultureInfo.InvariantCulture));
			xml.AddAttribute ("end-time", result.EndTime.ToString ("o", CultureInfo.InvariantCulture));
			xml.AddAttribute ("duration", result.Duration.ToString (CultureInfo.InvariantCulture));
			xml.AddChildNode (result.ToXml (true));
			File.WriteAllText (Path.Combine (workDirectory, "TestResult.xml"), xml.OuterXml);
			output.WriteLine ($"Total: {result.TotalCount}, passed: {result.PassCount}, failed: {result.FailCount}, skipped: {result.SkipCount}");
			lock (_gate)
				{
				return _stopRequested ? 3 : result.ResultState.Status == TestStatus.Failed ? 1 : 0;
				}
			}
		finally
			{
			lock (_gate)
				{
				_activeRunner = null;
				_busy = false;
				}
			}
		}

	// Selecting a whole suite must not opt in to NUnit's explicitly marked demonstration tests.
	private sealed class SuiteFilter (TestFilter inner) : TestFilter
		{
		public override bool Pass (ITest test) => inner.Pass (test);
		public override bool Pass (ITest test, bool negated) => inner.Pass (test, negated);
		public override bool Match (ITest test) => inner.Match (test);
		public override bool IsExplicitMatch (ITest test) => false;
		public override TNode AddToXml (TNode parentNode, bool recursive) => inner.AddToXml (parentNode, recursive);
		}

	private sealed class ProgressListener (TextWriter output, Action<string, string>? progress) : ITestListener
		{
		private readonly TextWriter _output = TextWriter.Synchronized (output);
		public void TestStarted (ITest test)
			{
			lock (_gate)
				{
				if (_stopRequested)
					{
					StopActiveRunner ();
					}
				}

			if (!test.IsSuite)
				{
				_output.WriteLine ("START " + test.FullName);
				progress?.Invoke ("test-start", test.ToXml (false).OuterXml);
				}
			}

		public void TestFinished (ITestResult result)
			{
			if (!result.Test.IsSuite)
				{
				_output.WriteLine (result.ResultState + " " + result.FullName);
				progress?.Invoke ("test-finish", result.ToXml (true).OuterXml);
				if (result.ResultState.Status == TestStatus.Failed)
					{
					_output.WriteLine (result.Message);
					_output.WriteLine (result.StackTrace);
					}

				if (!string.IsNullOrEmpty (result.Output))
					{
					_output.Write (result.Output);
					}
				}
			}

		public void TestOutput (TestOutput output)
			{
			_output.Write (output.Text);
			progress?.Invoke ("test-output", output.Text);
			}

		public void SendMessage (TestMessage message) => _output.WriteLine (message.Message);
		}
	}