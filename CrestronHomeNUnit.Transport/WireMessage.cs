// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;

namespace CrestronHomeNUnit.Transport;

[DataContract]
public sealed class WireMessage
	{
	[DataMember]
	public int Version { get; set; } = 1;

	[DataMember]
	public string Kind { get; set; } = "";

	[DataMember]
	public string RequestId { get; set; } = "";

	[DataMember]
	public string TargetId { get; set; } = "";

	[DataMember]
	public string Suite { get; set; } = "self-tests";

	[DataMember (EmitDefaultValue = false)]
	public bool EnableLiveTests
		{
		get; set;
		}

	[DataMember]
	public string Text { get; set; } = "";

	[DataMember]
	public string Xml { get; set; } = "";

	[DataMember]
	public int ExitCode
		{
		get; set;
		}

	[DataMember]
	public List<string> TestNames { get; set; } = [];

	[DataMember (EmitDefaultValue = false)]
	public List<TestSuiteInfo>? Suites
		{
		get; set;
		}
	[DataMember (EmitDefaultValue = false)]
	public string ProtectedData { get; set; } = "";
	[IgnoreDataMember]
	public List<TestInputFile>? TestInputs
		{
		get; set;
		}

	public static WireMessage Reply (WireMessage request, string kind, string text = "") => new ()
		{
		Kind = kind,
		RequestId = request.RequestId,
		Suite = request.Suite,
		Text = text
		};
	}

public interface ITestExecutionHost
	{
	Task<WireMessage> ExecuteAsync (WireMessage request, Action<WireMessage> publish);
	bool Cancel (string operationId);
	}