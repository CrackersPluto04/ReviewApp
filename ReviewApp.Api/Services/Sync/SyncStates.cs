using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.DAL.Entities;
using ReviewApp.Api.DTOs;

namespace ReviewApp.Api.Services.Sync;

// The server's version of the user's own reviews and collections, as the sync sends them
public static class SyncStates
{
    public static IQueryable<SyncReviewDto> Reviews(AppDbContext db) =>
        db.Reviews.AsNoTracking().Select(r => new SyncReviewDto
        {
            Id = r.ID,
            Media = new SyncMediaDto
            {
                DbMediaId = r.Media.ID,
                MediaType = r.Media.MediaType,
                ExternalApiId = r.Media.ExternalApiID,
                Title = r.Media.Title,
                PosterUrl = r.Media.PosterUrl,
                ReleaseDate = r.Media.ReleaseDate,
                Creator = r.Media.Creator
            },
            Score = r.Score,
            ReviewText = r.ReviewText,
            Pros = r.Pros,
            Cons = r.Cons,
            Visibility = r.VisibilityLevel,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            Version = r.RowVersion
        });

    public static IQueryable<SyncCollectionDto> Collections(AppDbContext db) =>
        db.Collections.AsNoTracking().Select(c => new SyncCollectionDto
        {
            Id = c.ID,
            Name = c.Name,
            Visibility = c.VisibilityLevel,
            IsDefault = c.Name == Collection.DefaultName,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            Version = c.RowVersion,
            Items = c.CollectionMedias
                .OrderBy(cm => cm.OrderIndex)
                .Select(cm => new SyncCollectionItemDto
                {
                    Media = new SyncMediaDto
                    {
                        DbMediaId = cm.Media.ID,
                        MediaType = cm.Media.MediaType,
                        ExternalApiId = cm.Media.ExternalApiID,
                        Title = cm.Media.Title,
                        PosterUrl = cm.Media.PosterUrl,
                        ReleaseDate = cm.Media.ReleaseDate,
                        Creator = cm.Media.Creator
                    },
                    OrderIndex = cm.OrderIndex,
                    AddedAt = cm.AddedAt
                })
                .ToList()
        });

    public static async Task<SyncReviewDto?> ReviewAsync(AppDbContext db, int reviewId, CancellationToken cancellationToken = default)
    {
        var review = await Reviews(db).FirstOrDefaultAsync(r => r.Id == reviewId, cancellationToken);
        return review == null ? null : AsUtc(review);
    }

    public static async Task<SyncCollectionDto?> CollectionAsync(AppDbContext db, int collectionId, CancellationToken cancellationToken = default)
    {
        var collection = await Collections(db).FirstOrDefaultAsync(c => c.Id == collectionId, cancellationToken);
        return collection == null ? null : AsUtc(collection);
    }

    // The database stores UTC without a kind; marked so the JSON carries the "Z"
    public static SyncReviewDto AsUtc(SyncReviewDto review) =>
        review with { CreatedAt = Utc(review.CreatedAt), UpdatedAt = Utc(review.UpdatedAt) };

    public static SyncCollectionDto AsUtc(SyncCollectionDto collection) =>
        collection with
        {
            CreatedAt = Utc(collection.CreatedAt),
            UpdatedAt = Utc(collection.UpdatedAt),
            Items = collection.Items.Select(item => item with { AddedAt = Utc(item.AddedAt) }).ToList()
        };

    public static DateTime Utc(DateTime time) => DateTime.SpecifyKind(time, DateTimeKind.Utc);
}
