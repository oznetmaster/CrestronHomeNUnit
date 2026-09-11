// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CrestronHomeNUnit.Transport;

[DataContract]
public sealed class TestSuiteInfo
	{
	[DataMember]
	public string Id { get; set; } = "";
	[DataMember]
	public string Name { get; set; } = "";
	[DataMember (EmitDefaultValue = false)]
	public bool ManualOnly
		{
		get; set;
		}
	public override string ToString () => Name;
	}

public interface ITestSuiteProvider
	{
	IReadOnlyList<TestSuiteInfo> Suites
		{
		get;
		}
	}