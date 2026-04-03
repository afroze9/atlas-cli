using System.CommandLine;
using System.Text.Json;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class WorkItemCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("workitem", "Jira work item operations");
        cmd.Subcommands.Add(BuildView(formatOption));
        cmd.Subcommands.Add(BuildSearch(formatOption));
        cmd.Subcommands.Add(BuildCreate(formatOption));
        cmd.Subcommands.Add(BuildEdit(formatOption));
        cmd.Subcommands.Add(BuildTransition(formatOption));
        cmd.Subcommands.Add(BuildAssign(formatOption));
        cmd.Subcommands.Add(CommentCommands.Build(formatOption));
        return cmd;
    }

    private static Command BuildView(Option<string> formatOption)
    {
        var keyArg = new Argument<string>("key") { Description = "Work item key (e.g. PROJ-123)" };
        var fieldsOption = new Option<string?>("--fields") { Description = "Comma-separated list of fields to return" };
        var cmd = new Command("view", "View a work item") { keyArg, fieldsOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyArg)!;
            var fields = parseResult.GetValue(fieldsOption);

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var url = $"issue/{Uri.EscapeDataString(key)}";
            if (!string.IsNullOrEmpty(fields))
                url += $"?fields={Uri.EscapeDataString(fields)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            var issue = data.Value;
            OutputService.Print(FormatIssue(issue), format);
        });
        return cmd;
    }

    private static Command BuildSearch(Option<string> formatOption)
    {
        var jqlOption = new Option<string>("--jql") { Description = "JQL query",  Required = true };
        var fieldsOption = new Option<string?>("--fields") { Description = "Comma-separated list of fields" };
        var limitOption = new Option<int>("--limit") { Description = "Max results",  DefaultValueFactory = _ => 50 };
        var countOption = new Option<bool>("--count") { Description = "Only return the count of matching issues" };
        var cmd = new Command("search", "Search work items with JQL") { jqlOption, fieldsOption, limitOption, countOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var jql = parseResult.GetValue(jqlOption)!;
            var fields = parseResult.GetValue(fieldsOption);
            var limit = parseResult.GetValue(limitOption);
            var countOnly = parseResult.GetValue(countOption);

            using var client = AtlasClientFactory.CreateJiraClient();
            var requestedFields = !string.IsNullOrEmpty(fields)
                ? fields
                : "summary,status,issuetype,assignee,priority,reporter,created,updated";
            var url = $"search/jql?jql={Uri.EscapeDataString(jql)}&maxResults={limit}&fields={Uri.EscapeDataString(requestedFields)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            if (countOnly)
            {
                OutputService.Print(new { Total = data.Value.GetString("total") }, format);
                return;
            }

            var issues = data.Value.GetProperty("issues").EnumerateArray().Select(FormatIssue);
            OutputService.Print(issues, format);
        });
        return cmd;
    }

    private static Command BuildCreate(Option<string> formatOption)
    {
        var projectOption = new Option<string>("--project") { Description = "Project key",  Required = true };
        var typeOption = new Option<string>("--type") { Description = "Issue type (e.g. Story, Task, Bug)",  Required = true };
        var summaryOption = new Option<string>("--summary") { Description = "Issue summary",  Required = true };
        var descriptionOption = new Option<string?>("--description") { Description = "Issue description" };
        var descFormatOption = new Option<string>("--description-format") { Description = "Description format: plain or markdown", DefaultValueFactory = _ => "plain" };
        var assigneeOption = new Option<string?>("--assignee") { Description = "Assignee email or account ID. Use '@me' for self-assign" };
        var labelOption = new Option<string?>("--label") { Description = "Comma-separated labels" };
        var parentOption = new Option<string?>("--parent") { Description = "Parent issue key" };
        var storyPointsOption = new Option<double?>("--story-points") { Description = "Story point estimate" };

        var cmd = new Command("create", "Create a work item") { projectOption, typeOption, summaryOption, descriptionOption, descFormatOption, assigneeOption, labelOption, parentOption, storyPointsOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var project = parseResult.GetValue(projectOption)!;
            var type = parseResult.GetValue(typeOption)!;
            var summary = parseResult.GetValue(summaryOption)!;
            var description = parseResult.GetValue(descriptionOption);
            var descFormat = parseResult.GetValue(descFormatOption)!;
            var assignee = parseResult.GetValue(assigneeOption);
            var labels = parseResult.GetValue(labelOption);
            var parent = parseResult.GetValue(parentOption);
            var storyPoints = parseResult.GetValue(storyPointsOption);

            if (!AllowedSpacesService.CheckAndPrompt(project.ToUpperInvariant(), "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();

            var fields = new Dictionary<string, object>
            {
                ["project"] = new { key = project },
                ["issuetype"] = new { name = type },
                ["summary"] = summary
            };

            if (!string.IsNullOrEmpty(description))
            {
                fields["description"] = descFormat == "markdown"
                    ? AdfConverter.ConvertMarkdownToAdf(description)
                    : AdfConverter.CreatePlainTextAdf(description);
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                var accountId = await ResolveAssignee(client, assignee, ct);
                if (accountId != null)
                    fields["assignee"] = new { accountId };
            }

            if (!string.IsNullOrEmpty(labels))
                fields["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!string.IsNullOrEmpty(parent))
                fields["parent"] = new { key = parent };

            if (storyPoints.HasValue)
            {
                var spField = AuthService.LoadConfig().StoryPointsField;
                fields[spField] = storyPoints.Value;
            }

            var payload = new { fields };
            var result = await ApiHelper.PostAsync(client, "issue", payload, ct);
            if (result == null) return;

            OutputService.Print(new
            {
                Status = "created",
                Key = result.Value.GetString("key"),
                Id = result.Value.GetString("id"),
                Url = result.Value.GetString("self")
            }, format);
        });
        return cmd;
    }

    private static Command BuildEdit(Option<string> formatOption)
    {
        var keyArg = new Argument<string>("key") { Description = "Work item key (e.g. PROJ-123)" };
        var summaryOption = new Option<string?>("--summary") { Description = "New summary" };
        var descriptionOption = new Option<string?>("--description") { Description = "New description" };
        var descFormatOption = new Option<string>("--description-format") { Description = "Description format: plain or markdown", DefaultValueFactory = _ => "plain" };
        var assigneeOption = new Option<string?>("--assignee") { Description = "New assignee email or account ID" };
        var labelOption = new Option<string?>("--label") { Description = "Comma-separated labels (replaces existing)" };
        var priorityOption = new Option<string?>("--priority") { Description = "Priority name (e.g. High, Medium, Low)" };
        var storyPointsOption = new Option<double?>("--story-points") { Description = "Story point estimate" };

        var cmd = new Command("edit", "Edit a work item") { keyArg, summaryOption, descriptionOption, descFormatOption, assigneeOption, labelOption, priorityOption, storyPointsOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyArg)!;
            var summary = parseResult.GetValue(summaryOption);
            var description = parseResult.GetValue(descriptionOption);
            var descFormat = parseResult.GetValue(descFormatOption)!;
            var assignee = parseResult.GetValue(assigneeOption);
            var labels = parseResult.GetValue(labelOption);
            var priority = parseResult.GetValue(priorityOption);
            var storyPoints = parseResult.GetValue(storyPointsOption);

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var fields = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(summary))
                fields["summary"] = summary;

            if (!string.IsNullOrEmpty(description))
            {
                fields["description"] = descFormat == "markdown"
                    ? AdfConverter.ConvertMarkdownToAdf(description)
                    : AdfConverter.CreatePlainTextAdf(description);
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                var accountId = await ResolveAssignee(client, assignee, ct);
                if (accountId != null)
                    fields["assignee"] = new { accountId };
            }

            if (!string.IsNullOrEmpty(labels))
                fields["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!string.IsNullOrEmpty(priority))
                fields["priority"] = new { name = priority };

            if (storyPoints.HasValue)
            {
                var spField = AuthService.LoadConfig().StoryPointsField;
                fields[spField] = storyPoints.Value;
            }

            if (fields.Count == 0)
            {
                OutputService.PrintError("no_changes", "No fields specified to update");
                Environment.ExitCode = 1;
                return;
            }

            var payload = new { fields };
            var result = await ApiHelper.PutAsync(client, $"issue/{Uri.EscapeDataString(key)}", payload, ct);
            if (result == null) return;

            OutputService.Print(new { Status = "updated", Key = key }, format);
        });
        return cmd;
    }

    private static Command BuildTransition(Option<string> formatOption)
    {
        var keyOption = new Option<string>("--key") { Description = "Work item key(s), comma-separated",  Required = true };
        var statusOption = new Option<string>("--status") { Description = "Target status name (e.g. 'In Progress', 'Done')",  Required = true };
        var cmd = new Command("transition", "Transition a work item to a new status") { keyOption, statusOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var keys = parseResult.GetValue(keyOption)!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var status = parseResult.GetValue(statusOption)!;

            using var client = AtlasClientFactory.CreateJiraClient();

            foreach (var key in keys)
            {
                var projectKey = AllowedSpacesService.ExtractProjectKey(key);
                if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

                // Get available transitions
                var transitions = await ApiHelper.GetAsync(client, $"issue/{Uri.EscapeDataString(key)}/transitions", ct);
                if (transitions == null) return;

                var match = transitions.Value.GetProperty("transitions").EnumerateArray()
                    .FirstOrDefault(t => string.Equals(t.GetString("to", "name"), status, StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(t.GetString("name"), status, StringComparison.OrdinalIgnoreCase));

                if (match.ValueKind == JsonValueKind.Undefined)
                {
                    var available = transitions.Value.GetProperty("transitions").EnumerateArray()
                        .Select(t => t.GetString("to", "name")).Where(n => n != null);
                    OutputService.PrintError("invalid_transition", $"No transition to '{status}' for {key}. Available: {string.Join(", ", available)}");
                    Environment.ExitCode = 1;
                    return;
                }

                var transitionId = match.GetString("id");
                var payload = new { transition = new { id = transitionId } };
                var result = await ApiHelper.PostAsync(client, $"issue/{Uri.EscapeDataString(key)}/transitions", payload, ct);
                if (result == null) return;

                OutputService.Print(new { Status = "transitioned", Key = key, To = status }, format);
            }
        });
        return cmd;
    }

    private static Command BuildAssign(Option<string> formatOption)
    {
        var keyOption = new Option<string>("--key") { Description = "Work item key",  Required = true };
        var assigneeOption = new Option<string>("--assignee") { Description = "Assignee email, account ID, or '@me' for self-assign",  Required = true };
        var cmd = new Command("assign", "Assign a work item") { keyOption, assigneeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyOption)!;
            var assignee = parseResult.GetValue(assigneeOption)!;

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var accountId = await ResolveAssignee(client, assignee, ct);
            if (accountId == null) return;

            var result = await ApiHelper.PutAsync(client, $"issue/{Uri.EscapeDataString(key)}/assignee", new { accountId }, ct);
            if (result == null) return;

            OutputService.Print(new { Status = "assigned", Key = key, Assignee = assignee }, format);
        });
        return cmd;
    }

    private static async Task<string?> ResolveAssignee(HttpClient client, string assignee, CancellationToken ct)
    {
        if (assignee == "@me")
        {
            var me = await ApiHelper.GetAsync(client, "myself", ct);
            return me?.GetString("accountId");
        }

        // If it looks like an account ID (no @), use directly
        if (!assignee.Contains('@'))
            return assignee;

        // Otherwise search by email
        var users = await ApiHelper.GetAsync(client, $"user/search?query={Uri.EscapeDataString(assignee)}", ct);
        if (users == null) return null;

        var firstMatch = users.Value.EnumerateArray().FirstOrDefault();
        if (firstMatch.ValueKind == JsonValueKind.Undefined)
        {
            OutputService.PrintError("user_not_found", $"No user found for '{assignee}'");
            Environment.ExitCode = 1;
            return null;
        }

        return firstMatch.GetString("accountId");
    }

    private static object FormatIssue(JsonElement issue)
    {
        issue.TryGetProperty("fields", out var fields);
        var spField = AuthService.LoadConfig().StoryPointsField;
        double? storyPoints = null;
        if (fields.TryGetProperty(spField, out var spValue) && spValue.ValueKind == JsonValueKind.Number)
            storyPoints = spValue.GetDouble();

        return new
        {
            Key = issue.GetString("key"),
            Summary = fields.GetString("summary"),
            Status = fields.GetString("status", "name"),
            Type = fields.GetString("issuetype", "name"),
            Priority = fields.GetString("priority", "name"),
            StoryPoints = storyPoints,
            Assignee = fields.GetString("assignee", "displayName"),
            Reporter = fields.GetString("reporter", "displayName"),
            Created = fields.GetString("created"),
            Updated = fields.GetString("updated"),
            Description = ExtractDescription(fields)
        };
    }

    private static object FormatIssueSummary(JsonElement issue)
    {
        issue.TryGetProperty("fields", out var fields);
        return new
        {
            Key = issue.GetString("key"),
            Type = fields.GetString("issuetype", "name"),
            Summary = fields.GetString("summary"),
            Status = fields.GetString("status", "name"),
            Assignee = fields.GetString("assignee", "displayName"),
            Priority = fields.GetString("priority", "name")
        };
    }

    private static string? ExtractDescription(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined || fields.ValueKind == JsonValueKind.Null)
            return null;
        if (!fields.TryGetProperty("description", out var desc) || desc.ValueKind == JsonValueKind.Null)
            return null;
        if (!desc.TryGetProperty("content", out var content))
            return null;

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
        return texts.Count > 0 ? string.Join("\n", texts) : null;
    }
}
