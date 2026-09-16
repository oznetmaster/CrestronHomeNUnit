// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.

using CrestronHomeNUnit.Android;

using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

[TestFixture]
public sealed class AndroidCompletionTests
	{
	[Test]
	public void CompletionRequiresTheCurrentRunPackageAndConfirmedRestoration ()
		{
		var context = new AndroidRunContext (1, "run", "worker", 1, 1, "host", 7, "guid", "1.0.0.1", "package", "source", null!, "evidence");
		var good = new AndroidRunCompletion (1, "run", "package", true);
		Assert.That (WorkflowAndroid.CompletionMatches (good, context), Is.True);
		foreach (var changed in new[] { good with { RunId = "other" }, good with { PackageSha256 = "other" }, good with { RestorationConfirmed = false }, good with { SchemaVersion = 2 } })
			Assert.That (WorkflowAndroid.CompletionMatches (changed, context), Is.False);
		}
	}