// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

using CrestronHomeNUnit.Runner;
using CrestronHomeNUnit.Transport;

internal static class RunnerPackageInputsValidation
	{
	public static void Run ()
		{
		Type inputs = typeof (RunnerForm).Assembly.GetType ("CrestronHomeNUnit.Runner.RunnerTestInputs", true)!;
		MethodInfo migrate = inputs.GetMethod ("TryMigratePackage", BindingFlags.Static | BindingFlags.NonPublic)!;
		var profiles = new Dictionary<string, string[]>
			{
			["processor/live"] = ["settings.json"],
			["other-processor/live"] = ["other-settings.json"],
			["processor/unrelated"] = ["unrelated-settings.json"]
			};
		string scope = "package/processor/primary";
		string[] legacy = ["processor/unit", "processor/live", "processor/control"];
		bool Migrate () => (bool)migrate.Invoke (null, [profiles, scope, legacy])!;
		Require (Migrate () && profiles[scope][0] == "settings.json", "Existing live inputs did not migrate to the package.");
		Require (!Migrate (), "Migration replaced an existing package selection.");
		profiles[scope] = [];
		Require (!Migrate () && profiles[scope].Length == 0, "Cleared inputs were restored from an old suite.");
		profiles.Remove (scope);
		profiles["processor/control"] = ["different-settings.json"];
		Require (!Migrate () && !profiles.ContainsKey (scope), "Conflicting old selections were silently combined.");
		profiles["processor/control"] = ["SETTINGS.JSON"];
		Require (Migrate (), "Equivalent Windows paths were treated as conflicting.");
		profiles.Remove (scope);
		profiles.Remove ("processor/live");
		profiles.Remove ("processor/control");
		Require (!Migrate (), "Inputs leaked from another processor or package.");

		using var form = new RunnerForm ((_, _, _, _) => Task.FromResult (new ProcessorConnection ("input-validation", "unused")));
		ComboBox suites = Field<ComboBox> (form, "_suite");
		suites.Items.Clear ();
		string unique = "input-validation-" + Guid.NewGuid ().ToString ("N");
		suites.Items.AddRange ([new TestSuiteInfo { Id = unique }, new TestSuiteInfo { Id = unique + "-live" }, new TestSuiteInfo { Id = unique + "-control" }]);
		typeof (RunnerForm).GetField ("_processorId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, "processor-one");
		string Scope () => (string)typeof (RunnerForm).GetMethod ("InputScope", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke (form, null)!;
		suites.SelectedIndex = 0;
		string original = Scope ();
		suites.SelectedIndex = 1;
		Require (Scope () == original, "Live suite changed the input scope.");
		suites.SelectedIndex = 2;
		Require (Scope () == original, "Control suite changed the input scope.");
		typeof (RunnerForm).GetField ("_connecting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, true);
		Field<NumericUpDown> (form, "_port").Value = 32123;
		typeof (RunnerForm).GetField ("_connecting", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, false);
		Require (Scope () == original, "A port change lost the package input scope.");
		typeof (RunnerForm).GetField ("_processorId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, "processor-two");
		Require (Scope () != original, "Different processors shared an input scope.");
		typeof (RunnerForm).GetField ("_processorId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue (form, "processor-one");
		((TestSuiteInfo)suites.Items[0]!).Id = unique + "-another-package";
		Require (Scope () != original, "Different packages shared an input scope.");
		Console.WriteLine ("Runner package inputs: suite switching, endpoint changes, processor/package isolation, migration, conflicts and clearing passed.");
		}
	private static T Field<T> (RunnerForm form, string name) => (T)typeof (RunnerForm).GetField (name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue (form)!;
	private static void Require (bool condition, string message)
		{
		if (!condition)
			throw new InvalidOperationException (message);
		}
	}