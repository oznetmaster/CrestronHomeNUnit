// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeNUnit.Workflow;

/// <summary>Explicit enclosing transport budget. It does not alter test or restoration deadlines.</summary>
public sealed record WorkflowTransportBudget(TimeSpan Preflight, TimeSpan ActiveWork, TimeSpan Evidence,
    TimeSpan Verification, TimeSpan Cleanup, TimeSpan Margin, bool PreparedHumanReadiness = false)
{
    /// <summary>Only an explicitly prepared human-readiness run may have no enclosing wall-clock expiry.
    /// Keep cancellation, the active-work deadline and separate cleanup budgets enabled.</summary>
    public TimeSpan Timeout
    {
        get
        {
            TimeSpan[] phases = [Preflight, ActiveWork, Evidence, Verification, Cleanup, Margin];
            if (phases.Any(p => p < TimeSpan.Zero || p > TimeSpan.FromDays(1)) ||
                ActiveWork == TimeSpan.Zero || Cleanup == TimeSpan.Zero || Margin == TimeSpan.Zero)
                throw new ArgumentException("Declare nonnegative phase bounds and positive active-work, cleanup and margin budgets.");
            long ticks = 0;
            foreach (var phase in phases) ticks = checked(ticks + phase.Ticks);
            return PreparedHumanReadiness ? System.Threading.Timeout.InfiniteTimeSpan : TimeSpan.FromTicks(ticks);
        }
    }

    public void ValidateEnclosingTimeout(TimeSpan enclosing)
    {
        var required = Timeout;
        if (required == System.Threading.Timeout.InfiniteTimeSpan ? enclosing != required : enclosing < required)
            throw new ArgumentException("Enclosing timeout can truncate the declared work, human wait or cleanup. Do not launch.");
    }
}
