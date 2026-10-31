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
