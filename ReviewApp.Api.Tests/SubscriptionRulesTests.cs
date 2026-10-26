using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests;

public class SubscriptionRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private static SubscriptionState Active(DateTime? periodEnd, bool cancel = false) =>
        new(SubscriptionStatus.Active, cancel, periodEnd, null);

    private static ProviderSubscriptionSnapshot Snapshot(SubscriptionStatus status, bool cancel = false, DateTime? periodEnd = null) =>
        new("cus_1", "sub_1", status, cancel, periodEnd ?? Now.AddDays(30));

    [Fact]
    public void IsEntitled_NoSubscription_IsFree()
    {
        Assert.False(SubscriptionRules.IsEntitled(SubscriptionState.Empty, Now));
        Assert.Equal(SubscriptionTier.Free, SubscriptionRules.GetTier(SubscriptionState.Empty, Now));
    }

    [Fact]
    public void IsEntitled_ActiveWithinPeriod_IsPremium()
    {
        var state = Active(Now.AddDays(10));

        Assert.True(SubscriptionRules.IsEntitled(state, Now));
        Assert.Equal(SubscriptionTier.Premium, SubscriptionRules.GetTier(state, Now));
    }

    [Fact]
    public void IsEntitled_ActiveWithoutPeriodEnd_IsFree()
    {
        Assert.False(SubscriptionRules.IsEntitled(Active(null), Now));
    }

    [Fact]
    public void IsEntitled_ActiveKeepsAccessDuringRenewalSlack()
    {
        var end = Now.AddHours(-1);

        Assert.True(SubscriptionRules.IsEntitled(Active(end), Now));
        Assert.True(SubscriptionRules.IsEntitled(Active(end), end + SubscriptionRules.RenewalSlack));
        Assert.False(SubscriptionRules.IsEntitled(Active(end), end + SubscriptionRules.RenewalSlack + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IsEntitled_CancelledKeepsAccessUntilPeriodEnd()
    {
        var state = Active(Now.AddDays(3), cancel: true);

        Assert.True(SubscriptionRules.IsEntitled(state, Now));
        Assert.False(SubscriptionRules.WillRenew(state));
    }

    [Fact]
    public void IsEntitled_PastDueKeepsAccessForGracePeriod()
    {
        var since = Now.AddDays(-2);
        var state = new SubscriptionState(SubscriptionStatus.PastDue, false, since, since);

        Assert.True(SubscriptionRules.IsEntitled(state, Now));
        Assert.True(SubscriptionRules.IsEntitled(state, since + SubscriptionRules.PastDueGrace));
        Assert.False(SubscriptionRules.IsEntitled(state, since + SubscriptionRules.PastDueGrace + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void IsEntitled_PastDueWithoutDates_IsFree()
    {
        var state = new SubscriptionState(SubscriptionStatus.PastDue, false, null, null);

        Assert.False(SubscriptionRules.IsEntitled(state, Now));
    }

    [Theory]
    [InlineData(SubscriptionStatus.None)]
    [InlineData(SubscriptionStatus.Ended)]
    public void IsEntitled_NotRunning_IsFreeEvenWithFuturePeriodEnd(SubscriptionStatus status)
    {
        var state = new SubscriptionState(status, false, Now.AddDays(20), null);

        Assert.False(SubscriptionRules.IsEntitled(state, Now));
    }

    [Fact]
    public void WillRenew_OnlyWhenActiveAndNotCancelled()
    {
        Assert.True(SubscriptionRules.WillRenew(Active(Now.AddDays(1))));
        Assert.False(SubscriptionRules.WillRenew(new SubscriptionState(SubscriptionStatus.PastDue, false, Now, Now)));
    }

    [Fact]
    public void AccessEndsAt_IsPeriodEndOrGraceEnd()
    {
        var end = Now.AddDays(4);
        var pastDue = new SubscriptionState(SubscriptionStatus.PastDue, false, Now.AddDays(-1), Now);

        Assert.Equal(end, SubscriptionRules.AccessEndsAt(Active(end)));
        Assert.Equal(Now + SubscriptionRules.PastDueGrace, SubscriptionRules.AccessEndsAt(pastDue));
        Assert.Null(SubscriptionRules.AccessEndsAt(SubscriptionState.Empty));
    }

    [Fact]
    public void Apply_PastDue_StartsGraceOnFirstFailureOnly()
    {
        var first = SubscriptionRules.Apply(Active(Now), Snapshot(SubscriptionStatus.PastDue), Now);
        var retry = SubscriptionRules.Apply(first, Snapshot(SubscriptionStatus.PastDue), Now.AddDays(2));

        Assert.Equal(Now, first.PastDueSince);
        Assert.Equal(Now, retry.PastDueSince);
    }

    [Fact]
    public void Apply_RecoveryClearsPastDue()
    {
        var pastDue = new SubscriptionState(SubscriptionStatus.PastDue, false, Now, Now);

        var next = SubscriptionRules.Apply(pastDue, Snapshot(SubscriptionStatus.Active), Now);

        Assert.Equal(SubscriptionStatus.Active, next.Status);
        Assert.Null(next.PastDueSince);
    }

    [Fact]
    public void Apply_EndedClearsCancelFlag()
    {
        var next = SubscriptionRules.Apply(Active(Now, cancel: true), Snapshot(SubscriptionStatus.Ended, cancel: true), Now);

        Assert.Equal(SubscriptionStatus.Ended, next.Status);
        Assert.False(next.CancelAtPeriodEnd);
    }

    [Fact]
    public void Apply_KeepsStoredPeriodEndWhenProviderSendsNone()
    {
        var end = Now.AddDays(5);
        var snapshot = new ProviderSubscriptionSnapshot("cus_1", "sub_1", SubscriptionStatus.Active, false, null);

        var next = SubscriptionRules.Apply(Active(end), snapshot, Now);

        Assert.Equal(end, next.CurrentPeriodEnd);
    }

    [Fact]
    public void Apply_SameSnapshotTwice_IsIdempotent()
    {
        var snapshot = Snapshot(SubscriptionStatus.Active, cancel: true);

        var once = SubscriptionRules.Apply(SubscriptionState.Empty, snapshot, Now);
        var twice = SubscriptionRules.Apply(once, snapshot, Now.AddMinutes(1));

        Assert.Equal(once, twice);
        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(once, twice));
    }

    [Fact]
    public void ExpireIfLapsed_EndsActiveAfterPeriodAndSlack()
    {
        var state = Active(Now.AddDays(-1), cancel: true);

        var expired = SubscriptionRules.ExpireIfLapsed(state, Now);

        Assert.NotNull(expired);
        Assert.Equal(SubscriptionStatus.Ended, expired!.Status);
        Assert.False(expired.CancelAtPeriodEnd);
    }

    [Fact]
    public void ExpireIfLapsed_LeavesEntitledAndNotRunningAlone()
    {
        Assert.Null(SubscriptionRules.ExpireIfLapsed(Active(Now.AddDays(1)), Now));
        Assert.Null(SubscriptionRules.ExpireIfLapsed(SubscriptionState.Empty, Now));
        Assert.Null(SubscriptionRules.ExpireIfLapsed(new SubscriptionState(SubscriptionStatus.Ended, false, Now.AddDays(-9), null), Now));
    }

    [Fact]
    public void DetectTransitions_FirstPayment_IsStarted()
    {
        var next = Active(Now.AddDays(30));

        Assert.Equal(SubscriptionTransitions.Started, SubscriptionRules.DetectTransitions(SubscriptionState.Empty, next));
    }

    [Fact]
    public void DetectTransitions_ResubscribeAfterEnded_IsStarted()
    {
        var ended = new SubscriptionState(SubscriptionStatus.Ended, false, Now.AddDays(-3), null);

        Assert.Equal(SubscriptionTransitions.Started, SubscriptionRules.DetectTransitions(ended, Active(Now.AddDays(30))));
    }

    [Fact]
    public void DetectTransitions_FailedRenewal_IsPaymentFailed()
    {
        var pastDue = new SubscriptionState(SubscriptionStatus.PastDue, false, Now, Now);

        Assert.Equal(SubscriptionTransitions.PaymentFailed, SubscriptionRules.DetectTransitions(Active(Now), pastDue));
        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(pastDue, pastDue));
    }

    [Fact]
    public void DetectTransitions_RecoveryFromPastDue_IsNotStarted()
    {
        var pastDue = new SubscriptionState(SubscriptionStatus.PastDue, false, Now, Now);

        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(pastDue, Active(Now.AddDays(30))));
    }

    [Fact]
    public void DetectTransitions_CancelAndResume()
    {
        var active = Active(Now.AddDays(10));
        var cancelled = active with { CancelAtPeriodEnd = true };

        Assert.Equal(SubscriptionTransitions.Cancelled, SubscriptionRules.DetectTransitions(active, cancelled));
        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(cancelled, active));
    }

    [Fact]
    public void DetectTransitions_RunningToEnded_IsEnded()
    {
        var ended = new SubscriptionState(SubscriptionStatus.Ended, false, Now, null);

        Assert.Equal(SubscriptionTransitions.Ended, SubscriptionRules.DetectTransitions(Active(Now, cancel: true), ended));
        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(ended, ended));
        Assert.Equal(SubscriptionTransitions.None, SubscriptionRules.DetectTransitions(SubscriptionState.Empty, ended));
    }

    [Theory]
    [InlineData("active", SubscriptionStatus.Active)]
    [InlineData("trialing", SubscriptionStatus.Active)]
    [InlineData("past_due", SubscriptionStatus.PastDue)]
    [InlineData("unpaid", SubscriptionStatus.PastDue)]
    [InlineData("canceled", SubscriptionStatus.Ended)]
    [InlineData("incomplete_expired", SubscriptionStatus.Ended)]
    [InlineData("paused", SubscriptionStatus.Ended)]
    [InlineData("incomplete", SubscriptionStatus.None)]
    [InlineData("something_new", SubscriptionStatus.None)]
    [InlineData(null, SubscriptionStatus.None)]
    public void MapStripeStatus_MapsKnownStatuses(string? stripeStatus, SubscriptionStatus expected)
    {
        Assert.Equal(expected, SubscriptionRules.MapStripeStatus(stripeStatus));
    }
}
