using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Recognition;

// Finds the real TMDb / Spotify items behind the AI's answer, in three steps:
//   1. the works the AI named (exact title, fitting year; anything else is dropped)
//   2. the film series (TMDb collection) of the closest movies, as Direct items
//   3. the best-known work of the connected actors / directors, as People items
// The calls of one recognition are bounded (a budget, a few in parallel, a deadline, stop after repeated failures)
// and successful answers are cached for an hour, so a recognition can't exhaust the TMDb / Spotify quotas.
public class MediaResolver : IMediaResolver
{
    private const int CollectionSeeds = 2;
    private const double CollectionDamping = 0.9;
    private const int PeopleToExpand = 2;
    private const double PersonMinConfidence = 0.5;
    private const int CreditsPerPerson = 8;
    private const double PeopleDamping = 0.85;
    // Credits with fewer votes are mostly cameos and obscure titles
    private const int MinCreditVotes = 20;
    // A series counts as the person's work from this many episodes, fewer is a guest appearance
    private const int MinSeriesEpisodes = 5;
    // A credit with fewer votes than this share of the person's best-known one is a short or an early obscure work
    private const double MinShareOfTopCreditVotes = 0.02;
    // How many of the top search results may match by a longer / shorter form of the title
    private const int LooseMatchSearchDepth = 3;
    private const int FailFastAfter = 3;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    // TMDb genres that are appearances rather than work: news, reality, talk shows
    private static readonly HashSet<int> ExcludedCreditGenres = [10763, 10764, 10767];

    private readonly ITmdbService _tmdb;
    private readonly ISpotifyService _spotify;
    private readonly IMediaService _mediaService;
    private readonly IMemoryCache _cache;
    private readonly RecognitionOptions _options;
    private readonly ILogger<MediaResolver> _logger;

