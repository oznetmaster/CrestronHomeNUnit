// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using CrestronHomeNUnit.Runtime;
using CrestronHomeNUnit.Transport;

using NUnit.Framework;

internal static class TestInputsValidation
	{
	public static async Task RunAsync ()
		{
		var sample = new TestInputFile { Name = "settings.json", Content = Encoding.UTF8.GetBytes ("synthetic-private-value") };
		var request = new WireMessage { Kind = "run", RequestId = "encryption-check", Suite = "inputs", TestInputs = new List<TestInputFile> { sample } };
		byte[] key = SecureTestData.Key ("test-pairing-key", SecureTestData.Nonce (), SecureTestData.Nonce ());
		request.ProtectedData = SecureTestData.Protect (key, request, request.TestInputs);
		using (var stream = new MemoryStream ())
			{
			new MessageStream (stream).Write (request);
			Require (!Encoding.UTF8.GetString (stream.ToArray ()).Contains ("synthetic-private-value"), "Plaintext inputs entered the wire message.");
			}
		Require (SecureTestData.Unprotect (key, request).Single ().Content.SequenceEqual (sample.Content), "Input encryption changed the contents.");
		string valid = request.ProtectedData;
		byte[] tampered = Convert.FromBase64String (valid);
		tampered[20] ^= 1;
		request.ProtectedData = Convert.ToBase64String (tampered);
		Reject (() => SecureTestData.Unprotect (key, request));
		request.ProtectedData = valid;
		request.EnableLiveTests = true;
		Reject (() => SecureTestData.Unprotect (key, request));
		request.EnableLiveTests = false;
		request.Suite = "different-suite";
		Reject (() => SecureTestData.Unprotect (key, request));
		foreach (string name in new[] { "../settings.json", "folder/settings.json", "C:settings.json", "settings.json.", "CON.json" })
			Reject (() => SecureTestData.ValidateFiles (new[] { new TestInputFile { Name = name, Content = [] } }));
		Reject (() => SecureTestData.ValidateFiles (new[] { new TestInputFile { Name = "huge.json", Content = new byte[SecureTestData.MaximumFileBytes + 1] } }));

		string work = Path.Combine (Path.GetTempPath (), "CrestronHomeNUnit-inputs-" + Guid.NewGuid ().ToString ("N"));
		const string FILTER = "<filter><and><namespace>InputValidationTests</namespace><not><cat>Live</cat></not></and></filter>";
		var suites = new[]
		{
				new TestSuiteDefinition { Id = "inputs", Name = "Input validation", FilterXml = FILTER },
				new TestSuiteDefinition { Id = "other-inputs", Name = "Other input validation", FilterXml = FILTER }
		  };
		using var host = new TestExecutionService (work, _ => Assembly.GetExecutingAssembly (), suites);
		using var server = new RemoteTestServer (host, "test-pairing-key", 0, IPAddress.Loopback);
		using var client = await RemoteTestClient.ConnectAsync ("127.0.0.1", server.Port, "test-pairing-key");
		Require (client.SupportsTestInputs && client.Suites.Select (s => s.Id).SequenceEqual (new[] { "inputs", "other-inputs" }), "Authenticated suite advertisement failed.");
		foreach (string expected in new[] { "first configuration", "updated configuration" })
			{
			InputValidationTests.InputProbe.Expected = expected;
			var files = new List<TestInputFile> { new TestInputFile { Name = "settings.json", Content = Encoding.UTF8.GetBytes (expected) } };
			WireMessage discovered = await client.SendAsync (new WireMessage { Kind = "discover", Suite = "inputs", TestInputs = files });
			Require (discovered.ExitCode == 0 && discovered.Xml.Contains ("InputProbe.ReadsInputs") && !discovered.Xml.Contains ("LiveProbe"), "Live fixture leaked into the non-live suite.");
			WireMessage run = await client.SendAsync (new WireMessage { Kind = "run", Suite = "inputs", TestInputs = files });
			Require (run.ExitCode == 0 && run.Xml.Contains ("passed=\"1\""), "Runner-provided configuration was not read: " + run.Text);
			}
		WireMessage excluded = await client.SendAsync (new WireMessage { Kind = "run", Suite = "inputs", TestNames = new List<string> { "InputValidationTests.LiveProbe.MustNotRun" } });
		Require (excluded.ExitCode == 2, "Selected test bypassed the suite exclusion.");
		InputValidationTests.InputProbe.Expected = null;
		WireMessage separate = await client.SendAsync (new WireMessage { Kind = "run", Suite = "other-inputs", TestInputs = [] });
		Require (separate.ExitCode == 0, "Input files leaked between suites.");
		WireMessage cleared = await client.SendAsync (new WireMessage { Kind = "run", Suite = "inputs", TestInputs = [] });
		Require (cleared.ExitCode == 0, "Clearing inputs retained old configuration.");
		Console.WriteLine ("Protected inputs, tamper rejection, safe filenames, suite advertisement, live exclusion, replacement, isolation and clearing passed.");
		}
	private static void Reject (Action operation)
		{
		try
			{
			operation ();
			}
		catch (InvalidDataException) { return; }
		throw new InvalidOperationException ("Invalid input was accepted.");
		}
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}

namespace InputValidationTests
	{
	[TestFixture]
	public sealed class InputProbe
		{
		public static string? Expected;
		[Test]
		public void ReadsInputs ()
			{
			string path = Path.Combine (TestContext.Parameters["TestDataDirectory"]!, "settings.json");
			if (Expected == null)
				Assert.That (File.Exists (path), Is.False);
			else
				Assert.That (File.ReadAllText (path), Is.EqualTo (Expected));
			}
		}
	[TestFixture, Category ("Live")]
	public sealed class LiveProbe
		{
		[Test]
		public void MustNotRun () => Assert.Fail ("The non-live filter failed.");
		}
	}