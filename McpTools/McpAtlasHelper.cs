using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasCli.McpTools;

public static class McpAtlasHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string ToJson(object? data) =>
        JsonSerializer.Serialize(data, JsonOptions);

    public static string Error(string code, string message)
    {
        Console.Error.WriteLine($"[atlas-cli] {code}: {message}");
        return ToJson(new { error = code, message });
    }

    public static string HandleApiError(AtlasCli.Services.AtlasApiException ex)
    {
        Console.Error.WriteLine($"[atlas-cli] ApiError: {ex.StatusCode} - {ex.Message}");
        return Error("api_error", ex.Message);
    }

    public static string HandleException(Exception ex)
    {
        Console.Error.WriteLine($"[atlas-cli] {ex.GetType().Name}: {ex.Message}");
        return Error(ex.GetType().Name, ex.Message);
    }
}
