// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Xml;

using CrestronHomeNUnit.Transport;

using Renci.SshNet;

internal static class SftpValidation
	{
	public static void Run (string projectUserFile)
		{
		var document = new XmlDocument { XmlResolver = null };
		document.Load (projectUserFile);
		string Setting (string name) => document.SelectSingleNode ("/Project/PropertyGroup/" + name)?.InnerText ?? throw new InvalidOperationException ("Missing deployment setting " + name);
		using var client = new SftpClient (Setting ("CrestronHomeIP"), Setting ("CrestronHomeFtpUser"), Setting ("CrestronHomeSftpPassword"));
		client.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
		client.OperationTimeout = TimeSpan.FromSeconds (10);
		client.Connect ();
		Console.WriteLine ("SFTP authentication with existing deployment credentials succeeded.");
		Console.WriteLine ("Shared data parent accessible: " + client.Exists ("/user/Data"));
		Console.WriteLine ("Updated test identity present: " + client.Exists (ProcessorIdentity.SHARED_DIRECTORY + "/ProcessorIdentity.txt"));
		}
	}