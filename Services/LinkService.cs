using System.Text.Json;

namespace AtlasCli.Services;

public static class LinkService
{
    public static async Task<object> ListTypesAsync(CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var data = await ApiHelper.GetOrThrowAsync(client, "issueLinkType", ct);
        return data.GetProperty("issueLinkTypes").EnumerateArray().Select(t => new
        {
            Id = t.GetString("id"),
            Name = t.GetString("name"),
            Inward = t.GetString("inward"),
            Outward = t.GetString("outward")
        }).ToList();
    }

    public static async Task<object> ListAsync(string key, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'read'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var issue = await ApiHelper.GetOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}?fields=issuelinks", ct);

        if (!issue.TryGetProperty("fields", out var fields) ||
            !fields.TryGetProperty("issuelinks", out var links) ||
            links.ValueKind != JsonValueKind.Array)
            return new List<object>();

        return links.EnumerateArray().Select(FormatLink).ToList();
    }

    public static async Task<object> CreateAsync(string fromKey, string toKey, string type, string? comment = null, CancellationToken ct = default)
    {
        var fromProject = AllowedSpacesService.ExtractProjectKey(fromKey);
        var toProject = AllowedSpacesService.ExtractProjectKey(toKey);
        if (!AllowedSpacesService.CheckAndPrompt(fromProject, "write"))
            throw new UnauthorizedAccessException($"Project '{fromProject}' is not allowed for 'write'.");
        if (!AllowedSpacesService.CheckAndPrompt(toProject, "write"))
            throw new UnauthorizedAccessException($"Project '{toProject}' is not allowed for 'write'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var payload = new Dictionary<string, object>
        {
            ["type"] = new { name = type },
            ["outwardIssue"] = new { key = fromKey },
            ["inwardIssue"] = new { key = toKey }
        };
        if (!string.IsNullOrEmpty(comment))
            payload["comment"] = new { body = AdfConverter.CreatePlainTextAdf(comment) };

        await ApiHelper.PostOrThrowAsync(client, "issueLink", payload, ct);
        return new { Status = "linked", From = fromKey, To = toKey, Type = type };
    }

    public static async Task<object> DeleteAsync(string linkId, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        await ApiHelper.DeleteOrThrowAsync(client, $"issueLink/{Uri.EscapeDataString(linkId)}", ct);
        return new { Status = "deleted", Id = linkId };
    }

    internal static object FormatLink(JsonElement link)
    {
        var type = link.TryGetProperty("type", out var t) ? t : default;
        string? direction = null;
        string? relationship = null;
        JsonElement related = default;

        if (link.TryGetProperty("outwardIssue", out var outward))
        {
            direction = "outward";
            relationship = type.GetString("outward");
            related = outward;
        }
        else if (link.TryGetProperty("inwardIssue", out var inward))
        {
            direction = "inward";
            relationship = type.GetString("inward");
            related = inward;
        }

        return new
        {
            Id = link.GetString("id"),
            Type = type.GetString("name"),
            Direction = direction,
            Relationship = relationship,
            IssueKey = related.ValueKind == JsonValueKind.Undefined ? null : related.GetString("key"),
            Summary = related.ValueKind == JsonValueKind.Undefined ? null : related.GetString("fields", "summary"),
            Status = related.ValueKind == JsonValueKind.Undefined ? null : related.GetString("fields", "status", "name")
        };
    }
}
