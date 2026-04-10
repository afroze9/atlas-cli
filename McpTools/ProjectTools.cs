using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "jira_project_list"), Description("List Jira projects")]
    public static async Task<string> List(
        [Description("Maximum projects to return (default: 50)")] int limit = 50)
    {
        try
        {
            var result = await JiraProjectService.ListAsync(limit);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_project_view"), Description("View Jira project details")]
    public static async Task<string> View(
        [Description("Project key (e.g. PROJ)")] string key)
    {
        try
        {
            var result = await JiraProjectService.ViewAsync(key);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
