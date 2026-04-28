using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class BitbucketTools
{
    [McpServerTool(Name = "bitbucket_workspace_list"), Description("List Bitbucket workspaces accessible to the current user")]
    public static async Task<string> WorkspaceList(
        [Description("Maximum workspaces to return (default: 25)")] int limit = 25)
    {
        try
        {
            var result = await BitbucketService.WorkspaceListAsync(limit);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_repo_list"), Description("List repositories in a Bitbucket workspace")]
    public static async Task<string> RepoList(
        [Description("Workspace slug")] string workspace,
        [Description("Maximum repos to return (default: 25)")] int limit = 25,
        [Description("Substring filter on repo name")] string? query = null)
    {
        try
        {
            var result = await BitbucketService.RepoListAsync(workspace, limit, query);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_repo_view"), Description("View a Bitbucket repository")]
    public static async Task<string> RepoView(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo)
    {
        try
        {
            var result = await BitbucketService.RepoViewAsync(workspace, repo);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_list"), Description("List pipeline runs for a Bitbucket repository")]
    public static async Task<string> PipelineList(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Filter by branch name")] string? branch = null,
        [Description("Filter by state: PENDING, IN_PROGRESS, COMPLETED")] string? status = null,
        [Description("Maximum pipelines to return (default: 25)")] int limit = 25)
    {
        try
        {
            var result = await BitbucketService.PipelineListAsync(workspace, repo, branch, status, limit);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_view"), Description("View a pipeline run by build number or UUID")]
    public static async Task<string> PipelineView(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Pipeline build number or UUID (with braces)")] string id)
    {
        try
        {
            var result = await BitbucketService.PipelineViewAsync(workspace, repo, id);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_steps"), Description("List steps for a pipeline run")]
    public static async Task<string> PipelineSteps(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Pipeline build number or UUID")] string id)
    {
        try
        {
            var result = await BitbucketService.PipelineStepsAsync(workspace, repo, id);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_log"), Description("Fetch raw log for a pipeline step. Defaults to first failed step, or first step if none failed. Use tail to limit output.")]
    public static async Task<string> PipelineLog(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Pipeline build number or UUID")] string id,
        [Description("Step UUID (optional)")] string? step = null,
        [Description("Return only the last N lines")] int? tail = null)
    {
        try
        {
            return await BitbucketService.PipelineLogAsync(workspace, repo, id, step, tail);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_stop"), Description("Stop a running pipeline")]
    public static async Task<string> PipelineStop(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Pipeline build number or UUID")] string id)
    {
        try
        {
            var result = await BitbucketService.PipelineStopAsync(workspace, repo, id);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }

    [McpServerTool(Name = "bitbucket_pipeline_run"), Description("Trigger a new pipeline run on a branch and/or commit, optionally selecting a custom pipeline from bitbucket-pipelines.yml")]
    public static async Task<string> PipelineRun(
        [Description("Workspace slug")] string workspace,
        [Description("Repository slug")] string repo,
        [Description("Branch name (required unless commit is given)")] string? branch = null,
        [Description("Commit SHA")] string? commit = null,
        [Description("Custom pipeline name from bitbucket-pipelines.yml")] string? custom = null)
    {
        try
        {
            var result = await BitbucketService.PipelineRunAsync(workspace, repo, branch, commit, custom);
            return McpAtlasHelper.ToJson(result);
        }
        catch (AtlasApiException ex) { return McpAtlasHelper.HandleApiError(ex); }
        catch (Exception ex) { return McpAtlasHelper.HandleException(ex); }
    }
}
