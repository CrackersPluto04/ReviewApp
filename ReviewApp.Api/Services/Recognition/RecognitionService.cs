using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;
using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Recognition;

// The recognition pipeline: cheap checks first (image, one at a time, daily allowance), then the AI call, which is
// counted as soon as it answered, then TMDb / Spotify resolution, ranking and the stored list for "Show more".
// The AI call and the counting don't follow the request's cancellation: a client that gives up after the provider
// was paid still uses up the recognition, so cancelling can't be used to get free ones.
public class RecognitionService : IRecognitionService
{
    private readonly AppDbContext _context;
    private readonly IRecognitionProvider _provider;
    private readonly IMediaResolver _resolver;
    private readonly IEntitlementService _entitlementService;
    private readonly RecognitionResultStore _store;
    private readonly RecognitionGate _gate;
    private readonly RecognitionOptions _options;
    private readonly ILogger<RecognitionService> _logger;

    public RecognitionService(AppDbContext context, IRecognitionProvider provider, IMediaResolver resolver, IEntitlementService entitlementService,
        RecognitionResultStore store, RecognitionGate gate, IOptions<RecognitionOptions> options, ILogger<RecognitionService> logger)
    {
        _context = context;
        _provider = provider;
        _resolver = resolver;
        _entitlementService = entitlementService;
        _store = store;
        _gate = gate;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public async Task<RecognitionAllowanceDto> GetAllowanceAsync(int userId)
    {
        if (!_options.Enabled)
            return new RecognitionAllowanceDto { Enabled = false };

        if (!await _entitlementService.HasFeatureAsync(userId, FeatureKeys.CameraRecognition))
            return new RecognitionAllowanceDto { Enabled = true, Premium = false };

        return ToDto(await AllowanceAsync(userId));
    }

    public async Task<RecognitionResult> RecognizeAsync(int userId, byte[] image)
    {
        if (!_options.Enabled)
            return new(RecognitionStatus.Disabled, "Camera recognition is temporarily unavailable.");

        var imageCheck = CheckImage(image);
        if (imageCheck != null)
            return imageCheck;
        var info = ImageInspector.Inspect(image)!.Value;

        using var running = _gate.TryEnter(userId);
        if (running == null)
            return new(RecognitionStatus.Busy, "A recognition is already running. Please wait for it to finish.");

        var allowance = await AllowanceAsync(userId);
        if (allowance.IsExhausted)
            return new(RecognitionStatus.DailyLimit, $"You have used all {allowance.Limit} recognitions for today.", ResetsAt: allowance.ResetsAt);

        var stopwatch = Stopwatch.StartNew();
        string? raw;
        try
        {
            // The provider retries and falls back on its own; this bounds all of its attempts together
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, _options.ProviderBudgetSeconds)));
            raw = await _provider.RecognizeAsync(image, info.MimeType, timeout.Token);
        }
        catch (Exception ex) when (ex is RecognitionProviderException or OperationCanceledException)
        {
            _logger.LogWarning("Recognition for user {UserId} failed at the provider after {Elapsed} ms: {Error}",
                userId, stopwatch.ElapsedMilliseconds, (ex as RecognitionProviderException)?.Error.ToString() ?? "timeout");
            return new(RecognitionStatus.ProviderUnavailable, "Camera recognition is temporarily unavailable. Please try again in a moment.");
        }

        // The provider answered (even if the answer is unusable): this one counts
        var usage = new RecognitionUsage { UserID = userId };
        _context.RecognitionUsages.Add(usage);
        await _context.SaveChangesAsync(CancellationToken.None);
        var used = allowance with { Used = allowance.Used + 1, Remaining = Math.Max(0, allowance.Remaining - 1) };

        var answer = RecognitionAnswerValidator.Parse(raw);
        IReadOnlyList<RankedItem> ranked = [];
        var incomplete = false;

        if (answer.Works.Count > 0 || answer.People.Count > 0)
        {
            var resolved = await _resolver.ResolveAsync(answer, CancellationToken.None);
            ranked = RecognitionRanking.Rank(resolved.Candidates, _options);
            incomplete = resolved.Failures > 0;

            // Nothing to show because TMDb / Spotify were down: not the user's fault, the recognition is given back
            if (ranked.Count == 0 && resolved.Failures > 0)
            {
                _context.RecognitionUsages.Remove(usage);
                await _context.SaveChangesAsync(CancellationToken.None);
                _logger.LogWarning("Recognition for user {UserId} refunded: media sources failed ({Failures} of {Lookups} lookups)",
                    userId, resolved.Failures, resolved.Lookups);
                return new(RecognitionStatus.SourceUnavailable, "We couldn't look up movies, series and music right now. This attempt wasn't counted, please try again.");
            }
        }

        _logger.LogInformation("Recognition for user {UserId}: {Outcome} in {Elapsed} ms, {Works} works named, {Items} items",
            userId, answer.Outcome, stopwatch.ElapsedMilliseconds, answer.Works.Count, ranked.Count);

        var page = RecognitionRanking.Page(ranked, 0, _options.FirstPageSize);
        return new(RecognitionStatus.Ok, Response: new RecognitionResponseDto
        {
            RecognitionId = page.HasMore ? _store.Put(userId, ranked) : null,
            Outcome = ApiName(answer.Outcome),
            Summary = answer.Summary,
            Subject = answer.Subject == null ? null : new RecognitionSubjectDto { Label = answer.Subject.Label, Kind = answer.Subject.Kind },
            Items = page.Items.Select(ToDto).ToList(),
            Total = page.Total,
            HasMore = page.HasMore,
            NextOffset = page.NextOffset,
            Incomplete = incomplete,
            Allowance = ToDto(used)
        });
    }

    public RecognitionItemsDto? GetItems(int userId, string recognitionId, int offset, int count)
    {
        var items = _store.Get(recognitionId, userId);
        if (items == null)
            return null;

        var page = RecognitionRanking.Page(items, offset, Math.Clamp(count, 1, Math.Max(1, _options.TotalCap)));
        return new RecognitionItemsDto
        {
            Items = page.Items.Select(ToDto).ToList(),
            Total = page.Total,
            HasMore = page.HasMore,
            NextOffset = page.NextOffset
        };
    }

    private RecognitionResult? CheckImage(byte[] image)
    {
        if (image.Length == 0)
            return new(RecognitionStatus.BadImage, "The photo is empty.");
        if (image.Length > _options.MaxImageBytes)
            return new(RecognitionStatus.TooLarge, $"The photo is too large (max {_options.MaxImageBytes / (1024 * 1024)} MB).");

        var info = ImageInspector.Inspect(image);
        if (info == null)
            return new(RecognitionStatus.BadImage, "Only JPEG and PNG photos are supported.");
        if (info.Value.Pixels > _options.MaxImagePixels)
            return new(RecognitionStatus.TooLarge, "The photo's resolution is too high.");

        return null;
    }

    private async Task<AllowanceState> AllowanceAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var dayStart = RecognitionAllowance.DayStart(now);
        var used = await _context.RecognitionUsages.CountAsync(u => u.UserID == userId && u.CreatedAt >= dayStart);
        return RecognitionAllowance.Compute(_options.DailyLimitPerUser, used, now);
    }

    private static RecognitionAllowanceDto ToDto(AllowanceState allowance) => new()
    {
        Enabled = true,
        Premium = true,
        Limit = allowance.Limit,
        Remaining = allowance.Remaining,
        ResetsAt = allowance.ResetsAt
    };

    private static RecognizedMediaDto ToDto(RankedItem item) => new() { Media = item.Media, Tier = ApiName(item.Tier) };

    public static string ApiName(RecognitionTier tier) => tier switch
    {
        RecognitionTier.Direct => "direct",
        RecognitionTier.Universe => "universe",
        RecognitionTier.People => "people",
        _ => "similar"
    };

    public static string ApiName(RecognitionOutcome outcome) => outcome switch
    {
        RecognitionOutcome.Recognised => "recognised",
        RecognitionOutcome.NotMediaRelated => "not_media_related",
        RecognitionOutcome.PersonNotIdentified => "person_not_identified",
        _ => "nothing_recognised"
    };
}
