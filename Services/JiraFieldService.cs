using System.Text.Json;

namespace AtlasCli.Services;

public static class JiraFieldService
{
    public static async Task<IReadOnlyList<JiraFieldDefinition>> ListAsync(
        string? query = null, string? type = null, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateJiraClient();
        return await ListAsync(client, query, type, ct);
    }

    internal static async Task<IReadOnlyList<JiraFieldDefinition>> ListAsync(
        HttpClient client, string? query = null, string? type = null, CancellationToken ct = default)
    {
        type = string.IsNullOrWhiteSpace(type) ? "all" : type.ToLowerInvariant();
        if (type is not ("all" or "custom" or "system"))
            throw new ArgumentException("Field type must be 'all', 'custom', or 'system'.", nameof(type));

        var fields = new List<JiraFieldDefinition>();
        var startAt = 0;

        while (true)
        {
            var queryParts = new List<string>
            {
                $"startAt={startAt}",
                "maxResults=100"
            };
            if (!string.IsNullOrWhiteSpace(query))
                queryParts.Add($"query={Uri.EscapeDataString(query)}");
            if (type != "all")
                queryParts.Add($"type={Uri.EscapeDataString(type)}");

            var page = await ApiHelper.GetOrThrowAsync(client, $"field/search?{string.Join('&', queryParts)}", ct);
            var values = GetArray(page, "values");
            fields.AddRange(values.Select(ParseFieldDefinition));

            if (IsLastPage(page, startAt, values.Count))
                break;
            startAt += values.Count;
        }

        return fields;
    }

    public static async Task<JiraCreateFieldMetadata> GetCreateMetadataAsync(
        string projectKey, string issueType, CancellationToken ct = default)
    {
        if (!AllowedSpacesService.CheckAndPrompt(projectKey.ToUpperInvariant(), "read"))
            throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "read");

