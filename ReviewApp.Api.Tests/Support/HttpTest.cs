using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReviewApp.Api.Tests.Support;

// Base for the HTTP tests: requests go through the whole pipeline (routing, auth, rate limits, filters, model binding)
public abstract class HttpTest : IntegrationTest
{
    // A cookie-keeping client, like a browser
    protected HttpClient Browser() => Track(App.CreateBrowser());

    protected HttpClient Anonymous() => Track(App.CreateBrowser());

    protected HttpClient As(DAL.Entities.User user) => Track(App.CreateClientFor(user));

    protected static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    // Asserts the status and returns the parsed body; the body is in the failure message, which makes failures readable
    protected static async Task<JsonElement> ExpectAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(status, text);
        return string.IsNullOrEmpty(text) || response.Content.Headers.ContentType?.MediaType?.Contains("json") != true
            ? default
            : JsonDocument.Parse(text).RootElement.Clone();
    }

    protected static async Task<string> ErrorAsync(HttpResponseMessage response, HttpStatusCode status) =>
        (await ExpectAsync(response, status)).GetProperty("error").GetString()!;

    protected static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body);

    protected static Task<HttpResponseMessage> LoginAsync(HttpClient client, DAL.Entities.User user, string password = TestData.Password) =>
        client.PostAsJsonAsync("api/Auth/login", new { email = user.Email, password });
}
