using System.CommandLine;
using System.Text.Json;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class ProjectCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("project", "Jira project operations");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildView(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var limitOption = new Option<int>("--limit") { Description = "Maximum number of projects to return",  DefaultValueFactory = _ => 50 };
        var cmd = new Command("list", "List projects") { limitOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var limit = parseResult.GetValue(limitOption);

            using var client = AtlasClientFactory.CreateJiraClient();
            var data = await ApiHelper.GetAsync(client, $"project/search?maxResults={limit}", ct);
            if (data == null) return;

            var projects = data.Value.GetProperty("values").EnumerateArray().Select(p => new
            {
                Key = p.GetString("key"),
                Name = p.GetString("name"),
                Type = p.GetString("projectTypeKey"),
                Style = p.GetString("style"),
                Lead = p.GetString("lead", "displayName")
            });

            OutputService.Print(projects, format);
        });
        return cmd;
    }

    private static Command BuildView(Option<string> formatOption)
    {
        var keyArg = new Argument<string>("key") { Description = "Project key (e.g. PROJ)" };
        var cmd = new Command("view", "View project details") { keyArg };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyArg)!;

            if (!AllowedSpacesService.CheckAndPrompt(key.ToUpperInvariant(), "read")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var data = await ApiHelper.GetAsync(client, $"project/{Uri.EscapeDataString(key)}", ct);
            if (data == null) return;

            var p = data.Value;
            OutputService.Print(new
            {
                Key = p.GetString("key"),
                Name = p.GetString("name"),
                Type = p.GetString("projectTypeKey"),
                Style = p.GetString("style"),
                Lead = p.GetString("lead", "displayName"),
                Description = p.GetString("description"),
                Url = p.GetString("self")
            }, format);
        });
        return cmd;
    }
}
