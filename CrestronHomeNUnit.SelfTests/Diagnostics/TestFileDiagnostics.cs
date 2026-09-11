// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;

namespace NUnit.Framework.Tests.TestUtilities;

internal static class TestFileDiagnostics
	{
	public static void Write (string path)
		{
		TestContext.Out.WriteLine ("TestFile diagnostics: " + path);
		Report ("Temporary directory", Path.GetTempPath);
		Report ("File.Exists", () => File.Exists (path).ToString ());
		Report ("New FileInfo.Exists", () => new FileInfo (path).Exists.ToString ());
		Report ("Attributes", () => File.GetAttributes (path).ToString ());
		Report ("OpenRead length", () =>
			{
				using var stream = File.OpenRead (path);
				return stream.Length.ToString ();
			});
		Report ("Matching directory entries", () => string.Join (", ",
			Directory.GetFiles (Path.GetDirectoryName (path)!)
				.Where (entry => string.Equals (Path.GetFileName (entry), Path.GetFileName (path), StringComparison.OrdinalIgnoreCase))));
		}

	private static void Report (string operation, Func<string> read)
		{
		try
			{
			TestContext.Out.WriteLine (operation + ": " + read ());
			}
		catch (Exception exception)
			{
			TestContext.Out.WriteLine (operation + ": " + exception.GetType ().FullName + ": " + exception.Message);
			}
		}
	}