// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

using CrestronHomeNUnit.Runner;

internal static class RunnerWindowPlacementValidation
	{
	public static void Run ()
		{
		string directory = Path.Combine (Path.GetTempPath (), "RunnerWindow-" + Guid.NewGuid ().ToString ("N"));
		string path = Path.Combine (directory, "window.json");
		string preferences = Path.Combine (directory, "selections.json");
		Directory.CreateDirectory (directory);
		RunnerForm Open ()
			{
			var form = new RunnerForm ((_, _, _, _) => throw new InvalidOperationException ("Window restoration must not authenticate."), preferences, windowPlacementPath: path)
				{
				Opacity = 0,
				ShowInTaskbar = false
				};
			form.Show ();
			Application.DoEvents ();
			return form;
			}
		try
			{
			Rectangle expected;
			using (var form = Open ())
				{
				Rectangle area = Screen.FromControl (form).WorkingArea;
				form.Bounds = new Rectangle (area.Left + 20, area.Top + 30, Math.Min (1100, area.Width - 20), Math.Min (720, area.Height - 30));
				expected = form.Bounds;
				Require (File.Exists (path), "Moving/resizing did not persist before close.");
				string initial = File.ReadAllText (path);
				// Move inward so the window stays inside even a narrow CI working area.
				form.Left -= 1;
				Require (File.ReadAllText (path) != initial, "Moving the window did not persist immediately.");
				expected = form.Bounds;
				string beforeMinimize = File.ReadAllText (path);
				form.WindowState = FormWindowState.Minimized;
				Application.DoEvents ();
				Require (File.ReadAllText (path) == beforeMinimize, "Minimizing modified saved placement.");
				form.WindowState = FormWindowState.Normal;
				form.Close ();
				}
			Require (File.Exists (path) && !File.Exists (preferences), "Window placement depended on opting into test selections.");
			using (var form = Open ())
				{
				Require (form.Bounds == expected && form.WindowState == FormWindowState.Normal, "Normal window bounds were not restored. Expected " + expected + ", actual " + form.Bounds + ".");
				form.WindowState = FormWindowState.Maximized;
				Application.DoEvents ();
				form.Close ();
				}
			using (var form = Open ())
				{
				Require (form.WindowState == FormWindowState.Maximized, "Maximized state was not restored.");
				form.WindowState = FormWindowState.Normal;
				Application.DoEvents ();
				Require (form.Bounds == expected, "Maximized window lost its normal restore bounds.");
				form.WindowState = FormWindowState.Maximized;
				form.WindowState = FormWindowState.Minimized;
				Application.DoEvents ();
				form.Close ();
				}
			using (var form = Open ())
				{
				Require (form.WindowState == FormWindowState.Maximized, "Closing minimized lost the preceding maximized state.");
				form.WindowState = FormWindowState.Normal;
				form.WindowState = FormWindowState.Minimized;
				Application.DoEvents ();
				form.Close ();
				}
			using (var form = Open ())
				{
				Require (form.WindowState == FormWindowState.Normal && form.Bounds == expected, "A minimized window reopened minimized or lost its normal bounds.");
				form.Close ();
				}
			File.WriteAllText (path, "invalid JSON");
			using (var form = Open ())
				{
				Require (form.Visible && form.WindowState == FormWindowState.Normal, "Invalid settings prevented a visible normal startup.");
				form.Close ();
				}
			MethodInfo fit = typeof (RunnerForm).GetMethod ("FitWindowBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
			Rectangle primary = new (0, 0, 1366, 728);
			Rectangle secondary = new (-1920, 0, 1920, 1040);
			Rectangle saved = new (-1800, 50, 1100, 700);
			Rectangle Fit (Rectangle value, Rectangle[] areas, Rectangle fallback) => (Rectangle)fit.Invoke (null, [value, new Size (1000, 650), areas, fallback])!;
			Require (Fit (saved, [primary, secondary], primary) == saved, "A connected monitor with negative coordinates was not retained.");
			Require (primary.Contains (Fit (saved, [primary], primary)), "Removed-monitor fallback left the window offscreen.");
			Rectangle small = new (0, 0, 800, 600);
			Require (Fit (saved, [small], small) == small, "A smaller display did not constrain the restored size.");
			Require (primary.Contains (Fit (new Rectangle (1300, 700, 1250, 850), [primary], primary)), "Partially offscreen placement was not constrained to the working area.");
			Require (Directory.GetFiles (directory, "*.tmp").Length == 0, "Window persistence left temporary files.");
			}
		finally
			{
			// Only files in this validation's uniquely created directory are removed.
			foreach (string file in Directory.GetFiles (directory))
				File.Delete (file);
			Directory.Delete (directory);
			}
		Console.WriteLine ("Window placement: immediate move/resize persistence, ignored minimize, normal/maximized/minimized restart, independent preferences, invalid settings, removed/negative-coordinate monitors and smaller working areas passed.");
		}

	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}