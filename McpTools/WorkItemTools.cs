using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class WorkItemTools
{
    [McpServerTool(Name = "jira_view"), Description("View a Jira work item by key")]
    public static async Task<string> View(
        [Description("Work item key (e.g. PROJ-123)")] string key,
        [Description("Comma-separated list of fields to return")] string? fields = null,
        [Description("Description format: plain, markdown, or adf")] string descriptionFormat = "plain")
    {
        try
        {
            var result = await WorkItemService.ViewAsync(key, fields, descriptionFormat);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_search"), Description("Search Jira work items using JQL")]
    public static async Task<string> Search(
        [Description("JQL query string")] string jql,
        [Description("Comma-separated list of fields")] string? fields = null,
        [Description("Maximum results (default: 50)")] int limit = 50,
        [Description("Only return the count")] bool countOnly = false,
        [Description("Description format: plain, markdown, or adf")] string descriptionFormat = "plain")
    {
        try
        {
            var result = await WorkItemService.SearchAsync(jql, fields, limit, countOnly, descriptionFormat);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_create"), Description("Create a new Jira work item")]
    public static async Task<string> Create(
        [Description("Project key (e.g. PROJ)")] string project,
        [Description("Issue type (e.g. Story, Task, Bug)")] string type,
        [Description("Issue summary")] string summary,
        [Description("Issue description")] string? description = null,
        [Description("Description format: plain, markdown, or adf")] string descriptionFormat = "plain",
        [Description("Assignee email, account ID, '@me', or 'none'")] string? assignee = null,
        [Description("Comma-separated labels")] string? labels = null,
        [Description("Parent issue key")] string? parent = null,
        [Description("Story point estimate")] double? storyPoints = null)
    {
        try
        {
            var result = await WorkItemService.CreateAsync(project, type, summary, description, descriptionFormat, assignee, labels, parent, storyPoints);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_edit"), Description("Edit an existing Jira work item")]
    public static async Task<string> Edit(
        [Description("Work item key (e.g. PROJ-123)")] string key,
        [Description("New summary")] string? summary = null,
        [Description("New description")] string? description = null,
        [Description("Description format: plain, markdown, or adf")] string descriptionFormat = "plain",
        [Description("New assignee email, account ID, or 'none'")] string? assignee = null,
        [Description("Comma-separated labels (replaces existing)")] string? labels = null,
        [Description("Priority name (e.g. High, Medium, Low)")] string? priority = null,
        [Description("Story point estimate")] double? storyPoints = null,
        [Description("Start date in ISO format (e.g. 2026-04-07)")] string? startDate = null,
        [Description("Due date in ISO format (e.g. 2026-04-14)")] string? dueDate = null)
    {
        try
        {
            var result = await WorkItemService.EditAsync(key, summary, description, descriptionFormat, assignee, labels, priority, storyPoints, startDate, dueDate);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_transition"), Description("Transition a Jira work item to a new status")]
    public static async Task<string> Transition(
        [Description("Work item key(s), comma-separated")] string keys,
        [Description("Target status name (e.g. 'In Progress', 'Done')")] string status)
    {
        try
        {
            var result = await WorkItemService.TransitionAsync(keys, status);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_assign"), Description("Assign a Jira work item")]
    public static async Task<string> Assign(
        [Description("Work item key")] string key,
        [Description("Assignee email, account ID, '@me', or 'none' to unassign")] string assignee)
    {
        try
        {
            var result = await WorkItemService.AssignAsync(key, assignee);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
