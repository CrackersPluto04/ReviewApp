using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Integration;

public class MediaServiceTests : IntegrationTest
{
    private IMediaService Media => Service<IMediaService>();

    [Theory]
    [InlineData(MediaType.Movie, typeof(Movie))]
    [InlineData(MediaType.Series, typeof(Series))]
    [InlineData(MediaType.Music, typeof(Music))]
    public async Task GetOrCreate_StoresTheRightSubtypeWithFetchedDetails(MediaType type, Type entityType)
    {
        var externalId = TestData.Unique("7");

        var id = await Media.GetOrCreateMediaAsync(type, externalId);

        var stored = await Query().Media.SingleAsync(m => m.ID == id);
        stored.ShouldBeOfType(entityType);
        stored.MediaType.ShouldBe(type);
        stored.ExternalApiID.ShouldBe(externalId);
        stored.Title.ShouldNotBeNullOrEmpty();
        stored.ReleaseDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetOrCreate_MusicKeepsArtistsAndCover()
    {
        var externalId = TestData.Unique("track");

        var id = await Media.GetOrCreateMediaAsync(MediaType.Music, externalId);

        var stored = await Query().Media.SingleAsync(m => m.ID == id);
        stored.Creator.ShouldBe("Artist A, Artist B");
        stored.PosterUrl.ShouldBe($"https://img.test/{externalId}.jpg");
        stored.ReleaseDate.ShouldBe(new DateTime(2018, 3, 4));
    }

    [Fact]
    public async Task GetOrCreate_SecondCall_ReusesTheRowWithoutFetching()
    {
        var externalId = TestData.Unique("7");
        var first = await Media.GetOrCreateMediaAsync(MediaType.Movie, externalId);
        var lookups = App.Tmdb.Lookups;

        var second = await Media.GetOrCreateMediaAsync(MediaType.Movie, externalId);

        second.ShouldBe(first);
        App.Tmdb.Lookups.ShouldBe(lookups);
        (await Query().Media.CountAsync(m => m.ExternalApiID == externalId)).ShouldBe(1);
    }

    [Fact]
    public async Task GetOrCreate_SameExternalIdOfAnotherType_IsADifferentMedia()
    {
        var externalId = TestData.Unique("7");

        var movie = await Media.GetOrCreateMediaAsync(MediaType.Movie, externalId);
        var series = await Media.GetOrCreateMediaAsync(MediaType.Series, externalId);

        series.ShouldNotBe(movie);
    }

    [Fact]
    public async Task GetOrCreate_InvalidMediaType_Fails()
    {
        (await Media.GetOrCreateMediaAsync((MediaType)42, "1")).ShouldBe(-1);
    }

    [Fact(Skip = "Known issue F1 (FINDINGS.md):a failed TMDb/Spotify lookup stores an 'Unknown Title' media row")]
    public async Task GetOrCreate_UnknownExternalId_FailsWithoutStoringAnything()
    {
        var externalId = TestData.Unique("7");
        App.Tmdb.MarkUnknown(externalId);

        var id = await Media.GetOrCreateMediaAsync(MediaType.Movie, externalId);

        id.ShouldBe(-1);
        (await Query().Media.AnyAsync(m => m.ExternalApiID == externalId)).ShouldBeFalse();
    }
}
