using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ReviewApp.Api.Services;

/// <summary>
/// Recognizes SQL Server errors that mean "a parallel request got there first" or "try again in a moment", so
/// callers can handle races without a separate existence check.
/// </summary>
public static class DbErrors
{
    /// <summary>
    /// Whether a save failed on a unique index or key (SQL Server error 2601 for a unique index, 2627 for a
    /// primary key or unique constraint). Used where two parallel requests can create the same row, such as a
    /// registration, a review, a follow or a collection.
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    /// <summary>
    /// Whether the exception, or one it wraps, is an error that goes away when the same work is tried again a bit
    /// later: 1205 (deadlock victim), 1222 (lock request timeout) or -2 (command timeout).
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is SqlException { Number: 1205 or 1222 or -2 })
                return true;
        }
        return false;
    }
}
