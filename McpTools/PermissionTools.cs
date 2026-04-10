using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class PermissionTools
{
    [McpServerTool(Name = "permissions_list"), Description("List allowed Jira projects and Confluence spaces with their permitted actions")]
    public static Task<string> List(
        [Description("Filter by type: jira or confluence")] string? type = null)
    {
        try
        {
            var result = PermissionService.List(type);
            return Task.FromResult(McpAtlasHelper.ToJson(result));
        }
        catch (Exception ex) { return Task.FromResult(McpAtlasHelper.HandleException(ex)); }
    }

    [McpServerTool(Name = "permissions_allow"), Description("Add or update an allowed Jira project or Confluence space")]
    public static Task<string> Allow(
        [Description("Jira project key (e.g. PROJ) or Confluence space ID")] string identifier,
        [Description("Comma-separated allowed actions: read, write, delete")] string actions,
        [Description("Display name")] string? name = null,
        [Description("Type: jira or confluence (default: jira)")] string type = "jira")
    {
        try
        {
            var result = PermissionService.Allow(identifier, actions, name, type);
            return Task.FromResult(McpAtlasHelper.ToJson(result));
        }
        catch (Exception ex) { return Task.FromResult(McpAtlasHelper.HandleException(ex)); }
    }

    [McpServerTool(Name = "permissions_remove"), Description("Remove a Jira project or Confluence space from the allowed list")]
    public static Task<string> Remove(
        [Description("Jira project key or Confluence space ID to remove")] string identifier,
        [Description("Type: jira or confluence (default: jira)")] string type = "jira")
    {
        try
        {
            var result = PermissionService.Remove(identifier, type);
            return Task.FromResult(McpAtlasHelper.ToJson(result));
        }
        catch (Exception ex) { return Task.FromResult(McpAtlasHelper.HandleException(ex)); }
    }
}
