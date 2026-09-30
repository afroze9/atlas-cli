using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class FieldTools
{
    [McpServerTool(Name = "jira_field_list"), Description("List Jira system and custom fields with their IDs and schemas")]
    public static async Task<string> List(
        [Description("Filter by field name or description")] string? query = null,
        [Description("Field type: all, custom, or system")] string type = "all")
    {
        try
        {
            var result = await JiraFieldService.ListAsync(query, type);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_create_field_metadata"), Description("Get required and optional fields, schemas, defaults, and allowed values for a project and issue type")]
    public static async Task<string> CreateMetadata(
        [Description("Jira project key")] string project,
        [Description("Issue type name or ID")] string issueType)
    {
        try
        {
            var result = await JiraFieldService.GetCreateMetadataAsync(project, issueType);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "jira_edit_field_metadata"), Description("Get fields currently editable on a Jira work item, including schemas, operations, and allowed values")]
    public static async Task<string> EditMetadata(
        [Description("Work item key (e.g. PROJ-123)")] string key)
    {
        try
        {
            var result = await JiraFieldService.GetEditMetadataAsync(key);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
