// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CrestronHomeNUnit.Runtime;

[DataContract]
public sealed class TestSuiteDefinition
	{
	[DataMember (IsRequired = true)]
	public string Id { get; set; } = "";
	[DataMember (IsRequired = true)]
	public string Name { get; set; } = "";
	[DataMember (IsRequired = true)]
	public string FilterXml { get; set; } = "";
	[DataMember]
	public bool ManualOnly
		{
		get; set;
		}
	[DataMember]
	public int ExpectedCount
		{
		get; set;
		}

	public static IReadOnlyList<TestSuiteDefinition> BuiltIn => new[]
	{
		  new TestSuiteDefinition { Id = "self-tests", Name = "NUnit framework self-tests", FilterXml = "<filter><namespace re='1'>^NUnit[.]Framework[.]Tests[.](Assertions|Constraints|Syntax)($|[.])</namespace></filter>" },
		  new TestSuiteDefinition { Id = "compatibility", Name = "C# compatibility", FilterXml = "<filter><or><class>LanguageTests</class><class>AsyncLifecycleTests</class></or></filter>" }
	 };
	}