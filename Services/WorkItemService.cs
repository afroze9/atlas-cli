using System.Text.Json;

namespace AtlasCli.Services;

public static class WorkItemService
{
    public static async Task<object> ViewAsync(string key, string? fields = null, string descFormat = "plain", CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read"))
            throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "read");

        using var client = AtlasClientFactory.CreateJiraClient();
        var url = $"issue/{Uri.EscapeDataString(key)}?expand=names,schema";
        if (!string.IsNullOrEmpty(fields))
            url += $"&fields={Uri.EscapeDataString(fields)}";

        var issue = await ApiHelper.GetOrThrowAsync(client, url, ct);
        return FormatIssue(issue, descFormat);
    }

    public static async Task<object> SearchAsync(string jql, string? fields = null, int limit = 50, bool countOnly = false, string descFormat = "plain", CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        var requestedFields = !string.IsNullOrEmpty(fields)
            ? fields
            : "summary,status,issuetype,assignee,priority,reporter,created,updated";
        var url = $"search/jql?jql={Uri.EscapeDataString(jql)}&maxResults={limit}" +
            $"&fields={Uri.EscapeDataString(requestedFields)}&expand=names,schema";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);

        if (countOnly)
            return new { Total = data.GetString("total") };

        var names = data.TryGetProperty("names", out var namesValue) ? namesValue.Clone() : (JsonElement?)null;
        var schema = data.TryGetProperty("schema", out var schemaValue) ? schemaValue.Clone() : (JsonElement?)null;
        return data.GetProperty("issues").EnumerateArray()
            .Select(issue => FormatIssue(issue, descFormat, names, schema)).ToList();
    }

    public static async Task<object> CreateAsync(string project, string type, string summary,
        string? description = null, string descFormat = "plain", string? assignee = null,
        string? labels = null, string? parent = null, double? storyPoints = null,
        IReadOnlyDictionary<string, JsonElement>? additionalFields = null, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(project.ToUpperInvariant(), "write"))
            throw AllowedSpacesService.CreateAccessDeniedException(project, "write");

        using var client = AtlasClientFactory.CreateJiraClient();
        var createMetadata = await JiraFieldService.GetCreateMetadataAsync(client, project, type, ct);
        var fieldDict = new Dictionary<string, object?>
        {
            ["project"] = new { key = project },
            ["issuetype"] = new { id = createMetadata.IssueType.Id },
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
            var storyPointsField = JiraFieldService.FindPreferredField(
                createMetadata.Fields,
                AuthService.LoadConfig().StoryPointsField,
                ["Story point estimate", "Story Points"],
                "number") ?? throw new InvalidOperationException(
                    "A numeric story-points field is not available for this project and issue type.");
            fieldDict[storyPointsField.Id] = storyPoints.Value;
        }

        JiraFieldService.ApplyAdditionalFields(fieldDict, additionalFields, createMetadata.Fields);
        JiraFieldService.ValidateRequiredFields(createMetadata.Fields, fieldDict.Keys);

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
        double? storyPoints = null, string? startDate = null, string? dueDate = null, string? parent = null,
        IReadOnlyDictionary<string, JsonElement>? additionalFields = null, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(key);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
            throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "write");

        using var client = AtlasClientFactory.CreateJiraClient();
        var fieldDict = new Dictionary<string, object?>();
        IReadOnlyList<JiraFieldMetadata>? editMetadata = null;
        if (storyPoints.HasValue || !string.IsNullOrEmpty(startDate) || !string.IsNullOrEmpty(dueDate) || additionalFields?.Count > 0)
            editMetadata = await JiraFieldService.GetEditMetadataAsync(client, key, ct);

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
            var storyPointsField = JiraFieldService.FindPreferredField(
                editMetadata!,
                AuthService.LoadConfig().StoryPointsField,
                ["Story point estimate", "Story Points"],
                "number") ?? throw new InvalidOperationException(
                    "A numeric story-points field is not editable on this work item.");
            fieldDict[storyPointsField.Id] = storyPoints.Value;
        }

        if (!string.IsNullOrEmpty(startDate))
        {
            var startDateField = JiraFieldService.FindPreferredField(
                editMetadata!,
                AuthService.LoadConfig().StartDateField,
                ["Start date", "Start Date"],
                "date") ?? throw new InvalidOperationException(
                    "A start-date field is not editable on this work item.");
            fieldDict[startDateField.Id] = startDate;
        }

        if (!string.IsNullOrEmpty(dueDate))
        {
            var dueDateField = JiraFieldService.FindPreferredField(
                editMetadata!, "duedate", ["Due date", "Due Date"], "date") ??
                throw new InvalidOperationException("The due-date field is not editable on this work item.");
            fieldDict[dueDateField.Id] = dueDate;
        }

        JiraFieldService.ApplyAdditionalFields(fieldDict, additionalFields, editMetadata ?? Array.Empty<JiraFieldMetadata>());

        if (!string.IsNullOrEmpty(parent))
        {
            fieldDict["parent"] = parent.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? null!
                : new { key = parent };
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
                throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "write");

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
            throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "write");

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

    internal static object FormatIssue(
        JsonElement issue,
        string descFormat = "plain",
        JsonElement? inheritedNames = null,
        JsonElement? inheritedSchema = null)
    {
        issue.TryGetProperty("fields", out var fields);
        var config = AuthService.LoadConfig();
        var names = issue.TryGetProperty("names", out var issueNames) ? issueNames : inheritedNames;
        var schema = issue.TryGetProperty("schema", out var issueSchema) ? issueSchema : inheritedSchema;
        double? storyPoints = null;
        var storyPointsFieldId = config.StoryPointsField;
        if (!fields.TryGetProperty(storyPointsFieldId, out var spValue))
        {
            storyPointsFieldId = FindFieldIdByName(names, "Story point estimate", "Story Points") ?? storyPointsFieldId;
            fields.TryGetProperty(storyPointsFieldId, out spValue);
        }
        if (spValue.ValueKind == JsonValueKind.Number)
            storyPoints = spValue.GetDouble();

        var startDateFieldId = fields.TryGetProperty(config.StartDateField, out _)
            ? config.StartDateField
            : FindFieldIdByName(names, "Start date", "Start Date") ?? "startDate";
        var startDate = fields.GetString(startDateFieldId) ?? fields.GetString("startDate");
        var dueDate = fields.GetString("duedate");

        object? description = descFormat switch
        {
            "markdown" => AdfConverter.ConvertAdfToMarkdown(fields),
            "adf" => AdfConverter.ExtractRawAdf(fields),
            _ => AdfConverter.ExtractPlainText(fields)
        };

        var normalizedFieldIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "summary", "status", "issuetype", "priority", "assignee", "reporter", "created", "updated",
            "description", "duedate", "startDate", storyPointsFieldId, startDateFieldId
        };
        var additionalFields = fields.ValueKind == JsonValueKind.Object
            ? fields.EnumerateObject()
                .Where(property => !normalizedFieldIds.Contains(property.Name))
                .ToDictionary(
                    property => property.Name,
                    property => new
                    {
                        Name = GetMetadataValue(names, property.Name),
                        Schema = GetMetadataElement(schema, property.Name),
                        Value = property.Value.Clone()
                    },
                    StringComparer.OrdinalIgnoreCase)
            : null;

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
            AssigneeId = fields.GetString("assignee", "accountId"),
            Reporter = fields.GetString("reporter", "displayName"),
            ReporterId = fields.GetString("reporter", "accountId"),
            Created = fields.GetString("created"),
            Updated = fields.GetString("updated"),
            Description = description,
            AdditionalFields = additionalFields is { Count: > 0 } ? additionalFields : null
        };
    }

    private static string? FindFieldIdByName(JsonElement? names, params string[] candidates)
    {
        if (!names.HasValue || names.Value.ValueKind != JsonValueKind.Object)
            return null;

        var expected = candidates.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var property in names.Value.EnumerateObject())
        {
            if (expected.Contains(property.Value.GetString() ?? ""))
                return property.Name;
        }
        return null;
    }

    private static string? GetMetadataValue(JsonElement? metadata, string fieldId)
    {
        if (metadata.HasValue && metadata.Value.ValueKind == JsonValueKind.Object &&
            metadata.Value.TryGetProperty(fieldId, out var value))
            return value.GetString();
        return null;
    }

    private static JsonElement? GetMetadataElement(JsonElement? metadata, string fieldId)
    {
        if (metadata.HasValue && metadata.Value.ValueKind == JsonValueKind.Object &&
            metadata.Value.TryGetProperty(fieldId, out var value))
            return value.Clone();
        return null;
    }
}
