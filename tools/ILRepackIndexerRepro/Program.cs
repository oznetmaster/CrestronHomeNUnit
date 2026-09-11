// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Reflection;

internal static class Program
	{
	public static int Main ()
		{
		Type named = typeof (NamedIndexers);
		Type ordinary = typeof (OrdinaryIndexers);
		Console.WriteLine ("Named properties: " + named.GetProperties ().Length);
		Console.WriteLine ("Named getters: " + named.GetMethods ().Count (method => method.Name == "get_Lookup"));
		Console.WriteLine ("Ordinary properties: " + ordinary.GetProperties ().Length);
		Console.WriteLine ("Direct two-argument call: " + new NamedIndexers ()[1, 2]);
		PropertyInfo reflected = named.GetProperty ("Lookup", new[] { typeof (int), typeof (int) });
		Console.WriteLine ("Reflected two-argument property: " + (reflected == null ? "MISSING" : reflected.GetValue (new NamedIndexers (), new object[] { 1, 2 })));
		return named.GetProperties ().Length == 2 && ordinary.GetProperties ().Length == 2 && reflected != null ? 0 : 1;
		}
	}
