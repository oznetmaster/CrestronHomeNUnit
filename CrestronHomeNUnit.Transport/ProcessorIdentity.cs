// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace CrestronHomeNUnit.Transport;

public sealed class ProcessorIdentity
	{
	public const string SHARED_DIRECTORY = "/user/Data/CrestronHomeNUnit";
	public string Id
		{
		get;
		}
	public string PairingKey
		{
		get;
		}
	private ProcessorIdentity (string id, string pairingKey)
		{
		Id = id;
		PairingKey = pairingKey;
		}

	public static ProcessorIdentity LoadOrCreate (string directory)
		{
		Directory.CreateDirectory (directory);
		string path = Path.Combine (directory, "ProcessorIdentity.txt");
		// FileShare.None synchronizes separately merged assemblies and driver processes.
		for (int attempt = 0; ; attempt++)
			{
			try
				{
				using var gate = new FileStream (Path.Combine (directory, "ProcessorIdentity.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
				if (!File.Exists (path))
					{
					var bytes = new byte[16];
					using var random = RandomNumberGenerator.Create ();
					random.GetBytes (bytes);
					string temporary = path + "." + Guid.NewGuid ().ToString ("N");
					File.WriteAllLines (temporary, [Guid.NewGuid ().ToString ("N"), BitConverter.ToString (bytes).Replace ("-", "")]);
					try
						{
						File.Move (temporary, path);
						}
					finally { if (File.Exists (temporary)) File.Delete (temporary); }
					}
				string[] values = File.ReadAllLines (path);
				if (values.Length != 2 || !Guid.TryParseExact (values[0], "N", out _) || !Regex.IsMatch (values[1], "^[A-Fa-f0-9]{32}$"))
					throw new InvalidDataException ("The processor pairing identity is invalid. Restore its shared identity file.");
				return new ProcessorIdentity (values[0], values[1]);
				}
			catch (IOException) when (attempt < 30) { Thread.Sleep (100); }
			}
		}
	}