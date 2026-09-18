// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class AndroidSelectionTests
	{
	[Test]
	public void ExactSelectionPreservesAllCasesSharingAName ()
		{
		string[] discovered = ["Example.Case(1)", "Example.Case(2)", "Example.Same", "Example.Same"];
		Assert.That (AndroidTestSelection.Select (discovered, ["Example.Same", "Example.Case(2)"]),
			Is.EqualTo (new[] { "Example.Case(2)", "Example.Same", "Example.Same" }));
		Assert.That (AndroidTestSelection.Select (discovered, null), Is.EqualTo (discovered));
		Assert.Throws<InvalidDataException> (() => AndroidTestSelection.Select (discovered, ["Example.Case"]));
		Assert.Throws<InvalidDataException> (() => AndroidTestSelection.Select (discovered, ["example.Case(1)"]));
		}

	[Test]
	public void EmptyMalformedDuplicateOrOversizedSelectionCannotDisableCoverage ()
		{
		foreach (string[] invalid in new string[][] { [], [" "], [null!], ["A", "A"], ["A\nB"], [new ('a', 4097)], Enumerable.Range (0, 1025).Select (i => "Case" + i).ToArray () })
			Assert.Throws<ArgumentException> (() => AndroidTestSelection.Validate (invalid));
		}
	}