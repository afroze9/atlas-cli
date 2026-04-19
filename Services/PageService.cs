using System.Text.Json;

namespace AtlasCli.Services;

public static class PageService
{
    public static async Task<object> ListAsync(string? spaceId = null, string? title = null, int limit = 25, string? status = null, string? subtype = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(spaceId))
        {
            if (!AllowedSpacesService.CheckAndPrompt(spaceId, "read", "confluence"))
                throw new UnauthorizedAccessException($"Confluence space '{spaceId}' is not allowed for 'read'.");
        }

        using var client = AtlasClientFactory.CreateConfluenceClient();
        var url = $"pages?limit={limit}";
        if (!string.IsNullOrEmpty(spaceId)) url += $"&space-id={Uri.EscapeDataString(spaceId)}";
        if (!string.IsNullOrEmpty(title)) url += $"&title={Uri.EscapeDataString(title)}";
        if (!string.IsNullOrEmpty(status)) url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrEmpty(subtype)) url += $"&subtype={Uri.EscapeDataString(subtype)}";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);

        return data.GetProperty("results").EnumerateArray().Select(p => new
        {
            Id = p.GetString("id"),
            Title = p.GetString("title"),
            SpaceId = p.GetString("spaceId"),
            Status = p.GetString("status"),
            Subtype = p.GetString("subtype"),
            ParentId = p.GetString("parentId"),
            AuthorId = p.GetString("authorId"),
            CreatedAt = p.GetString("createdAt"),
            Version = p.GetString("version", "number")
        }).ToList();
    }

    public static async Task<object> ViewAsync(string id, string bodyFormat = "storage", CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateConfluenceClient();
        var data = await ApiHelper.GetOrThrowAsync(client, $"pages/{Uri.EscapeDataString(id)}?body-format={Uri.EscapeDataString(bodyFormat)}", ct);

        var pageSpaceId = data.GetString("spaceId");
        if (!string.IsNullOrEmpty(pageSpaceId))
        {
            if (!AllowedSpacesService.CheckAndPrompt(pageSpaceId, "read"))
                throw new UnauthorizedAccessException($"Confluence space '{pageSpaceId}' is not allowed for 'read'.");
        }

        return new
        {
            Id = data.GetString("id"),
            Title = data.GetString("title"),
            SpaceId = pageSpaceId,
            Status = data.GetString("status"),
            Subtype = data.GetString("subtype"),
            AuthorId = data.GetString("authorId"),
            CreatedAt = data.GetString("createdAt"),
            Version = data.GetString("version", "number"),
            Body = data.GetString("body", bodyFormat, "value")
        };
    }

    public static async Task<object> CreateAsync(string spaceId, string title, string body, string bodyFormat = "markdown", string? parentId = null, string status = "current", string? subtype = null, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(spaceId, "write", "confluence"))
            throw new UnauthorizedAccessException($"Confluence space '{spaceId}' is not allowed for 'write'.");

        if (!string.IsNullOrEmpty(subtype) && subtype != "page" && subtype != "live")
            throw new InvalidOperationException("subtype must be 'page' or 'live'.");

        var adfBody = bodyFormat switch
        {
            "adf" => AdfConverter.ParseRawAdf(body),
            "plain" => AdfConverter.CreatePlainTextAdf(body),
            _ => AdfConverter.ConvertMarkdownToAdf(body)
        };

        var payload = new Dictionary<string, object>
        {
            ["spaceId"] = spaceId,
            ["status"] = status,
            ["title"] = title,
            ["body"] = new { representation = "atlas_doc_format", value = JsonSerializer.Serialize(adfBody) }
        };
        if (!string.IsNullOrEmpty(parentId))
            payload["parentId"] = parentId;
        if (subtype == "live")
            payload["subtype"] = "live";

        using var client = AtlasClientFactory.CreateConfluenceClient();
        var result = await ApiHelper.PostOrThrowAsync(client, "pages", payload, ct);

        return new
        {
            Status = "created",
            Id = result.GetString("id"),
            Title = result.GetString("title"),
            SpaceId = result.GetString("spaceId"),
            Subtype = result.GetString("subtype"),
            Version = result.GetString("version", "number")
        };
    }

    public static async Task<object> UpdateAsync(string id, string? title = null, string? body = null, string bodyFormat = "markdown", string? message = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            throw new InvalidOperationException("At least one of title or body must be provided.");

        using var client = AtlasClientFactory.CreateConfluenceClient();
        var existing = await ApiHelper.GetOrThrowAsync(client, $"pages/{Uri.EscapeDataString(id)}?body-format=atlas_doc_format", ct);

        var spaceId = existing.GetString("spaceId");
        if (!string.IsNullOrEmpty(spaceId))
        {
            if (!AllowedSpacesService.CheckAndPrompt(spaceId, "write", "confluence"))
                throw new UnauthorizedAccessException($"Confluence space '{spaceId}' is not allowed for 'write'.");
        }

        var currentVersion = int.Parse(existing.GetString("version", "number") ?? "0");
        var currentTitle = existing.GetString("title") ?? "";
        var currentStatus = existing.GetString("status") ?? "current";

        var version = new Dictionary<string, object> { ["number"] = currentVersion + 1 };
        if (!string.IsNullOrEmpty(message)) version["message"] = message;

        var payload = new Dictionary<string, object>
        {
            ["id"] = id,
            ["status"] = currentStatus,
            ["title"] = title ?? currentTitle,
            ["version"] = version
        };

        if (!string.IsNullOrEmpty(body))
        {
            var adfBody = bodyFormat switch
            {
                "adf" => AdfConverter.ParseRawAdf(body),
                "plain" => AdfConverter.CreatePlainTextAdf(body),
                _ => AdfConverter.ConvertMarkdownToAdf(body)
            };
            payload["body"] = new { representation = "atlas_doc_format", value = JsonSerializer.Serialize(adfBody) };
        }

        var result = await ApiHelper.PutOrThrowAsync(client, $"pages/{Uri.EscapeDataString(id)}", payload, ct);

        return new
        {
            Status = "updated",
            Id = result.GetString("id"),
            Title = result.GetString("title"),
            SpaceId = result.GetString("spaceId"),
            Version = result.GetString("version", "number")
        };
    }
}
