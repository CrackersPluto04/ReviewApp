using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Tests.Support;

// The real application in-process, against the test database, with every external system faked.
// It runs in the "Testing" environment, so the developer's appsettings.Development.json (real database, real keys)
// is never loaded and the run is the same locally and on CI.
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";
    public const string JwtKey = "reviewapp-tests-signing-key-0123456789abcdef";
    public const string JwtIssuer = "ReviewApp.Tests";
    public const string JwtAudience = "ReviewApp.Tests.Client";
    public const string FrontendBaseUrl = "https://frontend.test";

    // The real limits are tiny for a test suite that sends everything from one address; RateLimitTests use 1
    public const int RelaxedPermitMultiplier = 1000;

    private static readonly Lazy<ApiFactory> SharedInstance = new(() => new ApiFactory());

    private readonly int _permitMultiplier;

    public ApiFactory(int permitMultiplier = RelaxedPermitMultiplier)
    {
        _permitMultiplier = permitMultiplier;
    }

    // One host for the whole run: the integration tests resolve services from it, the HTTP tests send requests to it
    public static ApiFactory Shared => SharedInstance.Value;

    public FakeTmdbService Tmdb { get; } = new();
    public FakeSpotifyService Spotify { get; } = new();
    public CapturingEmailSender Mail { get; } = new();
    public TestPaymentProvider Payments => Services.GetRequiredService<TestPaymentProvider>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);

        // Read eagerly by Program.cs, so they go in as host settings, not through ConfigureAppConfiguration
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestDatabase.ConnectionString);
        builder.UseSetting("App:FrontendBaseUrl", FrontendBaseUrl);
        builder.UseSetting("Jwt:Key", JwtKey);
        builder.UseSetting("Jwt:Issuer", JwtIssuer);
        builder.UseSetting("Jwt:Audience", JwtAudience);
        builder.UseSetting("RateLimiting:PermitMultiplier", _permitMultiplier.ToString());
        // Fake is refused outside Development; Stripe with dummy values registers, then the provider is replaced below
        builder.UseSetting("Subscription:Provider", "Stripe");
        builder.UseSetting("Stripe:SecretKey", "sk_test_dummy");
        builder.UseSetting("Stripe:WebhookSecret", "whsec_dummy");
        builder.UseSetting("Stripe:PriceId", "price_dummy");
        builder.UseSetting("Tmdb:BaseUrl", "https://tmdb.invalid/");
        builder.UseSetting("Tmdb:ApiKey", "dummy");
        builder.UseSetting("Spotify:ClientId", "dummy");
        builder.UseSetting("Spotify:ClientSecret", "dummy");

        builder.ConfigureTestServices(services =>
        {
            // These jobs would change the shared database behind the tests' backs; their work is tested directly.
            // The email dispatcher keeps running: it delivers into the capturing sender instead of SMTP.
            services.RemoveAll<IHostedService>(typeof(DataCleanupService));
            services.RemoveAll<IHostedService>(typeof(SubscriptionExpiryService));

            services.RemoveAll<ITmdbService>();
            services.AddSingleton<ITmdbService>(Tmdb);
            services.RemoveAll<ISpotifyService>();
            services.AddSingleton<ISpotifyService>(Spotify);
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Mail);

            services.RemoveAll<IPaymentProvider>();
            services.AddSingleton<TestPaymentProvider>();
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<TestPaymentProvider>());

            // Test-only endpoints (PremiumProbeController)
            services.AddControllers().AddApplicationPart(typeof(ApiFactory).Assembly);
        });
    }

    // HTTPS like the real clients, so the Secure __Host- cookie is kept and sent back
    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    // Logged in without the login endpoint: a valid session cookie for the user's current token version
    public HttpClient CreateClientFor(DAL.Entities.User user)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        client.DefaultRequestHeaders.Add("Cookie", $"{TokenService.CookieName}={TestTokens.Session(user.ID, user.TokenVersion)}");
        return client;
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<TService>(this IServiceCollection services, Type implementationType)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(TService) && d.ImplementationType == implementationType).ToList())
            services.Remove(descriptor);
    }
}