        using var client = AtlasClientFactory.CreateJiraClient();
        return await GetCreateMetadataAsync(client, projectKey, issueType, ct);
    }

    internal static async Task<JiraCreateFieldMetadata> GetCreateMetadataAsync(
        HttpClient client, string projectKey, string issueType, CancellationToken ct = default)
    {
        var issueTypeDefinition = await ResolveIssueTypeAsync(client, projectKey, issueType, ct);
        var fields = new List<JiraFieldMetadata>();
        var startAt = 0;

        while (true)
        {
            var url = $"issue/createmeta/{Uri.EscapeDataString(projectKey)}/issuetypes/" +
                $"{Uri.EscapeDataString(issueTypeDefinition.Id)}?startAt={startAt}&maxResults=100";
            var page = await ApiHelper.GetOrThrowAsync(client, url, ct);
            var values = GetArray(page, "fields", "values");
            fields.AddRange(values.Select(value => ParseFieldMetadata(value, value.GetString("fieldId") ?? value.GetString("key"))));

            if (IsLastPage(page, startAt, values.Count))
                break;
            startAt += values.Count;
        }

        return new JiraCreateFieldMetadata(projectKey, issueTypeDefinition, fields);
    }

    public static async Task<IReadOnlyList<JiraFieldMetadata>> GetEditMetadataAsync(
        string issueKey, CancellationToken ct = default)
    {
        var projectKey = AllowedSpacesService.ExtractProjectKey(issueKey);
        if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write"))
            throw AllowedSpacesService.CreateAccessDeniedException(projectKey, "write");

        using var client = AtlasClientFactory.CreateJiraClient();
        return await GetEditMetadataAsync(client, issueKey, ct);
    }

    internal static async Task<IReadOnlyList<JiraFieldMetadata>> GetEditMetadataAsync(
        HttpClient client, string issueKey, CancellationToken ct = default)
    {
        var data = await ApiHelper.GetOrThrowAsync(
            client, $"issue/{Uri.EscapeDataString(issueKey)}/editmeta", ct);

        if (!data.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return Array.Empty<JiraFieldMetadata>();

        return fields.EnumerateObject()
            .Select(property => ParseFieldMetadata(property.Value, property.Name))
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyDictionary<string, JsonElement> ParseAdditionalFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>();

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("--fields-json must contain a JSON object whose keys are Jira field IDs or names.");

        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    public static void ApplyAdditionalFields(
        IDictionary<string, object?> target,
        IReadOnlyDictionary<string, JsonElement>? additionalFields,
        IReadOnlyList<JiraFieldMetadata> metadata)
    {
        if (additionalFields == null)
            return;

        foreach (var entry in additionalFields)
        {
            var field = ResolveField(metadata, entry.Key);
            if (target.ContainsKey(field.Id))
                throw new InvalidOperationException($"Field '{entry.Key}' was supplied more than once.");
            target[field.Id] = NormalizeValue(entry.Value, field);
        }
    }

    public static JiraFieldMetadata ResolveField(
        IReadOnlyList<JiraFieldMetadata> metadata, string idOrName)
    {
        var byId = metadata.FirstOrDefault(field =>
            field.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
            field.Key.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        if (byId != null)
            return byId;

        var byName = metadata
            .Where(field => field.Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return byName.Count switch
        {
            1 => byName[0],
            > 1 => throw new InvalidOperationException(
                $"Field name '{idOrName}' is ambiguous. Use one of these field IDs: {string.Join(", ", byName.Select(field => field.Id))}."),
            _ => throw new InvalidOperationException(
                $"Field '{idOrName}' is not available in this Jira context. Query the field metadata and use an available field ID.")
        };
    }

    public static JiraFieldMetadata? FindPreferredField(
        IReadOnlyList<JiraFieldMetadata> metadata,
        string? preferredId,
        IEnumerable<string> fallbackNames,
        string? schemaType = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredId))
        {
            var configured = metadata.FirstOrDefault(field => field.Id.Equals(preferredId, StringComparison.OrdinalIgnoreCase));
            if (configured != null)
                return configured;
        }

        var names = fallbackNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return metadata.FirstOrDefault(field =>
            names.Contains(field.Name) &&
            (schemaType == null || field.SchemaType.Equals(schemaType, StringComparison.OrdinalIgnoreCase)));
    }

    public static void ValidateRequiredFields(
        IReadOnlyList<JiraFieldMetadata> metadata, IEnumerable<string> suppliedFieldIds)
    {
        var supplied = suppliedFieldIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = metadata
            .Where(field => field.Required && !field.HasDefaultValue && !supplied.Contains(field.Id) &&
                field.Id is not "project" and not "issuetype")
            .Select(field => $"{field.Id} ({field.Name})")
            .ToList();

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Missing required Jira fields: {string.Join(", ", missing)}. " +
                "Query create field metadata, supply the fields, and retry.");
    }

    public static object? NormalizeValue(JsonElement value, JiraFieldMetadata field)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (field.SchemaType.Equals("array", StringComparison.OrdinalIgnoreCase) && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.String
                    ? ResolveAllowedValue(item.GetString()!, field) ?? item.GetString()
                    : (object?)item.Clone())
                .ToList();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            var allowedValue = ResolveAllowedValue(text, field);
            if (allowedValue != null)
                return allowedValue;

            if (field.SchemaType.Equals("user", StringComparison.OrdinalIgnoreCase))
                return new { accountId = text };

            if (field.SchemaType.Equals("option", StringComparison.OrdinalIgnoreCase))
                return new { value = text };

            if (field.SchemaCustom.Contains("textarea", StringComparison.OrdinalIgnoreCase))
                return AdfConverter.CreatePlainTextAdf(text);

            return text;
        }

        return value.Clone();
    }

    private static object? ResolveAllowedValue(string input, JiraFieldMetadata field)
    {
        if (!field.AllowedValues.HasValue || field.AllowedValues.Value.ValueKind != JsonValueKind.Array)
            return null;
        var allowedValues = field.AllowedValues.Value;

        foreach (var candidate in allowedValues.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.String &&
                candidate.GetString()!.Equals(input, StringComparison.OrdinalIgnoreCase))
                return candidate.GetString();

            if (candidate.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var propertyName in new[] { "id", "value", "name", "displayName", "key" })
            {
                if (candidate.TryGetProperty(propertyName, out var property) &&
                    property.ToString().Equals(input, StringComparison.OrdinalIgnoreCase))
                    return candidate.Clone();
            }
        }

        return null;
    }

    private static async Task<JiraIssueTypeDefinition> ResolveIssueTypeAsync(
        HttpClient client, string projectKey, string issueType, CancellationToken ct)
    {
        var startAt = 0;
        var available = new List<JiraIssueTypeDefinition>();

        while (true)
        {
            var page = await ApiHelper.GetOrThrowAsync(client,
                $"issue/createmeta/{Uri.EscapeDataString(projectKey)}/issuetypes?startAt={startAt}&maxResults=100", ct);
            var values = GetArray(page, "issueTypes", "values");
            available.AddRange(values.Select(value => new JiraIssueTypeDefinition(
                value.GetString("id") ?? "",
                value.GetString("name") ?? "",
                value.TryGetProperty("subtask", out var subtask) && subtask.ValueKind == JsonValueKind.True)));

            if (IsLastPage(page, startAt, values.Count))
                break;
            startAt += values.Count;
        }

        var match = available.FirstOrDefault(value =>
            value.Id.Equals(issueType, StringComparison.OrdinalIgnoreCase) ||
            value.Name.Equals(issueType, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new InvalidOperationException(
            $"Issue type '{issueType}' is not available in project '{projectKey}'. Available types: " +
            string.Join(", ", available.Select(value => $"{value.Name} ({value.Id})")));
    }

    private static JiraFieldDefinition ParseFieldDefinition(JsonElement value) => new(
        value.GetString("id") ?? value.GetString("key") ?? "",
        value.GetString("key") ?? value.GetString("id") ?? "",
        value.GetString("name") ?? "",
        value.TryGetProperty("custom", out var custom) && custom.ValueKind == JsonValueKind.True,
        CloneProperty(value, "schema"),
        value.GetString("description"));

    private static JiraFieldMetadata ParseFieldMetadata(JsonElement value, string? fallbackId)
    {
        var id = value.GetString("fieldId") ?? value.GetString("key") ?? fallbackId ?? "";
        var schema = CloneProperty(value, "schema");
        return new JiraFieldMetadata(
            id,
            value.GetString("key") ?? id,
            value.GetString("name") ?? id,
            value.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.True,
            value.TryGetProperty("hasDefaultValue", out var hasDefault) && hasDefault.ValueKind == JsonValueKind.True,
            schema,
            CloneProperty(value, "allowedValues"),
            CloneProperty(value, "defaultValue"),
            value.TryGetProperty("operations", out var operations) && operations.ValueKind == JsonValueKind.Array
                ? operations.EnumerateArray().Select(operation => operation.GetString() ?? "").Where(operation => operation.Length > 0).ToArray()
                : Array.Empty<string>());
    }

    private static List<JsonElement> GetArray(JsonElement data, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (data.TryGetProperty(propertyName, out var values) && values.ValueKind == JsonValueKind.Array)
                return values.EnumerateArray().Select(value => value.Clone()).ToList();
        }
        return new List<JsonElement>();
    }

    private static bool IsLastPage(JsonElement page, int startAt, int count)
    {
        if (page.TryGetProperty("isLast", out var isLast) && isLast.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return isLast.GetBoolean();
        if (page.TryGetProperty("total", out var total) && total.TryGetInt32(out var totalValue))
            return startAt + count >= totalValue;
        return count < 100;
    }

    private static JsonElement? CloneProperty(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) ? property.Clone() : null;
}

public sealed record JiraFieldDefinition(
    string Id,
    string Key,
    string Name,
    bool Custom,
    JsonElement? Schema,
    string? Description);

public sealed record JiraIssueTypeDefinition(string Id, string Name, bool Subtask);

public sealed record JiraCreateFieldMetadata(
    string ProjectKey,
    JiraIssueTypeDefinition IssueType,
    IReadOnlyList<JiraFieldMetadata> Fields);

public sealed record JiraFieldMetadata(
    string Id,
    string Key,
    string Name,
    bool Required,
    bool HasDefaultValue,
    JsonElement? Schema,
    JsonElement? AllowedValues,
    JsonElement? DefaultValue,
    IReadOnlyList<string> Operations)
{
    public string SchemaType => Schema.HasValue && Schema.Value.ValueKind == JsonValueKind.Object
        ? Schema.Value.GetString("type") ?? ""
        : "";

    public string SchemaCustom => Schema.HasValue && Schema.Value.ValueKind == JsonValueKind.Object
        ? Schema.Value.GetString("custom") ?? ""
        : "";
}
