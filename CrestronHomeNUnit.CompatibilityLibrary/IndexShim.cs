// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

// Minimal complete Index value representation for the compiler-driven probe.
namespace System;

internal readonly struct Index
	{
	private readonly int _value;
	public Index (int value, bool fromEnd = false)
		{
		if (value < 0)
			throw new ArgumentOutOfRangeException (nameof (value));
		_value = fromEnd ? ~value : value;
		}
	public int Value => _value < 0 ? ~_value : _value;
	public bool IsFromEnd => _value < 0;
	public int GetOffset (int length) => IsFromEnd ? length - Value : Value;
	public static implicit operator Index (int value) => new (value);
	}