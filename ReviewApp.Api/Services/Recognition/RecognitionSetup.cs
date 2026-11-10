using ReviewApp.Api.Enums;
using ReviewApp.Api.Services.Interfaces;

namespace ReviewApp.Api.Services.Recognition;

/// <summary>
/// Dependency registration for camera recognition.
/// </summary>
public static class RecognitionSetup
{
    /// <summary>
    /// Registers camera recognition and its AI provider. Fails fast when the feature is enabled with a provider that
    /// cannot work (Gemini without a key) or must not be used (Fake outside Development), and when the image limit
    /// does not fit under the request limit. The result store and gate are singletons holding in-memory state, so the
    /// API assumes a single instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configuration is unsafe or incomplete.</exception>
    public static void AddRecognition(this WebApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(RecognitionOptions.SectionName);
        var options = section.Get<RecognitionOptions>() ?? new RecognitionOptions();
        builder.Services.Configure<RecognitionOptions>(section);

        if (options.MaxImageBytes <= 0 || options.MaxImageBytes > RecognitionOptions.MaxRequestBytes / 2)
            throw new InvalidOperationException($"Recognition:MaxImageBytes must be between 1 and {RecognitionOptions.MaxRequestBytes / 2}.");

        if (options.Provider == RecognitionProvider.Fake)
        {
            if (!builder.Environment.IsDevelopment())
                throw new InvalidOperationException("Recognition:Provider 'Fake' is only allowed in Development, configure Gemini.");

            builder.Services.AddSingleton<IRecognitionProvider, FakeRecognitionProvider>();
        }
        else
        {
            if (options.Enabled && string.IsNullOrWhiteSpace(options.Model))
                throw new InvalidOperationException("Recognition:Model must be configured when Recognition is enabled with the Gemini provider.");

            var geminiSection = builder.Configuration.GetSection(GeminiOptions.SectionName);
            if (options.Enabled && string.IsNullOrWhiteSpace(geminiSection.Get<GeminiOptions>()?.ApiKey))
                throw new InvalidOperationException("Gemini:ApiKey must be configured when Recognition is enabled with the Gemini provider (or set Recognition:Enabled to false).");

            builder.Services.Configure<GeminiOptions>(geminiSection);
            builder.Services.AddHttpClient<IRecognitionProvider, GeminiRecognitionProvider>();
        }

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<RecognitionResultStore>();
        builder.Services.AddSingleton<RecognitionGate>();
        builder.Services.AddScoped<IMediaResolver, MediaResolver>();
        builder.Services.AddScoped<IRecognitionService, RecognitionService>();
    }
}
