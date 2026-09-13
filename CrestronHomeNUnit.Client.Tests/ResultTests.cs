// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

using CrestronHomeNUnit.Client;
using CrestronHomeNUnit.Transport;

namespace CrestronHomeNUnit.Client.Tests;

[TestFixture]
public sealed class ResultTests
	{
	[TestCase ("Passed", 3, 3, 0, 0, 0)]
	[TestCase ("Failed", 3, 2, 1, 0, 1)]
	[TestCase ("Passed", 0, 0, 0, 0, 4)]
	[TestCase ("Unknown", 3, 3, 0, 0, 3)]
	[TestCase ("Skipped", 3, 0, 0, 3, 0)]
	public void MapsNUnitOutcomeToCiExit (string outcome, int total, int passed, int failed, int skipped, int exit)
		{
		var result = TestResultSummary.FromResponse (new WireMessage { Kind = "complete", Xml = $"<test-run result='{outcome}' total='{total}' passed='{passed}' failed='{failed}' skipped='{skipped}'/>" });
		Assert.That (result.ExitCode, Is.EqualTo (exit));
		}
	[Test]
	public void NonzeroHostExitCannotBecomeSuccessThroughXml ()
		{
		var result = TestResultSummary.FromResponse (new WireMessage { Kind = "complete", ExitCode = 1, Xml = "<test-run result='Passed' total='3' passed='3' failed='0' skipped='0'/>" });
		Assert.That (result.ExitCode, Is.EqualTo (1));
		Assert.That (result.Failed, Is.Zero);
		Assert.That (result.HostExitCode, Is.EqualTo (1));
		}
	[Test]
	public void ClosedOrMissingRunIsIncomplete () => Assert.That (TestResultSummary.FromResponse (new WireMessage { Kind = "error" }).ExitCode, Is.EqualTo (3));
	[Test]
	public void InvalidCountsAreRejected () => Assert.Throws<InvalidDataException> (() => TestResultSummary.FromResponse (new WireMessage { Kind = "complete", Xml = "<test-run result='Passed' total='-1' passed='0' failed='0' skipped='0'/>" }));
	[Test]
	public void ProcessorIdentityMustHaveTwoValidFields ()
		{
		Assert.Throws<InvalidDataException> (() => ProcessorConnection.Parse ("invalid"));
		var identity = ProcessorConnection.Parse (new string ('a', 32) + "\n" + new string ('b', 32));
		Assert.That (identity.Id, Is.EqualTo (new string ('a', 32)));
		}
	}