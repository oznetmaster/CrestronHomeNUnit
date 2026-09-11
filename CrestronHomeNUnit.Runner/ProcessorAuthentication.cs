// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using CrestronHomeNUnit.Transport;

using Renci.SshNet;

namespace CrestronHomeNUnit.Runner;

public sealed class ProcessorConnection
	{
	public string Id
		{
		get;
		}
	public string Token
		{
		get;
		}
	public ProcessorConnection (string id, string token)
		{
		Id = id;
		Token = token;
		}
	public static ProcessorConnection Parse (string text)
		{
		string[] parts = text.Trim ().Split (['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length != 2 || !Guid.TryParseExact (parts[0], "N", out _) || !Regex.IsMatch (parts[1], "^[A-Fa-f0-9]{32}$"))
			throw new InvalidDataException ("The processor test identity is invalid. Deploy and activate an updated test package.");
		return new ProcessorConnection (parts[0], parts[1]);
		}
	}

public static class ProcessorAuthentication
	{
	public static Task<ProcessorConnection> AuthenticateAsync (string host, string user, string password, string expectedProcessorId) => Task.Run (() =>
		{
			if (string.IsNullOrWhiteSpace (user) || string.IsNullOrEmpty (password))
				throw new InvalidOperationException ("Enter the processor's SFTP username and password.");
			string known = ProcessorKeys.Get ("fingerprint:" + host.ToLowerInvariant ());
			if (known.Length == 0 && expectedProcessorId.Length != 0)
				known = ProcessorKeys.Get ("fingerprint-id:" + expectedProcessorId);
			string fingerprint = "";
			using var client = new SftpClient (host, user, password);
			client.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
			client.OperationTimeout = TimeSpan.FromSeconds (10);
			client.HostKeyReceived += (_, args) =>
				{
					fingerprint = args.FingerPrintSHA256;
					args.CanTrust = known.Length == 0 || known == fingerprint;
				};
			client.Connect ();
			using Stream input = client.OpenRead (ProcessorIdentity.SHARED_DIRECTORY + "/ProcessorIdentity.txt");
			var buffer = new byte[513];
			int count = 0;
			while (count < buffer.Length)
				{
				int read = input.Read (buffer, count, buffer.Length - count);
				if (read == 0)
					break;
				count += read;
				}
			if (count > 512)
				throw new InvalidDataException ("The processor test identity is too large.");
			ProcessorConnection identity = ProcessorConnection.Parse (Encoding.UTF8.GetString (buffer, 0, count));
			if (expectedProcessorId.Length != 0 && expectedProcessorId != identity.Id)
				throw new InvalidOperationException ("The discovered processor identity has changed. Find packages again.");
			ProcessorKeys.Set ("fingerprint:" + host.ToLowerInvariant (), fingerprint);
			ProcessorKeys.Set ("fingerprint-id:" + identity.Id, fingerprint);
			return identity;
		});
	}