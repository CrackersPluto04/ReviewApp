using System.Text.Json;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Sync;

namespace ReviewApp.Api.Tests.Unit;

// The pure rules of the offline sync: the three-way merge, the accepted client time and the op payload parser
public class SyncRulesTests
{
    /* FieldMerge */

    private static readonly ReviewFields Base = new(7m, "Good", "Cast", null, VisibilityLevel.Public);

    [Fact]
    public void Merge_OnlyMineChanged_TakesMine()
    {
        var mine = Base with { Score = 9m, Cons = "Long" };

        var outcome = FieldMerge.Review(Base, mine, Base);

        outcome.Value.ShouldBe(mine);
        outcome.HasConflicts.ShouldBeFalse();
        outcome.KeptServerChanges.ShouldBeFalse();
    }

    [Fact]
    public void Merge_DifferentFieldsChanged_KeepsBoth()
    {
        var mine = Base with { Score = 9m };
        var server = Base with { ReviewText = "Edited on the web", VisibilityLevel = VisibilityLevel.Private };

        var outcome = FieldMerge.Review(Base, mine, server);

        outcome.Value.ShouldBe(new ReviewFields(9m, "Edited on the web", "Cast", null, VisibilityLevel.Private));
        outcome.HasConflicts.ShouldBeFalse();
        outcome.KeptServerChanges.ShouldBeTrue();
    }

    [Fact]
    public void Merge_SameFieldChangedToTheSameValue_IsNoConflict()
    {
        var mine = Base with { Score = 4m };

        var outcome = FieldMerge.Review(Base, mine, mine);

        outcome.Value.ShouldBe(mine);
        outcome.HasConflicts.ShouldBeFalse();
    }

    [Fact]
    public void Merge_SameFieldChangedDifferently_IsAConflictOfThatField()
    {
        var mine = Base with { Score = 4m, ReviewText = "Mine", Pros = "Music" };
        var server = Base with { Score = 5m, ReviewText = "Theirs" };

        var outcome = FieldMerge.Review(Base, mine, server);

        outcome.Conflicts.ShouldBe(["score", "reviewText"]);
    }

    [Fact]
    public void Merge_NullAndEmptyTextAreTheSame()
    {
        var mine = Base with { Cons = "" };
        var server = Base with { Cons = null, Pros = "" };

        var outcome = FieldMerge.Review(Base with { Pros = null }, mine, server);

        outcome.HasConflicts.ShouldBeFalse();
        outcome.KeptServerChanges.ShouldBeFalse();
    }

    [Fact]
    public void MergeCollection_RenameAgainstVisibilityChange_KeepsBoth()
    {
        var @base = new CollectionFields("Trip", VisibilityLevel.Private);

        var outcome = FieldMerge.Collection(@base, @base with { Name = "Road trip" }, @base with { Visibility = VisibilityLevel.Public });

        outcome.Value.ShouldBe(new CollectionFields("Road trip", VisibilityLevel.Public));
        outcome.HasConflicts.ShouldBeFalse();
    }

    [Fact]
    public void MergeCollection_TwoRenames_IsAConflict_EvenOnlyByCase()
    {
        var @base = new CollectionFields("trip", VisibilityLevel.Private);

        FieldMerge.Collection(@base, @base with { Name = "Trip" }, @base with { Name = "TRIP" }).Conflicts.ShouldBe(["name"]);
    }

    /* ClientTimestamp */

    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ClientTime_WithinTheLastThirtyDays_IsKept()
    {
        var written = new DateTimeOffset(Now.AddDays(-29), TimeSpan.Zero);

        ClientTimestamp.Accept(written, Now).ShouldBe(written.UtcDateTime);
    }

    [Fact]
    public void ClientTime_InAnotherTimeZone_IsConvertedToUtc()
    {
        var written = new DateTimeOffset(2026, 11, 4, 13, 30, 0, TimeSpan.FromHours(2));

        var accepted = ClientTimestamp.Accept(written, Now);

        accepted.ShouldBe(new DateTime(2026, 11, 4, 11, 30, 0, DateTimeKind.Utc));
        accepted.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(-31 * 24 * 60.0)]   // older than 30 days
    [InlineData(10.0)]              // in the future
    public void ClientTime_OutOfBounds_IsReplacedByTheServerTime(double minutesFromNow)
    {
        ClientTimestamp.Accept(new DateTimeOffset(Now.AddMinutes(minutesFromNow), TimeSpan.Zero), Now).ShouldBe(Now);
    }

    [Fact]
    public void ClientTime_SlightlyAhead_IsTheServerTime()
    {
        // A device clock a minute fast: not in the future, not thrown away either
        ClientTimestamp.Accept(new DateTimeOffset(Now.AddMinutes(1), TimeSpan.Zero), Now).ShouldBe(Now);
    }

