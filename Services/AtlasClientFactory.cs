using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace AtlasCli.Services;

public static class AtlasClientFactory
{
    public static HttpClient CreateJiraClient()
    {
        var config = AuthService.LoadConfig();
        return ConfigureClient(config, $"https://{config.Domain}.atlassian.net/rest/api/3/");
    }

    public static HttpClient CreateAgileClient()
    {
        var config = AuthService.LoadConfig();
        return ConfigureClient(config, $"https://{config.Domain}.atlassian.net/rest/agile/1.0/");
    }

    public static HttpClient CreateConfluenceClient()
    {
        var config = AuthService.LoadConfig();
        return ConfigureClient(config, $"https://{config.Domain}.atlassian.net/wiki/api/v2/");
    }

    private static HttpClient ConfigureClient(AtlasConfig config, string baseUrl)
    {
        var client = new HttpClient();
        client.BaseAddress = new Uri(baseUrl);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Email}:{config.ApiToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}

public static class ApiHelper
{
    public static async Task<JsonElement?> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            await HandleError(response, ct);
            return null;
        }
        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    public static async Task<JsonElement?> PostAsync(HttpClient client, string url, object body, CancellationToken ct)
    {
        var response = await client.PostAsJsonAsync(url, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            await HandleError(response, ct);
            return null;
        }
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrEmpty(content)) return JsonDocument.Parse("{}").RootElement;
        return JsonDocument.Parse(content).RootElement;
    }

    public static async Task<JsonElement?> PutAsync(HttpClient client, string url, object body, CancellationToken ct)
    {
        var response = await client.PutAsJsonAsync(url, body, ct);
        if (!response.IsSuccessStatusCode)
        {
            await HandleError(response, ct);
            return null;
        }
        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrEmpty(content)) return JsonDocument.Parse("{}").RootElement;
        return JsonDocument.Parse(content).RootElement;
    }

    private static async Task HandleError(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            var errorJson = JsonDocument.Parse(body);
            if (errorJson.RootElement.TryGetProperty("errorMessages", out var msgs))
            {
                var messages = msgs.EnumerateArray().Select(m => m.GetString()).Where(m => !string.IsNullOrEmpty(m));
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrEmpty(joined))
                {
                    OutputService.PrintError(((int)response.StatusCode).ToString(), joined);
                    Environment.ExitCode = 1;
                    return;
                }
            }
            if (errorJson.RootElement.TryGetProperty("message", out var msg))
            {
                OutputService.PrintError(((int)response.StatusCode).ToString(), msg.GetString() ?? "");
                Environment.ExitCode = 1;
                return;
            }
        }
        catch { }

        OutputService.PrintError(((int)response.StatusCode).ToString(), response.ReasonPhrase ?? "Unknown error");
        Environment.ExitCode = 1;
    }

    public static string? GetString(this JsonElement el, params string[] path)
    {
        var current = el;
        foreach (var key in path)
        {
            if (current.ValueKind == JsonValueKind.Null || current.ValueKind == JsonValueKind.Undefined)
                return null;
            if (!current.TryGetProperty(key, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.Null ? null : current.ToString();
    }
}
