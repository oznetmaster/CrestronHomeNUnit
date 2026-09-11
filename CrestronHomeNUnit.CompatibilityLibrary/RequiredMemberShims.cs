// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Compiler metadata markers used only by this compatibility probe.
using System;
namespace System.Runtime.CompilerServices
	{
	[AttributeUsage (AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property)]
	internal sealed class RequiredMemberAttribute : Attribute
		{
		}
	[AttributeUsage (AttributeTargets.All, AllowMultiple = true)]
	internal sealed class CompilerFeatureRequiredAttribute (string featureName) : Attribute
		{
		public string FeatureName { get; } = featureName;
		public bool IsOptional
			{
			get; init;
			}
		}
	}
namespace System.Diagnostics.CodeAnalysis
	{
	[AttributeUsage (AttributeTargets.Constructor)]
	internal sealed class SetsRequiredMembersAttribute : Attribute
		{
		}
	}