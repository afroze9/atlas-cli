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
        cmd.Subcommands.Add(LinkCommands.Build(formatOption));
        return cmd;
    }

    private static Command BuildView(Option<string> formatOption)
    {
        var keyArg = new Argument<string>("key") { Description = "Work item key (e.g. PROJ-123)" };
        var fieldsOption = new Option<string?>("--fields") { Description = "Comma-separated list of fields to return" };
        var descFormatOption = new Option<string>("--description-format") { Description = "Description output format: plain, markdown, or adf", DefaultValueFactory = _ => "plain" };
        var cmd = new Command("view", "View a work item") { keyArg, fieldsOption, descFormatOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyArg)!;
            var fields = parseResult.GetValue(fieldsOption);
            var descFormat = parseResult.GetValue(descFormatOption)!;

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var url = $"issue/{Uri.EscapeDataString(key)}?expand=names,schema";
            if (!string.IsNullOrEmpty(fields))
                url += $"&fields={Uri.EscapeDataString(fields)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            var issue = data.Value;
            OutputService.Print(WorkItemService.FormatIssue(issue, descFormat), format);
        });
        return cmd;
    }

    private static Command BuildSearch(Option<string> formatOption)
    {
        var jqlOption = new Option<string>("--jql") { Description = "JQL query",  Required = true };
        var fieldsOption = new Option<string?>("--fields") { Description = "Comma-separated list of fields" };
        var limitOption = new Option<int>("--limit") { Description = "Max results",  DefaultValueFactory = _ => 50 };
        var countOption = new Option<bool>("--count") { Description = "Only return the count of matching issues" };
        var descFormatOption = new Option<string>("--description-format") { Description = "Description output format: plain, markdown, or adf", DefaultValueFactory = _ => "plain" };
        var cmd = new Command("search", "Search work items with JQL") { jqlOption, fieldsOption, limitOption, countOption, descFormatOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var jql = parseResult.GetValue(jqlOption)!;
            var fields = parseResult.GetValue(fieldsOption);
            var limit = parseResult.GetValue(limitOption);
            var countOnly = parseResult.GetValue(countOption);
            var descFormat = parseResult.GetValue(descFormatOption)!;

            using var client = AtlasClientFactory.CreateJiraClient();
            var requestedFields = !string.IsNullOrEmpty(fields)
                ? fields
                : "summary,status,issuetype,assignee,priority,reporter,created,updated";
            var url = $"search/jql?jql={Uri.EscapeDataString(jql)}&maxResults={limit}" +
                $"&fields={Uri.EscapeDataString(requestedFields)}&expand=names,schema";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            if (countOnly)
            {
                OutputService.Print(new { Total = data.Value.GetString("total") }, format);
                return;
            }

            var names = data.Value.TryGetProperty("names", out var namesValue) ? namesValue.Clone() : (JsonElement?)null;
            var schema = data.Value.TryGetProperty("schema", out var schemaValue) ? schemaValue.Clone() : (JsonElement?)null;
            var issues = data.Value.GetProperty("issues").EnumerateArray()
                .Select(issue => WorkItemService.FormatIssue(issue, descFormat, names, schema));
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
        var descFormatOption = new Option<string>("--description-format") { Description = "Description format: plain, markdown, or adf", DefaultValueFactory = _ => "plain" };
        var assigneeOption = new Option<string?>("--assignee") { Description = "Assignee email, account ID, '@me' for self-assign, or 'none' to unassign" };
        var labelOption = new Option<string?>("--label") { Description = "Comma-separated labels" };
        var parentOption = new Option<string?>("--parent") { Description = "Parent issue key" };
        var storyPointsOption = new Option<double?>("--story-points") { Description = "Story point estimate" };
        var fieldsJsonOption = new Option<string?>("--fields-json")
        {
            Description = "Additional Jira fields as a JSON object keyed by field ID or unique field name"
        };

        var cmd = new Command("create", "Create a work item") { projectOption, typeOption, summaryOption, descriptionOption, descFormatOption, assigneeOption, labelOption, parentOption, storyPointsOption, fieldsJsonOption };
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
            var fieldsJson = parseResult.GetValue(fieldsJsonOption);

            if (!AllowedSpacesService.CheckAndPrompt(project.ToUpperInvariant(), "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();

            JiraCreateFieldMetadata createMetadata;
            IReadOnlyDictionary<string, JsonElement> additionalFields;
            try
            {
                createMetadata = await JiraFieldService.GetCreateMetadataAsync(client, project, type, ct);
                additionalFields = JiraFieldService.ParseAdditionalFields(fieldsJson);
            }
            catch (Exception ex) { PrintDynamicFieldError(ex); return; }

            var fields = new Dictionary<string, object?>
            {
                ["project"] = new { key = project },
                ["issuetype"] = new { id = createMetadata.IssueType.Id },
                ["summary"] = summary
            };

            if (!string.IsNullOrEmpty(description))
            {
                fields["description"] = descFormat switch
                {
                    "markdown" => AdfConverter.ConvertMarkdownToAdf(description),
                    "adf" => AdfConverter.ParseRawAdf(description),
                    _ => AdfConverter.CreatePlainTextAdf(description)
                };
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase))
                    fields["assignee"] = null!;
                else
                {
                    var accountId = await ResolveAssignee(client, assignee, ct);
                    if (accountId != null)
                        fields["assignee"] = new { accountId };
                }
            }

            if (!string.IsNullOrEmpty(labels))
                fields["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!string.IsNullOrEmpty(parent))
                fields["parent"] = new { key = parent };

            if (storyPoints.HasValue)
            {
                var storyPointsField = JiraFieldService.FindPreferredField(
                    createMetadata.Fields,
                    AuthService.LoadConfig().StoryPointsField,
                    ["Story point estimate", "Story Points"],
                    "number");
                if (storyPointsField == null)
                {
                    OutputService.PrintError("field_not_available", "A numeric story-points field is not available for this project and issue type.");
                    Environment.ExitCode = 1;
                    return;
                }
                fields[storyPointsField.Id] = storyPoints.Value;
            }

            try
            {
                JiraFieldService.ApplyAdditionalFields(fields, additionalFields, createMetadata.Fields);
                JiraFieldService.ValidateRequiredFields(createMetadata.Fields, fields.Keys);
            }
            catch (Exception ex) { PrintDynamicFieldError(ex); return; }

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
        var descFormatOption = new Option<string>("--description-format") { Description = "Description format: plain, markdown, or adf", DefaultValueFactory = _ => "plain" };
        var assigneeOption = new Option<string?>("--assignee") { Description = "New assignee email, account ID, or 'none' to unassign" };
        var labelOption = new Option<string?>("--label") { Description = "Comma-separated labels (replaces existing)" };
        var priorityOption = new Option<string?>("--priority") { Description = "Priority name (e.g. High, Medium, Low)" };
        var storyPointsOption = new Option<double?>("--story-points") { Description = "Story point estimate" };
        var startDateOption = new Option<string?>("--start-date") { Description = "Start date in ISO format (e.g. 2026-04-07)" };
        var dueDateOption = new Option<string?>("--due-date") { Description = "Due date in ISO format (e.g. 2026-04-14)" };
        var parentOption = new Option<string?>("--parent") { Description = "New parent/epic issue key, or 'none' to remove parent" };
        var fieldsJsonOption = new Option<string?>("--fields-json")
        {
            Description = "Additional Jira fields as a JSON object keyed by field ID or unique field name"
        };

        var cmd = new Command("edit", "Edit a work item") { keyArg, summaryOption, descriptionOption, descFormatOption, assigneeOption, labelOption, priorityOption, storyPointsOption, startDateOption, dueDateOption, parentOption, fieldsJsonOption };
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
            var startDate = parseResult.GetValue(startDateOption);
            var dueDate = parseResult.GetValue(dueDateOption);
            var parent = parseResult.GetValue(parentOption);
            var fieldsJson = parseResult.GetValue(fieldsJsonOption);

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            IReadOnlyDictionary<string, JsonElement> additionalFields;
            try { additionalFields = JiraFieldService.ParseAdditionalFields(fieldsJson); }
            catch (Exception ex) { PrintDynamicFieldError(ex); return; }

            IReadOnlyList<JiraFieldMetadata>? editMetadata = null;
            if (storyPoints.HasValue || !string.IsNullOrEmpty(startDate) || !string.IsNullOrEmpty(dueDate) || additionalFields.Count > 0)
            {
                try { editMetadata = await JiraFieldService.GetEditMetadataAsync(client, key, ct); }
                catch (Exception ex) { PrintDynamicFieldError(ex); return; }
            }

            var fields = new Dictionary<string, object?>();

            if (!string.IsNullOrEmpty(summary))
                fields["summary"] = summary;

            if (!string.IsNullOrEmpty(description))
            {
                fields["description"] = descFormat switch
                {
                    "markdown" => AdfConverter.ConvertMarkdownToAdf(description),
                    "adf" => AdfConverter.ParseRawAdf(description),
                    _ => AdfConverter.CreatePlainTextAdf(description)
                };
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase))
                    fields["assignee"] = null!;
                else
                {
                    var accountId = await ResolveAssignee(client, assignee, ct);
                    if (accountId != null)
                        fields["assignee"] = new { accountId };
                }
            }

            if (!string.IsNullOrEmpty(labels))
                fields["labels"] = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (!string.IsNullOrEmpty(priority))
                fields["priority"] = new { name = priority };

            if (storyPoints.HasValue)
            {
                var storyPointsField = JiraFieldService.FindPreferredField(
                    editMetadata!,
                    AuthService.LoadConfig().StoryPointsField,
                    ["Story point estimate", "Story Points"],
                    "number");
                if (storyPointsField == null)
                {
                    OutputService.PrintError("field_not_available", "A numeric story-points field is not editable on this work item.");
                    Environment.ExitCode = 1;
                    return;
                }
                fields[storyPointsField.Id] = storyPoints.Value;
            }

            if (!string.IsNullOrEmpty(startDate))
            {
                var startDateField = JiraFieldService.FindPreferredField(
                    editMetadata!,
                    AuthService.LoadConfig().StartDateField,
                    ["Start date", "Start Date"],
                    "date");
                if (startDateField == null)
                {
                    OutputService.PrintError("field_not_available", "A start-date field is not editable on this work item.");
                    Environment.ExitCode = 1;
                    return;
                }
                fields[startDateField.Id] = startDate;
            }

            if (!string.IsNullOrEmpty(dueDate))
            {
                var dueDateField = JiraFieldService.FindPreferredField(editMetadata!, "duedate", ["Due date", "Due Date"], "date");
                if (dueDateField == null)
                {
                    OutputService.PrintError("field_not_available", "The due-date field is not editable on this work item.");
                    Environment.ExitCode = 1;
                    return;
                }
                fields[dueDateField.Id] = dueDate;
            }

            try { JiraFieldService.ApplyAdditionalFields(fields, additionalFields, editMetadata ?? Array.Empty<JiraFieldMetadata>()); }
            catch (Exception ex) { PrintDynamicFieldError(ex); return; }

            if (!string.IsNullOrEmpty(parent))
            {
                fields["parent"] = parent.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? null!
                    : new { key = parent };
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
        var assigneeOption = new Option<string>("--assignee") { Description = "Assignee email, account ID, '@me' for self-assign, or 'none' to unassign",  Required = true };
        var cmd = new Command("assign", "Assign a work item") { keyOption, assigneeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyOption)!;
            var assignee = parseResult.GetValue(assigneeOption)!;

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();

            if (assignee.Equals("none", StringComparison.OrdinalIgnoreCase) || assignee == "")
            {
                var result = await ApiHelper.PutAsync(client, $"issue/{Uri.EscapeDataString(key)}/assignee", new { accountId = (string?)null }, ct);
                if (result == null) return;
                OutputService.Print(new { Status = "unassigned", Key = key }, format);
                return;
            }

            var accountId = await ResolveAssignee(client, assignee, ct);
            if (accountId == null) return;

            var result2 = await ApiHelper.PutAsync(client, $"issue/{Uri.EscapeDataString(key)}/assignee", new { accountId }, ct);
            if (result2 == null) return;

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

    private static object FormatIssue(JsonElement issue, string descFormat = "plain")
    {
        issue.TryGetProperty("fields", out var fields);
        var config = AuthService.LoadConfig();
        double? storyPoints = null;
        if (fields.TryGetProperty(config.StoryPointsField, out var spValue) && spValue.ValueKind == JsonValueKind.Number)
            storyPoints = spValue.GetDouble();

        // Try team-managed start date field, fall back to company-managed
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
            AssigneeId = fields.GetString("assignee", "accountId"),
            Reporter = fields.GetString("reporter", "displayName"),
            ReporterId = fields.GetString("reporter", "accountId"),
            Created = fields.GetString("created"),
            Updated = fields.GetString("updated"),
            Description = description
        };
    }

    private static void PrintDynamicFieldError(Exception ex)
    {
        if (ex is AtlasApiException apiException)
            OutputService.PrintError(((int)apiException.StatusCode).ToString(), apiException.Message);
        else
            OutputService.PrintError("dynamic_fields", ex.Message);
        Environment.ExitCode = 1;
    }
}
