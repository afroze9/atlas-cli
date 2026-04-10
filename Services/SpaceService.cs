namespace AtlasCli.Services;

public static class SpaceService
{
    public static async Task<object> ListAsync(int limit = 25, string? type = null, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateConfluenceClient();
        var url = $"spaces?limit={limit}";
        if (!string.IsNullOrEmpty(type))
            url += $"&type={Uri.EscapeDataString(type)}";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);

        return data.GetProperty("results").EnumerateArray().Select(s => new
        {
            Id = s.GetString("id"),
            Key = s.GetString("key"),
            Name = s.GetString("name"),
            Type = s.GetString("type"),
            Status = s.GetString("status")
        }).ToList();
    }

    public static async Task<object> ViewAsync(string id, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(id, "read", "confluence"))
            throw new UnauthorizedAccessException($"Confluence space '{id}' is not allowed for 'read'.");

        using var client = AtlasClientFactory.CreateConfluenceClient();
        var s = await ApiHelper.GetOrThrowAsync(client, $"spaces/{Uri.EscapeDataString(id)}?description-format=plain", ct);

        return new
        {
            Id = s.GetString("id"),
            Key = s.GetString("key"),
            Name = s.GetString("name"),
            Type = s.GetString("type"),
            Status = s.GetString("status"),
            Description = s.GetString("description", "plain", "value"),
            HomepageId = s.GetString("homepageId")
        };
    }
}
