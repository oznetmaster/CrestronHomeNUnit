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
	public static async Task<ProcessorConnection> AuthenticateAsync (string host, string user, string password, string expectedProcessorId)
		{
		Client.ProcessorConnection identity = await Client.ProcessorAuthentication.AuthenticateAsync (host, user, password, expectedProcessorId,
			 ProcessorKeys.Get, ProcessorKeys.Set, trustUnknownHost: true).ConfigureAwait (false);
		return new ProcessorConnection (identity.Id, identity.Token);
		}
	}