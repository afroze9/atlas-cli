namespace AtlasCli.Services;

public static class UserService
{
    public static async Task<object> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var url = $"user/search?query={Uri.EscapeDataString(query)}&maxResults={limit}";
        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);

        return data.EnumerateArray().Select(u => new
        {
            AccountId = u.GetString("accountId"),
            DisplayName = u.GetString("displayName"),
            Email = u.GetString("emailAddress"),
            AccountType = u.GetString("accountType"),
            Active = u.TryGetProperty("active", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True
        }).ToList();
    }
}
