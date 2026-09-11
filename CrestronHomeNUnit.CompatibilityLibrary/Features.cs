// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LanguageProbe;

public static class Features
	{
	public static int SumList (params List<int> values) => values.Sum ();
	public static int SumEnumerable (params IEnumerable<int> values) => values.Sum ();
	public static int SumSpan (params ReadOnlySpan<int> values)
		{
		var total = 0;
		foreach (var value in values)
			total += value;
		return total;
		}

	public static int CallParamsList () => SumList (10, 20, 12);
	public static int CallParamsEnumerable () => SumEnumerable (10, 20, 12);
	public static int CallParamsSpan () => SumSpan (10, 20, 12);
	public static int EscapeCharacter () => '\e';

	public static bool LockScope ()
		{
		var gate = new Lock ();
		bool held;
		lock (gate)
			{
			held = gate.IsHeldByCurrentThread;
			}
		return held && !gate.IsHeldByCurrentThread;
		}

	public static string NaturalMethodGroup ()
		{
		var selected = MethodGroups.Select<int>;
		return selected (42);
		}

	public static int ImplicitIndexInitializer ()
		{
		var sample = new BufferHolder { Values = { [^1] = 42 } };
		return sample.Values[2];
		}

	public static async Task<int> RefAndSpanInAsync ()
		{
		int result;
			{
			Span<int> values = stackalloc int[] { 20, 21 };
			ref int last = ref values[1];
			last++;
			result = values[0] + last;
			}
		await Task.Delay (5).ConfigureAwait (false);
		return result;
		}

	public static async Task<int> UnsafeInAsync ()
		{
		int result;
		unsafe
			{
			int value = 42;
			int* pointer = &value;
			result = *pointer;
			}
		await Task.Yield ();
		return result;
		}

	public static IEnumerable<int> RefAndUnsafeIterator ()
		{
		int result;
			{
			Span<int> values = stackalloc int[] { 40, 1 };
			ref int last = ref values[1];
			last++;
			result = values[0] + last;
			}
		unsafe
			{
			int* pointer = stackalloc int[1];
			*pointer = result + 1;
			result = *pointer;
			}
		yield return result;
		}

	public static int RefStructInterface ()
		{
		var value = new Reading (42);
		return value.Value;
		}

	public static string PartialMembers ()
		{
		var value = new PartialContainer { Name = "ready" };
		value[1] = 42;
		return $"{value.Name}:{value[1]}";
		}

	public static string PrioritizedOverload () => SelectOverload ("text");
	[OverloadResolutionPriority (1)]
	private static string SelectOverload (object value) => "priority-object";
	private static string SelectOverload (string value) => "string";

	public static int CollectionsAndPatterns ()
		{
		int[] middle = [2, 3];
		int[] values = [1, .. middle, 4];
		return values is [1, 2, 3, 4] ? values.Sum () : -1;
		}

	public static string RecordWithAndRequired ()
		{
		var first = new SampleRecord { Name = "initial", Count = 41 };
		var second = first with
			{
			Name = "updated",
			Count = 42
			};
		return $"{first.Name}:{second.Name}:{second.Count}";
		}

	public static int PrimaryConstructor () => new Counter (40).Add (2);
	public static string RawString () => """{"value":42}""";
	public static int StaticLambda () => ((Func<int, int>)(static value => value + 1)) (41);
	public static (int Value, string Name) NamedTuple () => (42, "ready");

	public static async IAsyncEnumerable<int> Stream ()
		{
		await Task.Yield ();
		yield return 20;
		yield return 22;
		}

	public static async Task<int> AwaitForeach ()
		{
		var total = 0;
		await foreach (var value in Stream ())
			total += value;
		return total;
		}

	public static async Task<bool> AwaitUsing ()
		{
		var resource = new AsyncResource ();
		await using (resource)
			{
			await Task.Yield ();
			}
		return resource.Disposed;
		}
	}

internal static class MethodGroups
	{
	public static string Select<T> (T value) where T : class => "class";
	public static string Select<T> (int value) where T : struct => "struct";
	}

internal sealed class BufferHolder
	{
	public int[] Values { get; } = new int[3];
	}
internal interface IReading
	{
	int Value
		{
		get;
		}
	}
internal ref struct Reading (int value) : IReading
	{
	public int Value => value;
	}
internal sealed class Counter (int initial)
	{
	public int Add (int amount) => initial + amount;
	}

public sealed record SampleRecord
	{
	public required string Name
		{
		get; init;
		}
	public int Count
		{
		get; init;
		}
	}

internal sealed class AsyncResource : IAsyncDisposable
	{
	public bool Disposed
		{
		get; private set;
		}
	public async ValueTask DisposeAsync ()
		{
		await Task.Yield ();
		Disposed = true;
		}
	}

internal partial class PartialContainer
	{
	public partial string Name
		{
		get; set;
		}
	public partial int this[int index] { get; set; }
	}

internal partial class PartialContainer
	{
	private string _name = string.Empty;
	private readonly int[] _values = new int[2];
	public partial string Name
		{
		get => _name; set => _name = value;
		}
	public partial int this[int index] { get => _values[index]; set => _values[index] = value; }
	}