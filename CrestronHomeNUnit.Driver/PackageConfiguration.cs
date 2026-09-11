// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

using CrestronHomeNUnit.Runtime;

namespace CrestronHomeNUnit.Driver;

[DataContract]
public sealed class PackageConfiguration
	{
	[DataMember]
	public string Name { get; set; } = "NUnit Test Host";
	[DataMember]
	public int Port
		{
		get; set;
		}
	[DataMember (IsRequired = true)]
	public List<TestSuiteDefinition> Suites { get; set; } = [];

	public static PackageConfiguration Load ()
		{
		using Stream stream = Assembly.GetExecutingAssembly ().GetManifestResourceStream ("ProcessorTests.json")
			 ?? throw new InvalidOperationException ("The package has no ProcessorTests.json resource.");
		var configuration = (PackageConfiguration)new DataContractJsonSerializer (typeof (PackageConfiguration)).ReadObject (stream)!;
		if (configuration.Port < 0 || configuration.Port > 65535 || configuration.Suites == null || configuration.Suites.Count == 0)
			throw new InvalidOperationException ("The package must define a valid TCP port (zero for automatic assignment) and at least one suite.");
		var ids = new HashSet<string> (StringComparer.Ordinal);
		foreach (TestSuiteDefinition suite in configuration.Suites)
			{
			if (suite.Id is null || !Regex.IsMatch (suite.Id, "^[a-z0-9][a-z0-9-]{0,63}$") || !ids.Add (suite.Id) || string.IsNullOrWhiteSpace (suite.Name))
				throw new InvalidOperationException ("Suite IDs must be unique lowercase names containing letters, digits or hyphens.");
			if (string.IsNullOrWhiteSpace (suite.FilterXml))
				throw new InvalidOperationException ("Every suite must declare its test filter.");
			}
		return configuration;
		}

	public TestExecutionService CreateService (string workDirectory, string testDataDirectory) => new (workDirectory, _ => Assembly.GetExecutingAssembly (), Suites, testDataDirectory);
	}

// Also used by the desktop package validator: it executes the exact merged assembly and filters.
public static class PackageTestHost
	{
	public static int Run (string workDirectory, bool explore)
		{
		PackageConfiguration configuration = PackageConfiguration.Load ();
		foreach (TestSuiteDefinition suite in configuration.Suites)
			{
			// Package checks may discover manual suites, but never execute device operations.
			if (!explore && suite.ManualOnly)
				continue;
			string directory = Path.Combine (workDirectory, suite.Id);
			string dataDirectory = Path.Combine (Path.GetDirectoryName (Assembly.GetExecutingAssembly ().Location)!, "IncludeInPkg");
			int result = EmbeddedTestHost.RunWithProgress (Assembly.GetExecutingAssembly (), Path.Combine (workDirectory, suite.Id), Console.Out,
				 explore, suite.Id, null, null, CancellationToken.None, suite.FilterXml, dataDirectory);
			if (result != 0)
				return result;
			if (suite.ExpectedCount > 0)
				{
				var xml = new XmlDocument { XmlResolver = null };
				xml.Load (Path.Combine (directory, explore ? "TestTree.xml" : "TestResult.xml"));
				if (int.Parse (xml.DocumentElement!.GetAttribute ("testcasecount")) != suite.ExpectedCount)
					throw new InvalidOperationException ("Unexpected test count for " + suite.Id);
				}
			}
		return 0;
		}
	}