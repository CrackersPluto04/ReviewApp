using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ReviewApp.Api.Services;

// Recognizes the database errors that mean "a parallel request got there first"
public static class DbErrors
{
    // 2601: duplicate key in a unique index, 2627: duplicate primary key / unique constraint
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    // Errors that go away when the same work is tried again a bit later:
    // 1205 deadlock victim, 1222 lock request timeout, -2 command timeout
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
