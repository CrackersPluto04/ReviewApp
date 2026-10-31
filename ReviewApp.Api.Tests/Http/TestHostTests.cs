using Microsoft.EntityFrameworkCore;
using ReviewApp.Api.DAL;
using ReviewApp.Api.Services.Interfaces;
using ReviewApp.Api.Tests.Support;

namespace ReviewApp.Api.Tests.Http;

// Guards for the test host itself: if these fail, every other test could be running against the wrong things
public class TestHostTests
{
    [Fact]
    public void Host_UsesTheThrowawayTestDatabase()
    {
        using var scope = ApiFactory.Shared.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectionString = context.Database.GetConnectionString();

        connectionString.ShouldBe(TestDatabase.ConnectionString);
        TestDatabase.DatabaseName.ShouldStartWith(TestDatabase.NamePrefix);
    }

    [Fact]
    public void Host_RunsInTestingEnvironmentWithFakedExternalServices()
    {
        var services = ApiFactory.Shared.Services;

        services.GetRequiredService<IWebHostEnvironment>().EnvironmentName.ShouldBe(ApiFactory.EnvironmentName);
        services.GetRequiredService<IPaymentProvider>().ShouldBeOfType<TestPaymentProvider>();
        services.GetRequiredService<ITmdbService>().ShouldBeOfType<FakeTmdbService>();
        services.GetRequiredService<ISpotifyService>().ShouldBeOfType<FakeSpotifyService>();
        services.GetRequiredService<IEmailSender>().ShouldBeOfType<CapturingEmailSender>();
    }

    [Fact]
    public void Host_RunsOnlyTheEmailDispatcherOfTheBackgroundJobs()
    {
        var hostedServices = ApiFactory.Shared.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        hostedServices.ShouldContain("EmailDispatcherService");
        hostedServices.ShouldNotContain("DataCleanupService");
        hostedServices.ShouldNotContain("SubscriptionExpiryService");
    }

    [Fact]
    public void Database_HasTheTriggersAndSeedFromTheMigrations()
    {
        using var context = TestDatabase.CreateContext();

        context.Database.GetPendingMigrations().ShouldBeEmpty();
        context.Achievements.Count().ShouldBe(21);
        var triggers = context.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sys.triggers").ToList();
        triggers.ShouldContain("SetUpdatedAt");
        triggers.ShouldContain("AddDefaultCollection");
    }
}
