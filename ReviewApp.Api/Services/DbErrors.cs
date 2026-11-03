using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ReviewApp.Api.Services;

// Recognizes the database errors that mean "a parallel request got there first"
public static class DbErrors
{
    // 2601: duplicate key in a unique index, 2627: duplicate primary key / unique constraint
    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
