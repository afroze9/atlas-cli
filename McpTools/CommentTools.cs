using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class CommentTools
{
    [McpServerTool(Name = "jira_comment_list"), Description("List comments on a Jira work item")]
    public static async Task<string> List(
        [Description("Work item key (e.g. PROJ-123)")] string key)
    {
        try
        {
            var result = await CommentService.ListAsync(key);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_comment_create"), Description("Add a comment to a Jira work item, optionally @-mentioning users by accountId")]
    public static async Task<string> Create(
        [Description("Work item key (e.g. PROJ-123)")] string key,
        [Description("Comment text")] string body,
        [Description("Body format: plain, markdown, or adf")] string bodyFormat = "plain",
        [Description("Comma-separated Atlassian account IDs to @-mention (prepended to body). Use jira_user_search to find IDs.")] string? mentions = null)
    {
        try
        {
            var result = await CommentService.CreateAsync(key, body, bodyFormat, mentions);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
