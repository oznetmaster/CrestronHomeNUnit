// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Windows.Forms;

namespace CrestronHomeNUnit.Runner;

internal static class Program
	{
	[STAThread]
	private static void Main ()
		{
		Application.EnableVisualStyles ();
		Application.SetCompatibleTextRenderingDefault (false);
		Application.Run (new RunnerForm ());
		}
	}