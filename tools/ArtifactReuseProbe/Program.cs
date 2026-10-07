// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE in the repository root.
using System.Reflection;
using System.Text.Json;
using CrestronHomeNUnit.Workflow;

// Invoked only by the single-file packaging regression; no processor or device access.
var assembly = typeof (WorkflowRunner).Assembly;
#pragma warning disable IL3000 // This probe explicitly verifies the bundled assembly has no path.
if (assembly.Location.Length != 0) throw new InvalidOperationException ("Probe must run as a single-file bundle.");
#pragma warning restore IL3000
var root = Path.GetFullPath (args.Single ());
var project = Path.Combine (root, "Example.csproj");
Directory.CreateDirectory (Path.Combine (root, "obj"));
await File.WriteAllTextAsync (Path.Combine (root, "obj", "project.assets.json"), JsonSerializer.Serialize (new
	{
	project = new { restore = new { projectPath = project, frameworks = new { net10 = new { projectReferences = new { } } } } }
	}));
var type = assembly.GetType ("CrestronHomeNUnit.Workflow.WorkflowArtifacts", throwOnError: true)!;
var method = type.GetMethod ("DigestInputsAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
var digest = await (Task<string?>)method.Invoke (null, [project, Array.Empty<string> (), "synthetic-sdk", CancellationToken.None, null])!;
if (digest != null) throw new InvalidOperationException ("Bundled backend must request a fresh build.");
Console.WriteLine ("Bundled backend requests a fresh build without artifact reuse.");
