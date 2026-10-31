using System.Text.Json;

namespace ReviewApp.Api.Tests.Support;

// Several services return anonymous objects; tests read them the way the clients do, as camelCase JSON
public static class Json
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static JsonElement Of(object? value) => JsonSerializer.SerializeToElement(value, Options);

    public static List<JsonElement> Items<T>(IEnumerable<T> values) => values.Select(v => Of(v)).ToList();

    public static int Int(this JsonElement element, string property) => element.GetProperty(property).GetInt32();
    public static string? Str(this JsonElement element, string property) => element.GetProperty(property).GetString();
    public static decimal Dec(this JsonElement element, string property) => element.GetProperty(property).GetDecimal();
    public static bool Bool(this JsonElement element, string property) => element.GetProperty(property).GetBoolean();
}
