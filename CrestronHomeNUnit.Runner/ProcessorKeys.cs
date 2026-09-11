// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;


namespace CrestronHomeNUnit.Runner;

internal static class ProcessorKeys
	{
	private static string PathName => Path.Combine (Environment.GetFolderPath (Environment.SpecialFolder.LocalApplicationData), "CrestronHomeNUnit", "ProcessorKeys.dat");
	public static string Get (string identity)
		{
		Dictionary<string, string> keys = Read ();
		return keys.TryGetValue (identity, out string? key) ? key : "";
		}
	public static void Set (string name, string value)
		{
		Dictionary<string, string> keys = Read ();
		keys[name] = value;
		Write (keys);
		}
	public static void SaveCredentials (string identity, string host, string user, string password)
		{
		Dictionary<string, string> keys = Read ();
		foreach (string name in new[] { identity, "host:" + host.Trim ().ToLowerInvariant () })
			{
			keys["user:" + name] = user;
			keys["password:" + name] = password;
			}
		Write (keys);
		}
	private static void Write (Dictionary<string, string> keys)
		{
		using var stream = new MemoryStream ();
		new DataContractJsonSerializer (typeof (Dictionary<string, string>)).WriteObject (stream, keys);
		Directory.CreateDirectory (Path.GetDirectoryName (PathName)!);
		File.WriteAllBytes (PathName, ProtectedData.Protect (stream.ToArray (), null, DataProtectionScope.CurrentUser));
		}
	private static Dictionary<string, string> Read ()
		{
		if (!File.Exists (PathName))
			return new Dictionary<string, string> (StringComparer.Ordinal);
		using var stream = new MemoryStream (ProtectedData.Unprotect (File.ReadAllBytes (PathName), null, DataProtectionScope.CurrentUser));
		return (Dictionary<string, string>)new DataContractJsonSerializer (typeof (Dictionary<string, string>)).ReadObject (stream)!;
		}
	}