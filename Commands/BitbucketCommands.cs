using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class BitbucketCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("bitbucket", "Bitbucket Cloud operations");
        cmd.Subcommands.Add(BuildWorkspace(formatOption));
        cmd.Subcommands.Add(BuildRepo(formatOption));
        cmd.Subcommands.Add(BuildPipeline(formatOption));
        return cmd;
    }

    // ---------- workspace ----------

    private static Command BuildWorkspace(Option<string> formatOption)
    {
        var cmd = new Command("workspace", "Workspace operations");

        var limitOption = new Option<int>("--limit") { Description = "Max workspaces to return", DefaultValueFactory = _ => 25 };
        var list = new Command("list", "List workspaces you can access") { limitOption };
        list.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var limit = parseResult.GetValue(limitOption);
            try
            {
                var data = await BitbucketService.WorkspaceListAsync(limit, ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(list);
        return cmd;
    }

    // ---------- repo ----------

    private static Command BuildRepo(Option<string> formatOption)
    {
        var cmd = new Command("repo", "Repository operations");

        var workspaceOption = new Option<string?>("--workspace") { Description = "Workspace slug (defaults to configured workspace)" };

        var limitOption = new Option<int>("--limit") { Description = "Max repos to return", DefaultValueFactory = _ => 25 };
        var queryOption = new Option<string?>("--query") { Description = "Filter by repo name (substring match)" };
        var list = new Command("list", "List repositories in a workspace") { workspaceOption, limitOption, queryOption };
        list.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.RepoListAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(limitOption),
                    parseResult.GetValue(queryOption),
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(list);

        var repoOption = new Option<string>("--repo") { Description = "Repository slug", Required = true };
        var view = new Command("view", "View a repository") { workspaceOption, repoOption };
        view.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.RepoViewAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(view);

        return cmd;
    }

    // ---------- pipeline ----------

    private static Command BuildPipeline(Option<string> formatOption)
    {
        var cmd = new Command("pipeline", "Pipeline operations");

        var workspaceOption = new Option<string?>("--workspace") { Description = "Workspace slug (defaults to configured workspace)" };
        var repoOption = new Option<string>("--repo") { Description = "Repository slug", Required = true };
        var idOption = new Option<string>("--id") { Description = "Pipeline build number or UUID", Required = true };

        // list
        var branchOption = new Option<string?>("--branch") { Description = "Filter by branch" };
        var statusOption = new Option<string?>("--status") { Description = "Filter by state (PENDING, IN_PROGRESS, COMPLETED)" };
        var limitOption = new Option<int>("--limit") { Description = "Max pipelines to return", DefaultValueFactory = _ => 25 };
        var list = new Command("list", "List pipeline runs") { workspaceOption, repoOption, branchOption, statusOption, limitOption };
        list.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.PipelineListAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(branchOption),
                    parseResult.GetValue(statusOption),
                    parseResult.GetValue(limitOption),
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(list);

        // view
        var view = new Command("view", "View a pipeline run") { workspaceOption, repoOption, idOption };
        view.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.PipelineViewAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(idOption)!,
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(view);

        // steps
        var steps = new Command("steps", "List steps for a pipeline run") { workspaceOption, repoOption, idOption };
        steps.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.PipelineStepsAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(idOption)!,
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(steps);

        // log
        var stepOption = new Option<string?>("--step") { Description = "Step UUID (defaults to first failed, else first step)" };
        var tailOption = new Option<int?>("--tail") { Description = "Print only the last N lines" };
        var log = new Command("log", "Print log for a pipeline step") { workspaceOption, repoOption, idOption, stepOption, tailOption };
        log.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var text = await BitbucketService.PipelineLogAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(idOption)!,
                    parseResult.GetValue(stepOption),
                    parseResult.GetValue(tailOption),
                    ct);
                Console.WriteLine(text);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(log);

        // stop
        var stop = new Command("stop", "Stop a running pipeline") { workspaceOption, repoOption, idOption };
        stop.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.PipelineStopAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(idOption)!,
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(stop);

        // run
        var runBranch = new Option<string?>("--branch") { Description = "Branch to run pipeline on" };
        var commitOption = new Option<string?>("--commit") { Description = "Commit SHA" };
        var customOption = new Option<string?>("--custom") { Description = "Name of a custom pipeline from bitbucket-pipelines.yml" };
        var run = new Command("run", "Trigger a new pipeline") { workspaceOption, repoOption, runBranch, commitOption, customOption };
        run.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var data = await BitbucketService.PipelineRunAsync(
                    ResolveWorkspace(parseResult.GetValue(workspaceOption)),
                    parseResult.GetValue(repoOption)!,
                    parseResult.GetValue(runBranch),
                    parseResult.GetValue(commitOption),
                    parseResult.GetValue(customOption),
                    ct);
                OutputService.Print(data, format);
            }
            catch (Exception ex) { HandleError(ex); }
        });
        cmd.Subcommands.Add(run);

        return cmd;
    }

    private static string ResolveWorkspace(string? supplied)
    {
        if (!string.IsNullOrEmpty(supplied)) return supplied;
        var defaultWs = AtlasClientFactory.GetDefaultBitbucketWorkspace();
        if (!string.IsNullOrEmpty(defaultWs)) return defaultWs;
        throw new ArgumentException("--workspace not provided and no default configured. Run 'atlas-cli auth bitbucket-set-workspace <slug>' or pass --workspace.");
    }

    private static void HandleError(Exception ex)
    {
        if (ex is AtlasApiException api)
            OutputService.PrintError(((int)api.StatusCode).ToString(), api.Message);
        else
            OutputService.PrintError(ex.GetType().Name, ex.Message);
        Environment.ExitCode = 1;
    }
}
