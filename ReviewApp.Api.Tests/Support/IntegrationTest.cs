using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;

namespace ReviewApp.Api.Tests.Support;

// Base for the service tests: the real services from the application's own container, against the test database.
// Every Service<T>() call gets a fresh scope, like a separate request; Query() opens a fresh context to check what was stored.
public abstract class IntegrationTest : IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    protected IntegrationTest()
    {
        Data = new TestData();
        _disposables.Add(Data);
    }

    protected static ApiFactory App => ApiFactory.Shared;

    protected TestData Data { get; }

    protected T Service<T>() where T : notnull
    {
        var scope = App.Services.CreateScope();
        _disposables.Add(scope);
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    protected AppDbContext Query() => Track(TestDatabase.CreateContext());

    // The sync's changes feed stops below MIN_ACTIVE_ROWVERSION(), so a transaction another (parallel) test still has
    // open holds back rows written after it. Waits until every rowversion used so far is committed.
    protected async Task AllWritesCommittedAsync()
    {
        var context = Query();
        var written = await context.Database.SqlQueryRaw<long>("SELECT CONVERT(bigint, @@DBTS) AS [Value]").SingleAsync();
        await TestWait.UntilAsync(async () =>
            await context.Database.SqlQueryRaw<long>("SELECT CONVERT(bigint, MIN_ACTIVE_ROWVERSION()) AS [Value]").SingleAsync() > written);
    }

    // Disposed with the test
    protected T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
        GC.SuppressFinalize(this);
    }
}
