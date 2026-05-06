using System.Text.Json;

namespace AtlasCli.Services;

public static class CommentService
{
    public static async Task<object> ListAsync(string key, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'read'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var data = await ApiHelper.GetOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/comment", ct);

        return data.GetProperty("comments").EnumerateArray().Select(c => new
        {
            Id = c.GetString("id"),
            Author = c.GetString("author", "displayName"),
            AuthorId = c.GetString("author", "accountId"),
            Created = c.GetString("created"),
            Updated = c.GetString("updated"),
            Body = ExtractPlainText(c)
        }).ToList();
    }

    public static async Task<object> CreateAsync(string key, string body, string bodyFormat = "plain", string? mentions = null, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'write'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var adf = bodyFormat switch
        {
            "markdown" => AdfConverter.ConvertMarkdownToAdf(body),
            "adf" => AdfConverter.ParseRawAdf(body),
            _ => AdfConverter.CreatePlainTextAdf(body)
        };

        if (!string.IsNullOrEmpty(mentions))
        {
            var ids = mentions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            adf = AdfConverter.PrependMentions(adf, ids);
        }

        var payload = new { body = adf };
        var result = await ApiHelper.PostOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/comment", payload, ct);
        return new
        {
            Status = "created",
            Id = result.GetString("id"),
            Key = key
        };
    }

    private static string? ExtractPlainText(JsonElement comment)
    {
        if (!comment.TryGetProperty("body", out var body)) return null;
        if (!body.TryGetProperty("content", out var content)) return null;

        var texts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("content", out var inlineContent)) continue;
            foreach (var inline in inlineContent.EnumerateArray())
            {
                if (inline.TryGetProperty("text", out var text))
                    texts.Add(text.GetString() ?? "");
            }
        }
        return texts.Count > 0 ? string.Join(" ", texts) : null;
    }
}
