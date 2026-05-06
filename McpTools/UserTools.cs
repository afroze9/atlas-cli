using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class UserTools
{
    [McpServerTool(Name = "jira_user_search"), Description("Search Jira users by display name or email. Returns accountIds usable for assignment and @-mentions.")]
    public static async Task<string> Search(
        [Description("Display name or email to search for")] string query,
        [Description("Max results (default: 50)")] int limit = 50)
    {
        try
        {
            var result = await UserService.SearchAsync(query, limit);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
