// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

using Renci.SshNet;

internal static class SshNames
	{
	public static async Task ReadAsync (string userFile, string[] suffixes)
		{
		var settings = new XmlDocument { XmlResolver = null };
		settings.Load (userFile);
		string Value (string name) => settings.SelectSingleNode ("/Project/PropertyGroup/" + name)!.InnerText;
		string prefix = string.Join (".", Value ("CrestronHomeIP").Split ('.').Take (3)) + ".";
		foreach (string suffix in suffixes)
			{
			string host = prefix + suffix;
			try
				{
				using var client = new SshClient (host, Value ("CrestronHomeFtpUser"), Value ("CrestronHomeSftpPassword"));
				client.ConnectionInfo.Timeout = TimeSpan.FromSeconds (10);
				client.ConnectionInfo.AuthenticationBanner += (_, args) => Console.WriteLine (host + " login banner: " + args.BannerMessage.Trim ());
				client.Connect ();
				using ShellStream shell = client.CreateShellStream ("vt100", 80, 24, 800, 600, 4096);
				shell.Write ("\r");
				await Task.Delay (2000).ConfigureAwait (false);
				Console.WriteLine (host + " console greeting: " + shell.Read ().Trim ());
				}
			catch (Exception exception) { Console.WriteLine (host + ": " + exception.Message); }
			}
		}
	}