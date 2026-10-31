using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services;

namespace ReviewApp.Api.Tests.Unit;

// The mappers only read their argument, so the service is built without its dependencies
public class MediaMappingTests
{
    private readonly MediaService _service = new(null!, null!, null!);

    [Fact]
    public void FromTmdb_BuildsIdsPosterUrlAndTitle()
    {
        var dto = _service.ToMediaDto(MediaType.Movie, new TmdbItemDto
        {
            ID = 603, Title = "The Matrix", ReleaseDate = "1999-03-31", PosterPath = "/matrix.jpg", Overview = "Neo"
        });

        dto.ID.ShouldBe("603Movie");
        dto.ExternalApiID.ShouldBe("603");
        dto.MediaType.ShouldBe(MediaType.Movie);
        dto.Title.ShouldBe("The Matrix");
        dto.ReleaseDate.ShouldBe("1999-03-31");
        dto.PosterUrl.ShouldBe("https://image.tmdb.org/t/p/w500/matrix.jpg");
        dto.Overview.ShouldBe("Neo");
        dto.Creator.ShouldBe(string.Empty);
    }

    [Fact]
    public void FromTmdb_SeriesUsesNameAndFirstAirDate()
    {
        var dto = _service.ToMediaDto(MediaType.Series, new TmdbItemDto { ID = 1399, Name = "Game of Thrones", FirstAirDate = "2011-04-17" });

        dto.ID.ShouldBe("1399Series");
        dto.Title.ShouldBe("Game of Thrones");
        dto.ReleaseDate.ShouldBe("2011-04-17");
        dto.PosterUrl.ShouldBeNull();
    }

    [Fact]
    public void FromSpotify_JoinsArtistsAndTakesFirstImage()
    {
        var dto = _service.ToMediaDto(MediaType.Music, new SpotifyTrackDto
        {
            Id = "abc",
            Name = "Song",
            Album = new SpotifyAlbumDto { ReleaseDate = "2018", Images = [new() { Url = "https://img/1" }, new() { Url = "https://img/2" }] },
            Artists = [new() { Name = "A" }, new() { Name = "B" }]
        });

        dto.ID.ShouldBe("abcMusic");
        dto.Title.ShouldBe("Song");
        dto.Creator.ShouldBe("A, B");
        dto.PosterUrl.ShouldBe("https://img/1");
        dto.ReleaseDate.ShouldBe("2018");
    }

    [Fact]
    public void FromSpotify_WithoutAlbum_HasNoPosterOrDate()
    {
        var dto = _service.ToMediaDto(MediaType.Music, new SpotifyTrackDto { Id = "x", Name = "Song" });

        dto.PosterUrl.ShouldBeNull();
        dto.ReleaseDate.ShouldBeNull();
        dto.Creator.ShouldBe(string.Empty);
    }

    [Fact]
    public void FromEntity_FormatsReleaseDate()
    {
        var dto = _service.ToMediaDto(MediaType.Movie, new Movie
        {
            ExternalApiID = "7", Title = "Seven", ReleaseDate = new DateTime(1995, 9, 22), Creator = "Fincher"
        });

        dto.ID.ShouldBe("7Movie");
        dto.ReleaseDate.ShouldBe("1995-09-22");
        dto.Creator.ShouldBe("Fincher");
    }
}
