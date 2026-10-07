// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
using System.Runtime.InteropServices;
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;

namespace CrestronHomeNUnit.Workflow.Tests;

public sealed class SingleFileArtifactReuseTests
	{
	[Test]
	public async Task PublishedSingleFileRequestsFreshBuildWhenBackendBytesAreNotSeparate ()
		{
		var directory = new DirectoryInfo (TestContext.CurrentContext.TestDirectory);
		while (directory != null && !File.Exists (Path.Combine (directory.FullName, "tools", "ArtifactReuseProbe", "ArtifactReuseProbe.csproj")))
			directory = directory.Parent;
		Assert.That (directory, Is.Not.Null, "The packaging regression requires the repository's probe source.");
		var root = Path.Combine (Path.GetTempPath (), "singlefile-artifacts-" + Guid.NewGuid ().ToString ("N"));
		Directory.CreateDirectory (root);
		try
			{
			var publish = Path.Combine (root, "publish");
			var log = Path.Combine (root, "publish.log");
			using var timeout = new CancellationTokenSource (TimeSpan.FromMinutes (3));
			var project = Path.Combine (directory!.FullName, "tools", "ArtifactReuseProbe", "ArtifactReuseProbe.csproj");
			var exit = await WorkflowEvidence.ProcessAsync ("dotnet", ["publish", project, "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier,
				"--self-contained", "false", "-p:PublishSingleFile=true", "-o", publish, "--verbosity", "quiet"], root, log, timeout.Token);
			Assert.That (exit, Is.Zero, await File.ReadAllTextAsync (log, timeout.Token));
			var executable = Path.Combine (publish, OperatingSystem.IsWindows () ? "ArtifactReuseProbe.exe" : "ArtifactReuseProbe");
			var runLog = Path.Combine (root, "run.log");
			exit = await WorkflowEvidence.ProcessAsync (executable, [root], root, runLog, timeout.Token);
			Assert.That (exit, Is.Zero, await File.ReadAllTextAsync (runLog, timeout.Token));
			Assert.That (await File.ReadAllTextAsync (runLog, timeout.Token), Does.Contain ("Bundled backend requests a fresh build"));
			}
		finally
			{
			Directory.Delete (root, true);
			}
		}
	}
