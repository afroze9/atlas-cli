using System.CommandLine;
using System.Text.Json;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class PageCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("page", "Confluence page operations");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildView(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var spaceIdOption = new Option<string?>("--space-id") { Description = "Filter by space ID" };
        var titleOption = new Option<string?>("--title") { Description = "Filter by page title" };
        var limitOption = new Option<int>("--limit") { Description = "Maximum number of pages to return", DefaultValueFactory = _ => 25 };
        var statusOption = new Option<string?>("--status") { Description = "Filter by status (current, draft, trashed)" };
        var cmd = new Command("list", "List Confluence pages") { spaceIdOption, titleOption, limitOption, statusOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var spaceId = parseResult.GetValue(spaceIdOption);
            var title = parseResult.GetValue(titleOption);
            var limit = parseResult.GetValue(limitOption);
            var status = parseResult.GetValue(statusOption);

            if (!string.IsNullOrEmpty(spaceId))
            {
                if (!AllowedSpacesService.CheckAndPrompt(spaceId, "read", "confluence")) { Environment.ExitCode = 1; return; }
            }

            using var client = AtlasClientFactory.CreateConfluenceClient();
            var url = $"pages?limit={limit}";
            if (!string.IsNullOrEmpty(spaceId))
                url += $"&space-id={Uri.EscapeDataString(spaceId)}";
            if (!string.IsNullOrEmpty(title))
                url += $"&title={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrEmpty(status))
                url += $"&status={Uri.EscapeDataString(status)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            var pages = data.Value.GetProperty("results").EnumerateArray().Select(p => new
            {
                Id = p.GetString("id"),
                Title = p.GetString("title"),
                SpaceId = p.GetString("spaceId"),
                Status = p.GetString("status"),
                ParentId = p.GetString("parentId"),
                AuthorId = p.GetString("authorId"),
                CreatedAt = p.GetString("createdAt"),
                Version = p.GetString("version", "number")
            });

            OutputService.Print(pages, format);
        });
        return cmd;
    }

    private static Command BuildView(Option<string> formatOption)
    {
        var idArg = new Argument<string>("id") { Description = "Page ID" };
        var bodyFormatOption = new Option<string?>("--body-format") { Description = "Body format: storage, atlas_doc_format, or view (default: storage)" };
        var cmd = new Command("view", "View a Confluence page") { idArg, bodyFormatOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var id = parseResult.GetValue(idArg)!;
            var bodyFormat = parseResult.GetValue(bodyFormatOption) ?? "storage";

            using var client = AtlasClientFactory.CreateConfluenceClient();
            var data = await ApiHelper.GetAsync(client, $"pages/{Uri.EscapeDataString(id)}?body-format={Uri.EscapeDataString(bodyFormat)}", ct);
            if (data == null) return;

            var p = data.Value;
            var pageSpaceId = p.GetString("spaceId");
            if (!string.IsNullOrEmpty(pageSpaceId))
            {
                if (!AllowedSpacesService.CheckAndPrompt(pageSpaceId, "read")) { Environment.ExitCode = 1; return; }
            }

            OutputService.Print(new
            {
                Id = p.GetString("id"),
                Title = p.GetString("title"),
                SpaceId = pageSpaceId,
                Status = p.GetString("status"),
                AuthorId = p.GetString("authorId"),
                CreatedAt = p.GetString("createdAt"),
                Version = p.GetString("version", "number"),
                Body = p.GetString("body", bodyFormat, "value")
            }, format);
        });
        return cmd;
    }
}
