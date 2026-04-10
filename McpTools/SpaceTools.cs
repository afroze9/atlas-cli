using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class SpaceTools
{
    [McpServerTool(Name = "confluence_space_list"), Description("List Confluence spaces")]
    public static async Task<string> List(
        [Description("Maximum spaces to return (default: 25)")] int limit = 25,
        [Description("Filter by type: global or personal")] string? type = null)
    {
        try
        {
            var result = await SpaceService.ListAsync(limit, type);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "confluence_space_view"), Description("View a Confluence space by ID")]
    public static async Task<string> View(
        [Description("Space ID")] string id)
    {
        try
        {
            var result = await SpaceService.ViewAsync(id);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
