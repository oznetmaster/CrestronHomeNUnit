// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

public class NamedIndexers
	{
	[IndexerName ("Lookup")]
	public string this[int x]
		{
		get { return "one"; }
		}

	[IndexerName ("Lookup")]
	public string this[int x, int y]
		{
		get { return "two"; }
		}
	}

public class OrdinaryIndexers
	{
	public string this[int x]
		{
		get { return "one"; }
		}

	public string this[int x, int y]
		{
		get { return "two"; }
		}
	}
