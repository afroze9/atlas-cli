using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class PageTools
{
    [McpServerTool(Name = "confluence_page_list"), Description("List Confluence pages, optionally filtered by space, title, status, or subtype")]
    public static async Task<string> List(
        [Description("Filter by space ID")] string? spaceId = null,
        [Description("Filter by page title")] string? title = null,
        [Description("Maximum pages to return (default: 25)")] int limit = 25,
        [Description("Filter by status: current, draft, trashed")] string? status = null,
        [Description("Filter by subtype: page or live")] string? subtype = null)
    {
        try
        {
            var result = await PageService.ListAsync(spaceId, title, limit, status, subtype);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "confluence_page_view"), Description("View a Confluence page by ID")]
    public static async Task<string> View(
        [Description("Page ID")] string id,
        [Description("Body format: storage, atlas_doc_format, or view (default: storage)")] string bodyFormat = "storage")
    {
        try
        {
            var result = await PageService.ViewAsync(id, bodyFormat);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "confluence_page_create"), Description("Create a new Confluence page or live doc")]
    public static async Task<string> Create(
        [Description("Confluence space ID")] string spaceId,
        [Description("Page title")] string title,
        [Description("Page content")] string body,
        [Description("Body format: plain, markdown, or adf (default: markdown)")] string bodyFormat = "markdown",
        [Description("Parent page ID")] string? parentId = null,
        [Description("Page status: current or draft (default: current)")] string status = "current",
        [Description("Page subtype: page or live (default: page)")] string? subtype = null)
    {
        try
        {
            var result = await PageService.CreateAsync(spaceId, title, body, bodyFormat, parentId, status, subtype);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "confluence_page_update"), Description("Update an existing Confluence page")]
    public static async Task<string> Update(
        [Description("Page ID")] string id,
        [Description("New page title")] string? title = null,
        [Description("New page content")] string? body = null,
        [Description("Body format: plain, markdown, or adf (default: markdown)")] string bodyFormat = "markdown",
        [Description("Version message")] string? message = null)
    {
        try
        {
            var result = await PageService.UpdateAsync(id, title, body, bodyFormat, message);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
