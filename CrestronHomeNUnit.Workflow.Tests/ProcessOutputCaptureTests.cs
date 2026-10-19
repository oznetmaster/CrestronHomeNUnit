// Copyright (c) 2026 Neil Colvin. MIT licensed.
using CrestronHomeNUnit.Workflow;
using NUnit.Framework;
namespace CrestronHomeNUnit.Workflow.Tests;
public sealed class ProcessOutputCaptureTests
{
    [Test]
    public async Task ClosedStreamPreservesAllOutput()
    {
        var capture = new ProcessOutputCapture();
        Assert.That(await capture.ReadAsync(new StringReader("first\nsecond\n"), default), Is.True);
        Assert.That(capture.Text, Is.EqualTo("first\nsecond\n"));
    }
    [Test]
    public async Task InheritedPipeCannotIgnoreCleanupDeadlineAndPartialOutputSurvives()
    {
        var capture = new ProcessOutputCapture();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.That(await capture.ReadAsync(new InheritedPipe(), timeout.Token).WaitAsync(TimeSpan.FromSeconds(2)), Is.False);
        Assert.That(capture.Text, Is.EqualTo("original failure\n"));
    }
    private sealed class InheritedPipe : TextReader
    {
        private bool emitted;
        private readonly TaskCompletionSource<int> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default)
        {
            if (emitted) return new(never.Task); // Simulates a descendant retaining an inherited pipe.
            emitted = true;
            "original failure\n".AsSpan().CopyTo(buffer.Span);
            return new("original failure\n".Length);
        }
    }
}
