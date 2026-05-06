using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class LinkTools
{
    [McpServerTool(Name = "jira_link_types"), Description("List available Jira issue link types (e.g. Blocks, Relates, Cloners)")]
    public static async Task<string> ListTypes()
    {
        try
        {
            var result = await LinkService.ListTypesAsync();
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_link_list"), Description("List issue links on a Jira work item")]
    public static async Task<string> List(
        [Description("Work item key (e.g. PROJ-123)")] string key)
    {
        try
        {
            var result = await LinkService.ListAsync(key);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_link_create"), Description("Create a link between two Jira work items (e.g. 'PROJ-1 blocks PROJ-2' uses from=PROJ-1 to=PROJ-2 type=Blocks)")]
    public static async Task<string> Create(
        [Description("Source issue key (the one performing the relationship)")] string from,
        [Description("Target issue key (the one receiving the relationship)")] string to,
        [Description("Link type name, e.g. Blocks, Relates, Cloners, Duplicate")] string type,
        [Description("Optional comment to add when creating the link")] string? comment = null)
    {
        try
        {
            var result = await LinkService.CreateAsync(from, to, type, comment);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_link_delete"), Description("Remove a link between two Jira work items by link ID")]
    public static async Task<string> Delete(
        [Description("Link ID (from jira_link_list)")] string id)
    {
        try
        {
            var result = await LinkService.DeleteAsync(id);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
