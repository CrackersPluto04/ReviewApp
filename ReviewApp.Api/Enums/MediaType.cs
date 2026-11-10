namespace ReviewApp.Api.Enums;

/// <summary>
/// The kinds of media the app handles. Decides the source (TMDb for movies and series, Spotify for music) and is the
/// discriminator of the <c>Media</c> table-per-hierarchy mapping. The values are stored and sent as integers, so
/// they are explicit and must not change.
/// </summary>
public enum MediaType
{
    Movie = 0,
    Series = 1,
    Music = 2
}
