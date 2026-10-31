using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;

namespace ReviewApp.Api.Tests.Support;

// One throwaway SQL Server database per test run, created through the real migrations (so the triggers exist)
// and dropped when the test process exits. LocalDB by default; REVIEWAPP_TEST_SQL points to another server (CI).
// Tests share it and stay isolated by creating their own uniquely named data (TestData).
public static class TestDatabase
{
    public const string ServerEnvironmentVariable = "REVIEWAPP_TEST_SQL";
    public const string NamePrefix = "ReviewAppTests_";

    // Pinned so unique names and emails compare the same (case-insensitive) on LocalDB and on any CI server
    private const string Collation = "SQL_Latin1_General_CP1_CI_AS";
    private const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";

    private static readonly Lazy<string> Instance = new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string ConnectionString => Instance.Value;

    public static string DatabaseName => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    public static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    // Points to a database that doesn't exist, so every query fails: for "a failure never escapes" tests
    public static AppDbContext CreateBrokenContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = NamePrefix + "DoesNotExist" }.ConnectionString)
            .Options);

    private static string Create()
    {
        var server = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ServerEnvironmentVariable) ?? DefaultServer)
        {
            // LocalDB starts its instance on the first connection, which can take a while
            ConnectTimeout = 60
        };
        var name = NamePrefix + Guid.NewGuid().ToString("N");

        var master = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        try
        {
            using var connection = new SqlConnection(master);
            connection.Open();
            DropStaleDatabases(connection);
            Execute(connection, $"CREATE DATABASE [{name}] COLLATE {Collation}");
        }
        catch (SqlException ex)
        {
            throw new InvalidOperationException(
                $"Can't reach the test SQL Server ({server.DataSource}). Locally the tests use SQL Server LocalDB " +
                $"(check `sqllocaldb info`), or set {ServerEnvironmentVariable} to another server's connection string.", ex);
        }

        var connectionString = new SqlConnectionStringBuilder(server.ConnectionString) { InitialCatalog = name }.ConnectionString;
        using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options))
            context.Database.Migrate();

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Drop(master, name);
        return connectionString;
    }

    // Databases left behind by a crashed or killed run. Only old ones, a parallel run (IDE + terminal) may still use a new one.
    private static void DropStaleDatabases(SqlConnection connection)
    {
        var stale = new List<string>();
        using (var command = new SqlCommand(
            "SELECT name FROM sys.databases WHERE name LIKE @prefix AND create_date < DATEADD(day, -1, GETDATE())", connection))
        {
            command.Parameters.AddWithValue("@prefix", NamePrefix.Replace("_", "[_]") + "%");
            using var reader = command.ExecuteReader();
            while (reader.Read())
                stale.Add(reader.GetString(0));
        }

        foreach (var name in stale)
            Execute(connection, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
    }

    private static void Drop(string master, string name)
    {
        try
        {
            SqlConnection.ClearAllPools();
            using var connection = new SqlConnection(master);
            connection.Open();
            Execute(connection, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]");
        }
        catch (SqlException)
        {
            // Best effort, the next run drops it once it is a day old
        }
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = new SqlCommand(sql, connection);
        command.ExecuteNonQuery();
    }
}