    [Fact]
    public void ClientTime_Missing_IsTheServerTime()
    {
        ClientTimestamp.Accept(null, Now).ShouldBe(Now);
    }

    /* SyncOpParser */

    private static JsonElement Payload(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static readonly object Media = new { type = 0, externalApiId = "603" };
    private static readonly object Fields = new { score = 8.5, reviewText = "Great", visibility = 1 };

    [Fact]
    public void Parse_ReviewCreate_ReadsEveryField()
    {
        var parsed = SyncOpParser.Parse(SyncOpTypes.ReviewCreate,
            Payload(new { media = Media, fields = Fields, createdAt = "2026-11-01T10:00:00+01:00" }), clientRef: "r1");

        var payload = parsed!.Payload.ShouldBeOfType<ReviewCreatePayload>();
        payload.Media!.Type.ShouldBe(MediaType.Movie);
        payload.Media.ExternalApiId.ShouldBe("603");
        payload.Fields!.ToFields().ShouldBe(new ReviewFields(8.5m, "Great", null, null, VisibilityLevel.Public));
        payload.CreatedAt!.Value.UtcDateTime.ShouldBe(new DateTime(2026, 11, 1, 9, 0, 0));
    }

    [Fact]
    public void Parse_ReferencesByIdOrByClientRef()
    {
        SyncOpParser.Parse(SyncOpTypes.ReviewDelete, Payload(new { review = new { id = 5 } })).ShouldNotBeNull();
        SyncOpParser.Parse(SyncOpTypes.ReviewDelete, Payload(new { review = new { @ref = "r1" } })).ShouldNotBeNull();
    }

    public static TheoryData<string, object> InvalidPayloads => new()
    {
        // Missing required parts
        { SyncOpTypes.ReviewCreate, new { fields = Fields } },
        { SyncOpTypes.ReviewCreate, new { media = Media, fields = new { reviewText = "No score", visibility = 1 } } },
        // Out of range or too long
        { SyncOpTypes.ReviewCreate, new { media = Media, fields = new { score = 11, visibility = 1 } } },
        { SyncOpTypes.ReviewCreate, new { media = Media, fields = new { score = 5, visibility = 1, pros = new string('x', 251) } } },
        { SyncOpTypes.ReviewCreate, new { media = new { type = 0, externalApiId = new string('1', 65) }, fields = Fields } },
        // Unknown enum values
        { SyncOpTypes.ReviewCreate, new { media = new { type = 7, externalApiId = "1" }, fields = Fields } },
        { SyncOpTypes.CollectionCreate, new { fields = new { name = "Trip", visibility = 9 } } },
        // A reference needs exactly one of id / ref, and a real id
        { SyncOpTypes.ReviewDelete, new { review = new { id = 5, @ref = "r1" } } },
        { SyncOpTypes.ReviewDelete, new { review = new { } } },
        { SyncOpTypes.ReviewDelete, new { review = new { id = 0 } } },
        // Wrong JSON types
        { SyncOpTypes.FollowSet, new { userId = "five", follow = true } },
        { SyncOpTypes.CollectionReorder, new { collection = new { id = 1 }, items = new object?[] { null } } },
        // A profile change has to change something, and the picture must be a web address
        { SyncOpTypes.ProfileUpdate, new { fields = new { } } },
        { SyncOpTypes.ProfileUpdate, new { fields = new { profilePictureUrl = "javascript:alert(1)" } } },
        { SyncOpTypes.NotificationMarkAllSeen, new { } }
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void Parse_InvalidPayload_IsNull(string type, object payload)
    {
        SyncOpParser.Parse(type, Payload(payload)).ShouldBeNull();
    }

    [Fact]
    public void Parse_UnknownTypeOrNonObjectPayload_IsNull()
    {
        SyncOpParser.Parse("review.rate", Payload(new { })).ShouldBeNull();
        SyncOpParser.Parse(SyncOpTypes.FollowSet, Payload(new[] { 1, 2 })).ShouldBeNull();
    }

    [Fact]
    public void Parse_ClientRefOnlyOnCreates()
    {
        SyncOpParser.Parse(SyncOpTypes.CollectionCreate, Payload(new { fields = new { name = "Trip", visibility = 0 } }), "c1").ShouldNotBeNull();
        SyncOpParser.Parse(SyncOpTypes.ReviewDelete, Payload(new { review = new { id = 5 } }), "c1").ShouldBeNull();
    }

    [Fact]
    public void Parse_ProfileFieldLeftOut_IsNotChanged_EmptyClears()
    {
        var payload = SyncOpParser.Parse(SyncOpTypes.ProfileUpdate, Payload(new { fields = new { bio = "" } }))!
            .Payload.ShouldBeOfType<ProfileUpdatePayload>();

        payload.Fields!.Bio.ShouldBe("");
        payload.Fields.ProfilePictureUrl.ShouldBeNull();
    }
}
