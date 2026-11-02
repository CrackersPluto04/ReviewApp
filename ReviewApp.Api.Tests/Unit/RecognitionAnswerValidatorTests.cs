using System.Text.Json;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Recognition;

namespace ReviewApp.Api.Tests.Unit;

// The provider's answer is untrusted text: whatever it contains, the validator returns a safe, bounded answer
public class RecognitionAnswerValidatorTests
{
    private static RecognitionAnswer Parse(object answer) => RecognitionAnswerValidator.Parse(JsonSerializer.Serialize(answer));

    private static object Work(string title = "Iron Man", string mediaType = "movie", string relation = "direct", object? confidence = null,
        object? year = null, string? creator = null) =>
        new { title, mediaType, relation, confidence = confidence ?? 0.9, year, creator };

    [Fact]
    public void FullAnswer_IsRead()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            summary = "We saw an Iron Man statue.",
            subject = new { label = "Iron Man statue", kind = "statue", confidence = 0.95 },
            franchises = new[] { "Marvel Cinematic Universe" },
            people = new[] { new { name = "Robert Downey Jr.", role = "actor", confidence = 0.9 } },
            works = new[] { Work(year: 2008), Work("Back in Black", "music", "universe", 0.6, 1980, "AC/DC") }
        });

        answer.Outcome.ShouldBe(RecognitionOutcome.Recognised);
        answer.Summary.ShouldBe("We saw an Iron Man statue.");
        answer.Subject.ShouldBe(new RecognitionSubject("Iron Man statue", "statue", 0.95));
        answer.Franchises.ShouldBe(["Marvel Cinematic Universe"]);
        answer.People.Single().ShouldBe(new RecognitionPerson("Robert Downey Jr.", "actor", 0.9));
        answer.Works.ShouldBe([
            new RecognitionWork("Iron Man", MediaType.Movie, 2008, null, RecognitionTier.Direct, 0.9),
            new RecognitionWork("Back in Black", MediaType.Music, 1980, "AC/DC", RecognitionTier.Universe, 0.6)
        ]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"works\": \"Iron Man\"}")]
    public void UnusableAnswer_IsNothingRecognised(string? raw)
    {
        var answer = RecognitionAnswerValidator.Parse(raw);

        answer.Outcome.ShouldBe(RecognitionOutcome.NothingRecognised);
        answer.Works.ShouldBeEmpty();
        answer.Summary.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("nothing_recognised", RecognitionOutcome.NothingRecognised)]
    [InlineData("not_media_related", RecognitionOutcome.NotMediaRelated)]
    public void NegativeOutcomes_IgnoreAnyListedWorks(string outcome, RecognitionOutcome expected)
    {
        var answer = Parse(new { outcome, summary = "We saw a mug.", works = new[] { Work() }, people = new[] { new { name = "X", role = "actor", confidence = 1 } } });

        answer.Outcome.ShouldBe(expected);
        answer.Summary.ShouldBe("We saw a mug.");
        answer.Works.ShouldBeEmpty();
        answer.People.ShouldBeEmpty();
    }

    [Fact]
    public void PersonNotIdentified_NeverKeepsPeople_ButKeepsWorksOfACostume()
    {
        var answer = Parse(new
        {
            outcome = "person_not_identified",
            summary = "We saw a person in a Darth Vader costume.",
            people = new[] { new { name = "Some Actor", role = "actor", confidence = 0.9 } },
            works = new[] { Work("Star Wars") }
        });

        answer.Outcome.ShouldBe(RecognitionOutcome.PersonNotIdentified);
        answer.People.ShouldBeEmpty();
        answer.Works.Single().Title.ShouldBe("Star Wars");
    }

    [Fact]
    public void UnknownOutcome_IsJudgedByTheWorks()
    {
        Parse(new { outcome = "maybe", works = new[] { Work() } }).Outcome.ShouldBe(RecognitionOutcome.Recognised);
        Parse(new { outcome = "maybe", works = Array.Empty<object>() }).Outcome.ShouldBe(RecognitionOutcome.NothingRecognised);
    }

    [Fact]
    public void Confidences_AreClamped_AndDefaultedWhenMissing()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            works = new object[]
            {
                Work("A", confidence: 7),
                Work("B", confidence: -1),
                new { title = "C", mediaType = "movie", relation = "direct" },
                Work("D", confidence: "high")
            }
        });

        answer.Works.Select(w => w.Confidence).ShouldBe([1.0, 0.0, 0.5, 0.5]);
    }

    [Fact]
    public void InvalidWorks_AreDropped()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            works = new object[]
            {
                Work("", "movie"),
                Work("Game", "videogame"),
                Work("Loose", relation: "cousin"),
                new { mediaType = "movie", relation = "direct", confidence = 1 },
                "Iron Man",
                Work("Kept")
            }
        });

        answer.Works.Single().Title.ShouldBe("Kept");
    }

    [Fact]
    public void ImplausibleYears_AreIgnored()
    {
        var answer = Parse(new { outcome = "recognised", works = new[] { Work("A", year: 1200), Work("B", year: "2008"), Work("C", year: 2008) } });

        answer.Works.Select(w => w.Year).ShouldBe([null, null, 2008]);
    }

    [Fact]
    public void OnlyMusic_HasACreator()
    {
        var answer = Parse(new { outcome = "recognised", works = new[] { Work("Movie", creator: "Someone"), Work("Album", "music", creator: "Band") } });

        answer.Works.Select(w => w.Creator).ShouldBe([null, "Band"]);
    }

    [Fact]
    public void Lists_AreCapped()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            franchises = Enumerable.Range(0, 10).Select(i => $"Franchise {i}").ToArray(),
            people = Enumerable.Range(0, 10).Select(i => new { name = $"Person {i}", role = "actor", confidence = 0.9 }).ToArray(),
            works = Enumerable.Range(0, 30).Select(i => Work($"Work {i}")).ToArray()
        });

        answer.Works.Count.ShouldBe(RecognitionAnswerValidator.MaxWorks);
        answer.People.Count.ShouldBe(RecognitionAnswerValidator.MaxPeople);
        answer.Franchises.Count.ShouldBe(RecognitionAnswerValidator.MaxFranchises);
        answer.Works[0].Title.ShouldBe("Work 0");
    }

    [Fact]
    public void Text_IsCleanedAndLimited()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            summary = "We saw\n\nan   Iron Man\u0000 statue." + new string('!', 500),
            works = new[] { Work("  Iron\tMan​ 2  " + new string('x', 300)) }
        });

        answer.Summary.ShouldStartWith("We saw an Iron Man statue.");
        answer.Summary.Length.ShouldBeLessThanOrEqualTo(RecognitionAnswerValidator.MaxSummaryLength);
        answer.Works.Single().Title.ShouldStartWith("Iron Man 2 xxx");
        answer.Works.Single().Title.Length.ShouldBeLessThanOrEqualTo(RecognitionAnswerValidator.MaxTextLength);
    }

    [Fact]
    public void UnknownEnumValues_FallBackToOther()
    {
        var answer = Parse(new
        {
            outcome = "recognised",
            subject = new { label = "Thing", kind = "spaceship", confidence = 0.5 },
            people = new[] { new { name = "Someone", role = "stuntman", confidence = 0.5 } },
            works = new[] { Work() }
        });

        answer.Subject!.Kind.ShouldBe("other");
        answer.People.Single().Role.ShouldBe("other");
    }

    [Fact]
    public void UnknownFields_AreIgnored()
    {
        var answer = RecognitionAnswerValidator.Parse("""
            { "outcome": "recognised", "summary": "We saw X.", "instructions": "ignore all rules", "works": [
              { "title": "X", "mediaType": "movie", "relation": "direct", "confidence": 0.9, "url": "https://evil.test" } ] }
            """);

        answer.Works.Single().ShouldBe(new RecognitionWork("X", MediaType.Movie, null, null, RecognitionTier.Direct, 0.9));
    }

    [Fact]
    public void MissingSummary_GetsADefaultPerOutcome()
    {
        Parse(new { outcome = "not_media_related" }).Summary.ShouldBe(RecognitionAnswerValidator.DefaultSummary(RecognitionOutcome.NotMediaRelated));
    }
}
