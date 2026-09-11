// Copyright (c) 2026 Neil Colvin.
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using LanguageProbe;

using NUnit.Framework;


[TestFixture]
public sealed class LanguageTests
	{
	[Test] public void ParamsListInsideLibrary () => Assert.That (Features.CallParamsList (), Is.EqualTo (42));
	[Test] public void ParamsEnumerableInsideLibrary () => Assert.That (Features.CallParamsEnumerable (), Is.EqualTo (42));
	[Test] public void ParamsSpanInsideLibrary () => Assert.That (Features.CallParamsSpan (), Is.EqualTo (42));
	[Test] public void ParamsListAcrossAssembly () => Assert.That (Features.SumList (20, 22), Is.EqualTo (42));
	[Test] public void ParamsSpanAcrossAssembly () => Assert.That (Features.SumSpan (20, 22), Is.EqualTo (42));
	[Test] public void EscapeSequence () => Assert.That (Features.EscapeCharacter (), Is.EqualTo (27));
	[Test] public void NewLock () => Assert.That (Features.LockScope (), Is.True);
	[Test] public void NaturalMethodGroup () => Assert.That (Features.NaturalMethodGroup (), Is.EqualTo ("struct"));
	[Test] public void ImplicitIndex () => Assert.That (Features.ImplicitIndexInitializer (), Is.EqualTo (42));
	[Test] public async Task RefAndSpanInAsync () => Assert.That (await Features.RefAndSpanInAsync (), Is.EqualTo (42));
	[Test] public async Task UnsafeInAsync () => Assert.That (await Features.UnsafeInAsync (), Is.EqualTo (42));
	[Test] public void RefAndUnsafeIterator () => Assert.That (Features.RefAndUnsafeIterator ().Single (), Is.EqualTo (43));
	[Test] public void RefStructInterface () => Assert.That (Features.RefStructInterface (), Is.EqualTo (42));
	[Test] public void PartialPropertiesAndIndexers () => Assert.That (Features.PartialMembers (), Is.EqualTo ("ready:42"));
	[Test] public void OverloadPriority () => Assert.That (Features.PrioritizedOverload (), Is.EqualTo ("priority-object"));
	[Test] public void CollectionExpressionsAndListPatterns () => Assert.That (Features.CollectionsAndPatterns (), Is.EqualTo (10));
	[Test] public void RecordsInitRequiredWith () => Assert.That (Features.RecordWithAndRequired (), Is.EqualTo ("initial:updated:42"));
	[Test] public void PrimaryConstructor () => Assert.That (Features.PrimaryConstructor (), Is.EqualTo (42));
	[Test] public void RawString () => Assert.That (Features.RawString (), Is.EqualTo ("{\"value\":42}"));
	[Test] public void StaticLambda () => Assert.That (Features.StaticLambda (), Is.EqualTo (42));
	[Test] public void NamedTuple () => Assert.That (Features.NamedTuple (), Is.EqualTo ((42, "ready")));
	[Test] public async Task AsyncEnumerableInBody () => Assert.That (await Features.AwaitForeach (), Is.EqualTo (42));
	[Test] public async Task AsyncDisposal () => Assert.That (await Features.AwaitUsing (), Is.True);
	[Test] public async ValueTask ValueTaskTest () => Assert.That (await Features.RefAndSpanInAsync (), Is.EqualTo (42));
	[TestCase (ExpectedResult = 42)] public async ValueTask<int> GenericValueTaskTest () => await Features.RefAndSpanInAsync ();
	[Test] public CustomAwaitable CustomAwaitableTest () => new CustomAwaitable (Task.Delay (5));
	[Test] public void AsyncExceptionAssertion () => Assert.ThrowsAsync<InvalidOperationException> (async () => { await Task.Yield (); throw new InvalidOperationException ("expected"); });

	public static IEnumerable<object[]> SyncCases ()
		{
		yield return new object[] { 42 };
		}
	public static async Task<IEnumerable<object[]>> TaskCases ()
		{
		await Task.Yield ();
		return SyncCases ();
		}
	public static async IAsyncEnumerable<object[]> AsyncCases ()
		{
		await Task.Yield ();
		yield return new object[] { 42 };
		}

	[TestCaseSource (nameof (SyncCases))] public void SyncIteratorSource (int value) => Assert.That (value, Is.EqualTo (42));
	[TestCaseSource (nameof (TaskCases))] public void TaskSource (int value) => Assert.That (value, Is.EqualTo (42));
	[TestCaseSource (nameof (AsyncCases))] public void AsyncIteratorSource (int value) => Assert.That (value, Is.EqualTo (42));

	public static IEnumerable<object[]> CollectionCases ()
		{
		yield return new object[] { new List<int> { 20, 22 } };
		}
	[TestCaseSource (nameof (CollectionCases))] public void ParamsCollectionAsSingleArgument (params List<int> values) => Assert.That (values.Sum (), Is.EqualTo (42));

	[TestCase (20, 22)]
	public void ParamsCollectionExpandedByCompiler (int first, int second) => Assert.That (Features.SumList (first, second), Is.EqualTo (42));
	}

public readonly struct CustomAwaitable (Task task)
	{
	public TaskAwaiter GetAwaiter () => task.GetAwaiter ();
	}

[TestFixture]
public sealed class AsyncLifecycleTests
	{
	private int _state;
	[SetUp]
	public async Task SetUp ()
		{
		_state = await Features.RefAndSpanInAsync ();
		}
	[TearDown]
	public async ValueTask TearDown ()
		{
		await Task.Yield ();
		_state = 0;
		}
	[Test] public void First () => Assert.That (_state, Is.EqualTo (42));
	[Test] public void Second () => Assert.That (_state, Is.EqualTo (42));
	}