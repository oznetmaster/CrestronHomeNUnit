// Copyright (c) 2026 Neil Colvin. MIT licensed.
using NUnit.Framework;
namespace CrestronHomeNUnit.Workflow.Tests;
public sealed class WorkflowTransportBudgetTests
{
    private static WorkflowTransportBudget Budget(bool human=false) => new(TimeSpan.FromSeconds(240),
        TimeSpan.FromSeconds(900), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(300),
        TimeSpan.FromSeconds(645), TimeSpan.FromSeconds(315), human);
    [Test] public void OrdinaryBudgetReservesAllPhasesWithoutExtendingActiveWork()
    {
        var budget=Budget();
        Assert.That(budget.Timeout, Is.EqualTo(TimeSpan.FromSeconds(2700)));
        Assert.That(budget.ActiveWork, Is.EqualTo(TimeSpan.FromSeconds(900)));
        Assert.Throws<ArgumentException>(()=>budget.ValidateEnclosingTimeout(TimeSpan.FromSeconds(1200)));
        Assert.DoesNotThrow(()=>budget.ValidateEnclosingTimeout(TimeSpan.FromSeconds(2700)));
        Assert.Throws<ArgumentException>(()=>budget.ValidateEnclosingTimeout(System.Threading.Timeout.InfiniteTimeSpan));
    }
    [Test] public void HumanReadinessCannotBeEnclosedByFiniteSshTimeout()
    {
        var budget=Budget(true);
        Assert.That(budget.Timeout, Is.EqualTo(System.Threading.Timeout.InfiniteTimeSpan));
        Assert.Throws<ArgumentException>(()=>budget.ValidateEnclosingTimeout(TimeSpan.FromSeconds(4200)));
        Assert.DoesNotThrow(()=>budget.ValidateEnclosingTimeout(System.Threading.Timeout.InfiniteTimeSpan));
    }
    [Test] public void MissingCleanupCannotBeDisguisedByHumanWait()
    {
        var budget=Budget(true) with { Cleanup=TimeSpan.Zero };
        Assert.Throws<ArgumentException>(()=>{_=budget.Timeout;});
    }
}
