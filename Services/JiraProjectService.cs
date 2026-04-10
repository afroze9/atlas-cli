namespace AtlasCli.Services;

public static class JiraProjectService
{
    public static async Task<object> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var data = await ApiHelper.GetOrThrowAsync(client, $"project/search?maxResults={limit}", ct);

        return data.GetProperty("values").EnumerateArray().Select(p => new
        {
            Key = p.GetString("key"),
            Name = p.GetString("name"),
            Type = p.GetString("projectTypeKey"),
            Style = p.GetString("style"),
            Lead = p.GetString("lead", "displayName")
        }).ToList();
    }

    public static async Task<object> ViewAsync(string key, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(key.ToUpperInvariant(), "read"))
            throw new UnauthorizedAccessException($"Project '{key}' is not allowed for 'read'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var p = await ApiHelper.GetOrThrowAsync(client, $"project/{Uri.EscapeDataString(key)}", ct);

        return new
        {
            Key = p.GetString("key"),
            Name = p.GetString("name"),
            Type = p.GetString("projectTypeKey"),
            Style = p.GetString("style"),
            Lead = p.GetString("lead", "displayName"),
            Description = p.GetString("description"),
            Url = p.GetString("self")
        };
    }
}
