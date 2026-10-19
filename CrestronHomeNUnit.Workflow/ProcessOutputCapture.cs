// Copyright (c) 2026 Neil Colvin. MIT licensed.
using System.Text;
namespace CrestronHomeNUnit.Workflow;

internal sealed class ProcessOutputCapture
{
    private readonly StringBuilder text = new();
    internal string Text => text.ToString();
    internal async Task<bool> ReadAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        try
        {
            while (true)
            {
                // WaitAsync also bounds readers that do not honour ReadAsync cancellation.
                var pending = reader.ReadAsync(buffer.AsMemory(), token).AsTask();
                int count;
                try { count = await pending.WaitAsync(token).ConfigureAwait(false); }
                catch
                {
                    _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    throw;
                }
                if (count == 0) return true;
                text.Append(buffer, 0, count);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }
}
