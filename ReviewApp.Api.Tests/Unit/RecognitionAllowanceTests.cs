using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Unit;

// The daily allowance, the one-at-a-time gate and the stored results behind "Show more"
public class RecognitionAllowanceTests
{
    [Fact]
    public void Allowance_CountsDownAndResetsAtTheNextUtcMidnight()
    {
        var now = new DateTime(2026, 11, 2, 23, 59, 0, DateTimeKind.Utc);

        var state = RecognitionAllowance.Compute(20, 6, now);

        state.ShouldBe(new AllowanceState(20, 6, 14, new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc)));
        state.IsExhausted.ShouldBeFalse();
        state.ResetsAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public void Allowance_NeverGoesBelowZero()
    {
        var state = RecognitionAllowance.Compute(20, 25, DateTime.UtcNow);

        state.Remaining.ShouldBe(0);
        state.IsExhausted.ShouldBeTrue();
    }

    [Fact]
    public void DayStart_IsUtcMidnight()
    {
        RecognitionAllowance.DayStart(new DateTime(2026, 11, 2, 0, 0, 1, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Gate_LetsOneRecognitionPerUserRun()
    {
        var gate = new RecognitionGate();

        var first = gate.TryEnter(1).ShouldNotBeNull();
        gate.TryEnter(1).ShouldBeNull();
        // Other users are not affected
        using (gate.TryEnter(2).ShouldNotBeNull()) { }

        first.Dispose();
        first.Dispose(); // a second release must not free someone else's later entry
        using var again = gate.TryEnter(1);
        again.ShouldNotBeNull();
        first.Dispose();
        gate.TryEnter(1).ShouldBeNull();
    }

    [Fact]
    public void Gate_IsReleasedWhenTheRecognitionThrows()
    {
        var gate = new RecognitionGate();

        Should.Throw<InvalidOperationException>(() =>
        {
            using var running = gate.TryEnter(7);
            throw new InvalidOperationException();
        });

        gate.TryEnter(7).ShouldNotBeNull();
    }

    [Fact]
    public void Store_GivesTheListOnlyToItsOwner()
    {
        var store = new RecognitionResultStore(new MemoryCache(new MemoryCacheOptions()), Options.Create(new RecognitionOptions()));
        RankedItem[] items = [new(new MediaDto { ID = "1Movie", ExternalApiID = "1", MediaType = MediaType.Movie }, RecognitionTier.Direct)];

        var id = store.Put(5, items);

        id.Length.ShouldBeGreaterThanOrEqualTo(20);
        store.Put(5, items).ShouldNotBe(id);
        store.Get(id, 5).ShouldBe(items);
        store.Get(id, 6).ShouldBeNull();
        store.Get("unknown", 5).ShouldBeNull();
        store.Get(null, 5).ShouldBeNull();
        store.Get(new string('a', 500), 5).ShouldBeNull();
    }
}
