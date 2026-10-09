using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using ReviewApp.Api.DAL;
using ReviewApp.Api.Services;
using ReviewApp.Api.Services.Interfaces;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins("https://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              // Cross-origin JS can only read exposed headers (the achievement unlock toast uses it)
              .WithExposedHeaders(ResponseHeaderUnlockHandler.HeaderName);
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Fail fast on a missing or too short signing key instead of an obscure error on the first login
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < TokenService.MinKeyBytes)
    throw new InvalidOperationException($"Jwt:Key must be configured and at least {TokenService.MinKeyBytes} bytes long.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // Only accept the algorithm we sign with
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            // Default is 5 minutes of tolerance after expiry; the API is the only issuer, so little is needed
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(TokenService.CookieName, out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },

            // Reject tokens of deleted users and tokens issued before the last password change
            OnTokenValidated = async context =>
            {
                var idClaim = context.Principal?.FindFirst("id")?.Value;
                if (!int.TryParse(idClaim, out var userId))
                {
                    context.Fail("Invalid token.");
                    return;
                }

                // Tokens issued before TokenVersion existed carry no claim and count as version 0
                var tokenVersion = int.TryParse(context.Principal?.FindFirst("ver")?.Value, out var v) ? v : 0;

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var currentVersion = await db.Users
                    .Where(u => u.ID == userId)
                    .Select(u => (int?)u.TokenVersion)
                    .FirstOrDefaultAsync();

                if (currentVersion != tokenVersion)
                    context.Fail("Token revoked.");
            }
        };
    });
builder.Services.AddAuthorization();

// Rate limiting: a generous global limit for every request, plus stricter policies (applied with
// [EnableRateLimiting("<name>")]) for endpoints that are sensitive or proxy to third-party APIs.
// Both the global limit and the endpoint policy must pass.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Logged-in users are limited per account, everyone else per IP
    static string ClientKey(HttpContext httpContext) =>
        httpContext.User.FindFirst("id")?.Value is { } id
            ? $"user:{id}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

    static SlidingWindowRateLimiterOptions Sliding(int permitLimit, TimeSpan window) => new()
    {
        PermitLimit = permitLimit,
        Window = window,
        SegmentsPerWindow = 5,
        QueueLimit = 0
    };

    // Every request: 300 / minute
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientKey(httpContext), _ => Sliding(300, TimeSpan.FromMinutes(1))));

    // Login / register: 10 attempts per 10 minutes per IP (slows down password guessing and mass account creation)
    options.AddPolicy("auth-attempt", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"ip:{httpContext.Connection.RemoteIpAddress}", _ => Sliding(10, TimeSpan.FromMinutes(10))));

    // Change email / change password: 5 attempts per 10 minutes per user (they verify the current password)
    options.AddPolicy("credential-change", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientKey(httpContext), _ => Sliding(5, TimeSpan.FromMinutes(10))));

    // Endpoints that call TMDb / Spotify: 60 / minute, protects our third-party API quotas
    options.AddPolicy("external-api", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientKey(httpContext), _ => Sliding(60, TimeSpan.FromMinutes(1))));

    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

        await response.WriteAsJsonAsync(new { error = "Too many requests. Please try again later." }, cancellationToken);
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGci...\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                },
                Scheme = "oauth2",
                Name = "Bearer",
                In = ParameterLocation.Header,
            },
            new List<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<ITmdbService, TmdbService>();
builder.Services.AddHttpClient<ISpotifyService, SpotifyService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IReviewService, ReviewService>();
builder.Services.AddScoped<IReviewReplyService, ReviewReplyService>();
builder.Services.AddScoped<ICollectionService, CollectionService>();
builder.Services.AddScoped<IFollowerService, FollowerService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IUserAuthHelper, UserAuthHelper>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAchievementService, AchievementService>();
builder.Services.AddScoped<IAchievementUnlockHandler, ResponseHeaderUnlockHandler>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Tells browsers to never talk to the API over plain HTTP (not used on localhost, it would pin the browser)
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();
