using System.CommandLine;
using System.Text.Json;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class SpaceCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("space", "Confluence space operations");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildView(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var limitOption = new Option<int>("--limit") { Description = "Maximum number of spaces to return", DefaultValueFactory = _ => 25 };
        var typeOption = new Option<string?>("--type") { Description = "Filter by type (global or personal)" };
        var cmd = new Command("list", "List Confluence spaces") { limitOption, typeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var limit = parseResult.GetValue(limitOption);
            var type = parseResult.GetValue(typeOption);

            using var client = AtlasClientFactory.CreateConfluenceClient();
            var url = $"spaces?limit={limit}";
            if (!string.IsNullOrEmpty(type))
                url += $"&type={Uri.EscapeDataString(type)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            var spaces = data.Value.GetProperty("results").EnumerateArray().Select(s => new
            {
                Id = s.GetString("id"),
                Key = s.GetString("key"),
                Name = s.GetString("name"),
                Type = s.GetString("type"),
                Status = s.GetString("status")
            });

            OutputService.Print(spaces, format);
        });
        return cmd;
    }

    private static Command BuildView(Option<string> formatOption)
    {
        var idArg = new Argument<string>("id") { Description = "Space ID" };
        var cmd = new Command("view", "View a Confluence space") { idArg };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var id = parseResult.GetValue(idArg)!;

            if (!AllowedSpacesService.CheckAndPrompt(id, "read", "confluence")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateConfluenceClient();
            var data = await ApiHelper.GetAsync(client, $"spaces/{Uri.EscapeDataString(id)}?description-format=plain", ct);
            if (data == null) return;

            var s = data.Value;
            OutputService.Print(new
            {
                Id = s.GetString("id"),
                Key = s.GetString("key"),
                Name = s.GetString("name"),
                Type = s.GetString("type"),
                Status = s.GetString("status"),
                Description = s.GetString("description", "plain", "value"),
                HomepageId = s.GetString("homepageId")
            }, format);
        });
        return cmd;
    }
}
