using System.Text.Json;

namespace AtlasCli.Services;

public static class WorkItemService
{
    public static async Task<object> ViewAsync(string key, string? fields = null, string descFormat = "plain", CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'read'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var url = $"issue/{Uri.EscapeDataString(key)}";
        if (!string.IsNullOrEmpty(fields))
            url += $"?fields={Uri.EscapeDataString(fields)}";

        var issue = await ApiHelper.GetOrThrowAsync(client, url, ct);
        return FormatIssue(issue, descFormat);
    }

    public static async Task<object> SearchAsync(string jql, string? fields = null, int limit = 50, bool countOnly = false, string descFormat = "plain", CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var requestedFields = !string.IsNullOrEmpty(fields)
            ? fields
            : "summary,status,issuetype,assignee,priority,reporter,created,updated";
        var url = $"search/jql?jql={Uri.EscapeDataString(jql)}&maxResults={limit}&fields={Uri.EscapeDataString(requestedFields)}";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);

        if (countOnly)
            return new { Total = data.GetString("total") };

        return data.GetProperty("issues").EnumerateArray().Select(i => FormatIssue(i, descFormat)).ToList();
    }

    public static async Task<object> CreateAsync(string project, string type, string summary,
        string? description = null, string descFormat = "plain", string? assignee = null,
        string? labels = null, string? parent = null, double? storyPoints = null, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(project.ToUpperInvariant(), "write"))
            throw new UnauthorizedAccessException($"Project '{project}' is not allowed for 'write'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var fieldDict = new Dictionary<string, object>
        {
            ["project"] = new { key = project },
            ["issuetype"] = new { name = type },
            ["summary"] = summary
        };

        if (!string.IsNullOrEmpty(description))
        {
            fieldDict["description"] = descFormat switch
            {
                "markdown" => AdfConverter.ConvertMarkdownToAdf(description),
                "adf" => AdfConverter.ParseRawAdf(description),
                _ => AdfConverter.CreatePlainTextAdf(description)
            };
        }

        if (!string.IsNullOrEmpty(assignee))
        {
            if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase))
                fieldDict["assignee"] = null!;
            else
            {
                var accountId = await ResolveAssignee(client, assignee, ct);
                if (accountId != null) fieldDict["assignee"] = new { accountId };
            }
        }

        if (!string.IsNullOrEmpty(labels))
            fieldDict["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrEmpty(parent))
            fieldDict["parent"] = new { key = parent };

        if (storyPoints.HasValue)
        {
            var spField = AuthService.LoadConfig().StoryPointsField;
            fieldDict[spField] = storyPoints.Value;
        }

        var result = await ApiHelper.PostOrThrowAsync(client, "issue", new { fields = fieldDict }, ct);
        return new
        {
            Status = "created",
            Key = result.GetString("key"),
            Id = result.GetString("id"),
            Url = result.GetString("self")
        };
    }

    public static async Task<object> EditAsync(string key, string? summary = null, string? description = null,
        string descFormat = "plain", string? assignee = null, string? labels = null, string? priority = null,
        double? storyPoints = null, string? startDate = null, string? dueDate = null, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'write'.");

        using var client = AtlasClientFactory.CreateJiraClient();
        var fieldDict = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(summary)) fieldDict["summary"] = summary;

        if (!string.IsNullOrEmpty(description))
        {
            fieldDict["description"] = descFormat switch
            {
                "markdown" => AdfConverter.ConvertMarkdownToAdf(description),
                "adf" => AdfConverter.ParseRawAdf(description),
                _ => AdfConverter.CreatePlainTextAdf(description)
            };
        }

        if (!string.IsNullOrEmpty(assignee))
        {
            if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase))
                fieldDict["assignee"] = null!;
            else
            {
                var accountId = await ResolveAssignee(client, assignee, ct);
                if (accountId != null) fieldDict["assignee"] = new { accountId };
            }
        }

        if (!string.IsNullOrEmpty(labels))
            fieldDict["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrEmpty(priority))
            fieldDict["priority"] = new { name = priority };

        if (storyPoints.HasValue)
        {
            var spField = AuthService.LoadConfig().StoryPointsField;
            fieldDict[spField] = storyPoints.Value;
        }

        if (!string.IsNullOrEmpty(startDate) || !string.IsNullOrEmpty(dueDate))
        {
            var dateFields = await ResolveDateFields(client, projectKey, ct);
            if (!string.IsNullOrEmpty(startDate)) fieldDict[dateFields.StartDateField] = startDate;
            if (!string.IsNullOrEmpty(dueDate)) fieldDict[dateFields.DueDateField] = dueDate;
        }

        if (fieldDict.Count == 0)
            throw new InvalidOperationException("No fields specified to update");

        await ApiHelper.PutOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}", new { fields = fieldDict }, ct);
        return new { Status = "updated", Key = key };
    }

    public static async Task<object> TransitionAsync(string keys, string status, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var keyList = keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var results = new List<object>();

        foreach (var key in keyList)
        {
            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
                throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'write'.");

            var transitions = await ApiHelper.GetOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/transitions", ct);
            var match = transitions.GetProperty("transitions").EnumerateArray()
                .FirstOrDefault(t => string.Equals(t.GetString("to", "name"), status, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(t.GetString("name"), status, StringComparison.OrdinalIgnoreCase));

            if (match.ValueKind == JsonValueKind.Undefined)
            {
                var available = transitions.GetProperty("transitions").EnumerateArray()
                    .Select(t => t.GetString("to", "name")).Where(n => n != null);
                throw new InvalidOperationException($"No transition to '{status}' for {key}. Available: {string.Join(", ", available)}");
            }

            var transitionId = match.GetString("id");
            await ApiHelper.PostOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/transitions", new { transition = new { id = transitionId } }, ct);
            results.Add(new { Status = "transitioned", Key = key, To = status });
        }

        return results.Count == 1 ? results[0] : results;
    }

    public static async Task<object> AssignAsync(string key, string assignee, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
            throw new UnauthorizedAccessException($"Project '{projectKey}' is not allowed for 'write'.");

        using var client = AtlasClientFactory.CreateJiraClient();

        if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase) || assignee == "")
        {
            await ApiHelper.PutOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/assignee", new { accountId = (string?)null }, ct);
            return new { Status = "unassigned", Key = key };
        }

        var accountId = await ResolveAssignee(client, assignee, ct);
        await ApiHelper.PutOrThrowAsync(client, $"issue/{Uri.EscapeDataString(key)}/assignee", new { accountId }, ct);
        return new { Status = "assigned", Key = key, Assignee = assignee };
    }

    internal static async Task<string?> ResolveAssignee(HttpClient client, string assignee, CancellationToken ct)
    {
        if (assignee == "@me")
        {
            var me = await ApiHelper.GetOrThrowAsync(client, "myself", ct);
            return me.GetString("accountId");
        }

        if (!assignee.Contains('@'))
            return assignee;

        var users = await ApiHelper.GetOrThrowAsync(client, $"user/search?query={Uri.EscapeDataString(assignee)}", ct);
        var firstMatch = users.EnumerateArray().FirstOrDefault();
        if (firstMatch.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"No user found for '{assignee}'");

        return firstMatch.GetString("accountId");
    }

    internal static async Task<(string StartDateField, string DueDateField)> ResolveDateFields(HttpClient client, string projectKey, CancellationToken ct)
    {
        var project = await ApiHelper.GetOrThrowAsync(client, $"project/{Uri.EscapeDataString(projectKey)}", ct);
        var style = project.GetString("style");
        var isTeamManaged = string.Equals(style, "next-gen", StringComparison.OrdinalIgnoreCase);

        if (isTeamManaged)
        {
            var config = AuthService.LoadConfig();
            return (config.StartDateField, "duedate");
        }

        return ("startDate", "duedate");
    }

    internal static object FormatIssue(JsonElement issue, string descFormat = "plain")
    {
        issue.TryGetProperty("fields", out var fields);
        var config = AuthService.LoadConfig();
        double? storyPoints = null;
        if (fields.TryGetProperty(config.StoryPointsField, out var spValue) && spValue.ValueKind == JsonValueKind.Number)
            storyPoints = spValue.GetDouble();

        var startDate = fields.GetString(config.StartDateField) ?? fields.GetString("startDate");
        var dueDate = fields.GetString("duedate");

        object? description = descFormat switch
        {
            "markdown" => AdfConverter.ConvertAdfToMarkdown(fields),
            "adf" => AdfConverter.ExtractRawAdf(fields),
            _ => AdfConverter.ExtractPlainText(fields)
        };

        return new
        {
            Key = issue.GetString("key"),
            Summary = fields.GetString("summary"),
            Status = fields.GetString("status", "name"),
            Type = fields.GetString("issuetype", "name"),
            Priority = fields.GetString("priority", "name"),
            StoryPoints = storyPoints,
            StartDate = startDate,
            DueDate = dueDate,
            Assignee = fields.GetString("assignee", "displayName"),
            Reporter = fields.GetString("reporter", "displayName"),
            Created = fields.GetString("created"),
            Updated = fields.GetString("updated"),
            Description = description
        };
    }
}