    public MediaResolver(ITmdbService tmdb, ISpotifyService spotify, IMediaService mediaService, IMemoryCache cache,
        IOptions<RecognitionOptions> options, ILogger<MediaResolver> logger)
    {
        _tmdb = tmdb;
        _spotify = spotify;
        _mediaService = mediaService;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ResolveResult> ResolveAsync(RecognitionAnswer answer, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ResolveDeadlineSeconds)));
        using var run = new Run(_cache, _options, deadline.Token, cancellationToken);

        var candidates = (await Task.WhenAll(answer.Works.Select(w => ResolveWorkAsync(run, w))))
            .OfType<RankCandidate>()
            .ToList();

        if (_options.ExpandCollections)
            candidates.AddRange(await ExpandCollectionsAsync(run, candidates));

        if (_options.ExpandPeople)
            candidates.AddRange(await ExpandPeopleAsync(run, answer.People));

        if (run.Failures > 0)
            _logger.LogWarning("Recognition media lookups: {Failures} of {Lookups} failed", run.Failures, run.Lookups);

        return new ResolveResult(candidates, run.Lookups, run.Failures);
    }

    private async Task<RankCandidate?> ResolveWorkAsync(Run run, RecognitionWork work)
    {
        var key = TitleMatcher.Normalize(work.Title);
        switch (work.MediaType)
        {
            case MediaType.Movie:
            {
                var (ok, hits) = await run.LookupAsync($"movie:{key}", ct => _tmdb.FindMoviesAsync(work.Title, ct));
                var hit = ok ? BestMatch(hits!, work, h => h.Title ?? h.Name, h => h.ReleaseDate) : null;
                return hit == null ? null : new RankCandidate(_mediaService.ToMediaDto(MediaType.Movie, hit), work.Relation, work.Confidence, hit.Popularity);
            }
            case MediaType.Series:
            {
                var (ok, hits) = await run.LookupAsync($"series:{key}", ct => _tmdb.FindSeriesAsync(work.Title, ct));
                var hit = ok ? BestMatch(hits!, work, h => h.Name ?? h.Title, h => h.FirstAirDate) : null;
                return hit == null ? null : new RankCandidate(_mediaService.ToMediaDto(MediaType.Series, hit), work.Relation, work.Confidence, hit.Popularity);
            }
            default:
            {
                var (ok, tracks) = await run.LookupAsync($"music:{key}|{TitleMatcher.Normalize(work.Creator)}",
                    ct => _spotify.FindTracksAsync(work.Title, work.Creator, ct));
                // The AI names albums and singles, the app's music items are tracks: an album lands on its most popular track.
                // Remasters move the release date, so the year is not checked; the artist is.
                var track = ok
                    ? tracks!
                        .Where(t => TitleMatcher.TitleMatches(work.Title, t.Name) || TitleMatcher.TitleMatches(work.Title, t.Album?.Name))
                        .Where(t => work.Creator == null || t.Artists.Any(a => TitleMatcher.TitleMatches(work.Creator, a.Name)))
                        .OrderByDescending(t => t.Popularity)
                        .FirstOrDefault()
                    : null;
                return track == null ? null : new RankCandidate(_mediaService.ToMediaDto(MediaType.Music, track), work.Relation, work.Confidence, track.Popularity);
            }
        }
    }

    // Exact title with a fitting year; an exact year beats a neighbouring one, then the more popular hit wins.
    // Without one, the AI's title may be a longer or shorter form of TMDb's ("Star Wars: Episode IV - A New Hope" is
    // "Star Wars" there, TMDb's search finds it through its alternative titles): accepted only among the top search
    // results and with a known, fitting year on both sides.
    private static TmdbItemDto? BestMatch(List<TmdbItemDto> hits, RecognitionWork work, Func<TmdbItemDto, string?> title, Func<TmdbItemDto, string?> date)
    {
        var exact = hits
            .Where(h => TitleMatcher.TitleMatches(work.Title, title(h)) && TitleMatcher.YearMatches(work.Year, date(h)))
            .OrderByDescending(h => work.Year != null && TitleMatcher.YearOf(date(h)) == work.Year)
            .ThenByDescending(h => h.Popularity)
            .FirstOrDefault();
        if (exact != null || work.Year == null)
            return exact;

        return hits
            .Take(LooseMatchSearchDepth)
            .Where(h => TitleMatcher.YearOf(date(h)) != null && TitleMatcher.YearMatches(work.Year, date(h))
                        && TitleMatcher.TitleContains(work.Title, title(h)))
            .OrderByDescending(h => TitleMatcher.YearOf(date(h)) == work.Year)
            .ThenByDescending(h => h.Popularity)
            .FirstOrDefault();
    }

    private async Task<IEnumerable<RankCandidate>> ExpandCollectionsAsync(Run run, List<RankCandidate> resolved)
    {
        var seeds = resolved
            .Where(c => c.Tier == RecognitionTier.Direct && c.Media.MediaType == MediaType.Movie)
            .OrderByDescending(c => c.Confidence)
            .DistinctBy(c => c.Media.ExternalApiID)
            .Take(CollectionSeeds)
            .ToList();

        var collections = await Task.WhenAll(seeds.Select(async seed =>
        {
            if (!int.TryParse(seed.Media.ExternalApiID, out var movieId))
                return (Seed: seed, Id: (int?)null);
            var (ok, collectionId) = await run.LookupAsync($"movie-collection:{movieId}", ct => _tmdb.GetMovieCollectionIdAsync(movieId, ct));
            return (Seed: seed, Id: ok ? collectionId : null);
        }));

        // Two seeds of the same film series need it only once, through the more confident one
        var expansions = await Task.WhenAll(collections
            .Where(c => c.Id != null)
            .DistinctBy(c => c.Id)
            .Select(async c =>
            {
                var (ok, parts) = await run.LookupAsync($"collection:{c.Id}", ct => _tmdb.GetCollectionPartsAsync(c.Id!.Value, ct));
                return ok
                    ? parts!
                        // Announced, unreleased parts have no date yet
                        .Where(p => TitleMatcher.YearOf(p.ReleaseDate) != null)
                        .Select(p => new RankCandidate(_mediaService.ToMediaDto(MediaType.Movie, p), RecognitionTier.Direct,
                            c.Seed.Confidence * CollectionDamping, p.Popularity))
                    : [];
            }));

        return expansions.SelectMany(e => e);
    }

    private async Task<IEnumerable<RankCandidate>> ExpandPeopleAsync(Run run, IReadOnlyList<RecognitionPerson> people)
    {
        var minimum = Math.Max(PersonMinConfidence, _options.MinConfidence);
        var selected = people
            .Where(p => p.Role is "actor" or "director" && p.Confidence >= minimum)
            .OrderByDescending(p => p.Confidence)
            .Take(PeopleToExpand)
            .ToList();

        var expansions = await Task.WhenAll(selected.Select(async person =>
        {
            var (found, matches) = await run.LookupAsync($"person:{TitleMatcher.Normalize(person.Name)}", ct => _tmdb.FindPeopleAsync(person.Name, ct));
            var match = found
                ? matches!.Where(p => TitleMatcher.TitleMatches(person.Name, p.Name)).OrderByDescending(p => p.Popularity).FirstOrDefault()
                : null;
            if (match == null)
                return [];

            var (ok, credits) = await run.LookupAsync($"person-credits:{match.ID}", ct => _tmdb.GetPersonCreditsAsync(match.ID, ct));
            if (!ok)
                return [];

            return BestKnownWork(credits!, person.Role)
                .Select(c =>
                {
                    var type = c.MediaType == "tv" ? MediaType.Series : MediaType.Movie;
                    return new RankCandidate(_mediaService.ToMediaDto(type, c), RecognitionTier.People, person.Confidence * PeopleDamping, c.Popularity);
                })
                .ToList();
        }));

        return expansions.SelectMany(e => e);
    }

    // A person's best-known films and series: the most voted ones, not the most popular (TMDb popularity is what is
    // trending now, a talk show appearance or an airing series), and only real work (no appearances, guest roles,
    // shorts and obscure early works)
    private static List<TmdbCreditDto> BestKnownWork(TmdbPersonCreditsDto credits, string role)
    {
        var work = (role == "director" ? credits.Crew.Where(c => c.Job == "Director") : credits.Cast)
            .Where(c => c.MediaType is "movie" or "tv" && c.VoteCount >= MinCreditVotes && !c.GenreIds.Any(ExcludedCreditGenres.Contains))
            .Where(c => c.MediaType == "movie" || c.EpisodeCount == null || c.EpisodeCount >= MinSeriesEpisodes)
            .DistinctBy(c => (c.MediaType, c.ID))
            .OrderByDescending(c => c.VoteCount)
            .ThenByDescending(c => c.Popularity)
            .ToList();

        var floor = work.Count == 0 ? 0 : work[0].VoteCount * MinShareOfTopCreditVotes;
        return work.Where(c => c.VoteCount >= floor).Take(CreditsPerPerson).ToList();
    }

    // The bookkeeping of one recognition's lookups
    private sealed class Run : IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly int _maxLookups;
        private readonly SemaphoreSlim _parallel;
        private readonly CancellationToken _deadline;
        private readonly CancellationToken _caller;
        private int _lookups;
        private int _failures;
        private int _consecutiveFailures;

        public Run(IMemoryCache cache, RecognitionOptions options, CancellationToken deadline, CancellationToken caller)
        {
            _cache = cache;
            _maxLookups = Math.Max(0, options.MaxLookups);
            _parallel = new SemaphoreSlim(Math.Max(1, options.MaxParallelLookups));
            _deadline = deadline;
            _caller = caller;
        }

        public int Lookups => Math.Min(Volatile.Read(ref _lookups), _maxLookups);
        public int Failures => Volatile.Read(ref _failures);

        // (false, default) when the lookup failed or was skipped; a cached answer costs no call
        public async Task<(bool Ok, T? Value)> LookupAsync<T>(string key, Func<CancellationToken, Task<T>> fetch)
        {
            var cacheKey = "recognition-lookup:" + key;
            if (_cache.TryGetValue(cacheKey, out T? cached))
                return (true, cached);

            if (GivenUp())
                return (false, default);

            // Over budget: skipped, which is a limit and not a failure
            if (Interlocked.Increment(ref _lookups) > _maxLookups)
                return (false, default);

            try
            {
                await _parallel.WaitAsync(_deadline);
                try
                {
                    // Checked again: the source may have failed while this lookup was waiting for its turn
                    if (GivenUp())
                        return (false, default);

                    var value = await fetch(_deadline);
                    _cache.Set(cacheKey, value, CacheLifetime);
                    Volatile.Write(ref _consecutiveFailures, 0);
                    return (true, value);
                }
                finally
                {
                    _parallel.Release();
                }
            }
            catch (MediaSourceUnavailableException)
            {
                Interlocked.Increment(ref _failures);
                Interlocked.Increment(ref _consecutiveFailures);
                return (false, default);
            }
            catch (OperationCanceledException) when (_deadline.IsCancellationRequested && !_caller.IsCancellationRequested)
            {
                Interlocked.Increment(ref _failures);
                return (false, default);
            }
        }

        // A source that keeps failing is given up on, and so is everything after the deadline (both count as failed lookups)
        private bool GivenUp()
        {
            if (Volatile.Read(ref _consecutiveFailures) < FailFastAfter && !_deadline.IsCancellationRequested)
                return false;

            Interlocked.Increment(ref _failures);
            return true;
        }

        public void Dispose() => _parallel.Dispose();
    }
}
