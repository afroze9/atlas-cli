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
        cmd.Subcommands.Add(BuildCreate(formatOption));
        cmd.Subcommands.Add(BuildUpdate(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var spaceIdOption = new Option<string?>("--space-id") { Description = "Filter by space ID" };
        var titleOption = new Option<string?>("--title") { Description = "Filter by page title" };
        var limitOption = new Option<int>("--limit") { Description = "Maximum number of pages to return", DefaultValueFactory = _ => 25 };
        var statusOption = new Option<string?>("--status") { Description = "Filter by status (current, draft, trashed)" };
        var subtypeOption = new Option<string?>("--subtype") { Description = "Filter by subtype (page or live)" };
        var cmd = new Command("list", "List Confluence pages") { spaceIdOption, titleOption, limitOption, statusOption, subtypeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var spaceId = parseResult.GetValue(spaceIdOption);
            var title = parseResult.GetValue(titleOption);
            var limit = parseResult.GetValue(limitOption);
            var status = parseResult.GetValue(statusOption);
            var subtype = parseResult.GetValue(subtypeOption);

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
            if (!string.IsNullOrEmpty(subtype))
                url += $"&subtype={Uri.EscapeDataString(subtype)}";

            var data = await ApiHelper.GetAsync(client, url, ct);
            if (data == null) return;

            var pages = data.Value.GetProperty("results").EnumerateArray().Select(p => new
            {
                Id = p.GetString("id"),
                Title = p.GetString("title"),
                SpaceId = p.GetString("spaceId"),
                Status = p.GetString("status"),
                Subtype = p.GetString("subtype"),
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
                Subtype = p.GetString("subtype"),
                AuthorId = p.GetString("authorId"),
                CreatedAt = p.GetString("createdAt"),
                Version = p.GetString("version", "number"),
                Body = p.GetString("body", bodyFormat, "value")
            }, format);
        });
        return cmd;
    }

    private static string ResolveBody(string? body, string? bodyFile)
    {
        if (!string.IsNullOrEmpty(bodyFile))
        {
            var fullPath = Path.GetFullPath(bodyFile);
            var allowedDir = Path.GetFullPath(Directory.GetCurrentDirectory()) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(allowedDir, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Access denied: --body-file must reference a file within the current directory. Got: {bodyFile}");
            return File.ReadAllText(fullPath);
        }
        return body!;
    }

    private static Command BuildCreate(Option<string> formatOption)
    {
        var spaceIdOption = new Option<string>("--space-id") { Description = "Confluence space ID", Required = true };
        var titleOption = new Option<string>("--title") { Description = "Page title", Required = true };
        var bodyOption = new Option<string?>("--body") { Description = "Page content (or use --body-file)" };
        var bodyFileOption = new Option<string?>("--body-file") { Description = "Read page content from file" };
        var bodyFormatOption = new Option<string>("--body-format") { Description = "Body format: plain, markdown, or adf (default: markdown)", DefaultValueFactory = _ => "markdown" };
        var parentIdOption = new Option<string?>("--parent-id") { Description = "Parent page ID" };
        var statusOption = new Option<string>("--status") { Description = "Page status: current or draft (default: current)", DefaultValueFactory = _ => "current" };
        var subtypeOption = new Option<string?>("--subtype") { Description = "Page subtype: page or live (default: page)" };
        var cmd = new Command("create", "Create a Confluence page") { spaceIdOption, titleOption, bodyOption, bodyFileOption, bodyFormatOption, parentIdOption, statusOption, subtypeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var spaceId = parseResult.GetValue(spaceIdOption)!;
            var title = parseResult.GetValue(titleOption)!;
            var bodyRaw = parseResult.GetValue(bodyOption);
            var bodyFile = parseResult.GetValue(bodyFileOption);
            var bodyFormat = parseResult.GetValue(bodyFormatOption)!;

            if (string.IsNullOrEmpty(bodyRaw) && string.IsNullOrEmpty(bodyFile))
            {
                OutputService.PrintError("validation", "Either --body or --body-file must be provided.");
                Environment.ExitCode = 1;
                return;
            }

            string body;
            try { body = ResolveBody(bodyRaw, bodyFile); }
            catch (Exception ex)
            {
                OutputService.PrintError("file", ex.Message);
                Environment.ExitCode = 1;
                return;
            }
            var parentId = parseResult.GetValue(parentIdOption);
            var status = parseResult.GetValue(statusOption)!;
            var subtype = parseResult.GetValue(subtypeOption);

            if (!string.IsNullOrEmpty(subtype) && subtype != "page" && subtype != "live")
            {
                OutputService.PrintError("validation", "--subtype must be 'page' or 'live'.");
                Environment.ExitCode = 1;
                return;
            }

            if (!AllowedSpacesService.CheckAndPrompt(spaceId, "write", "confluence")) { Environment.ExitCode = 1; return; }

            var adfBody = bodyFormat switch
            {
                "adf" => AdfConverter.ParseRawAdf(body),
                "plain" => AdfConverter.CreatePlainTextAdf(body),
                _ => AdfConverter.ConvertMarkdownToAdf(body)
            };

            var payload = new Dictionary<string, object>
            {
                ["spaceId"] = spaceId,
                ["status"] = status,
                ["title"] = title,
                ["body"] = new { representation = "atlas_doc_format", value = JsonSerializer.Serialize(adfBody) }
            };
            if (!string.IsNullOrEmpty(parentId))
                payload["parentId"] = parentId;
            if (subtype == "live")
                payload["subtype"] = "live";

            using var client = AtlasClientFactory.CreateConfluenceClient();
            var result = await ApiHelper.PostAsync(client, "pages", payload, ct);
            if (result == null) return;

            OutputService.Print(new
            {
                Status = "created",
                Id = result.Value.GetString("id"),
                Title = result.Value.GetString("title"),
                SpaceId = result.Value.GetString("spaceId"),
                Subtype = result.Value.GetString("subtype"),
                Version = result.Value.GetString("version", "number")
            }, format);
        });
        return cmd;
    }

    private static Command BuildUpdate(Option<string> formatOption)
    {
        var idArg = new Argument<string>("id") { Description = "Page ID" };
        var titleOption = new Option<string?>("--title") { Description = "New page title" };
        var bodyOption = new Option<string?>("--body") { Description = "New page content (or use --body-file)" };
        var bodyFileOption = new Option<string?>("--body-file") { Description = "Read page content from file" };
        var bodyFormatOption = new Option<string>("--body-format") { Description = "Body format: plain, markdown, or adf (default: markdown)", DefaultValueFactory = _ => "markdown" };
        var messageOption = new Option<string?>("--message") { Description = "Version message" };
        var cmd = new Command("update", "Update a Confluence page") { idArg, titleOption, bodyOption, bodyFileOption, bodyFormatOption, messageOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var id = parseResult.GetValue(idArg)!;
            var title = parseResult.GetValue(titleOption);
            var bodyRaw = parseResult.GetValue(bodyOption);
            var bodyFile = parseResult.GetValue(bodyFileOption);
            var bodyFormat = parseResult.GetValue(bodyFormatOption)!;
            var message = parseResult.GetValue(messageOption);

            string? body;
            try { body = !string.IsNullOrEmpty(bodyRaw) || !string.IsNullOrEmpty(bodyFile) ? ResolveBody(bodyRaw, bodyFile) : null; }
            catch (Exception ex)
            {
                OutputService.PrintError("file", ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
            {
                OutputService.PrintError("validation", "At least one of --title, --body, or --body-file must be provided.");
                Environment.ExitCode = 1;
                return;
            }

            using var client = AtlasClientFactory.CreateConfluenceClient();

            // GET current page to read version number and current title
            var existing = await ApiHelper.GetAsync(client, $"pages/{Uri.EscapeDataString(id)}?body-format=atlas_doc_format", ct);
            if (existing == null) return;

            var p = existing.Value;
            var spaceId = p.GetString("spaceId");
            if (!string.IsNullOrEmpty(spaceId))
            {
                if (!AllowedSpacesService.CheckAndPrompt(spaceId, "write", "confluence")) { Environment.ExitCode = 1; return; }
            }

            var currentVersion = int.Parse(p.GetString("version", "number") ?? "0");
            var currentTitle = p.GetString("title") ?? "";
            var currentStatus = p.GetString("status") ?? "current";

            var version = new Dictionary<string, object> { ["number"] = currentVersion + 1 };
            if (!string.IsNullOrEmpty(message))
                version["message"] = message;

            var payload = new Dictionary<string, object>
            {
                ["id"] = id,
                ["status"] = currentStatus,
                ["title"] = title ?? currentTitle,
                ["version"] = version
            };

            if (!string.IsNullOrEmpty(body))
            {
                var adfBody = bodyFormat switch
                {
                    "adf" => AdfConverter.ParseRawAdf(body),
                    "plain" => AdfConverter.CreatePlainTextAdf(body),
                    _ => AdfConverter.ConvertMarkdownToAdf(body)
                };
                payload["body"] = new { representation = "atlas_doc_format", value = JsonSerializer.Serialize(adfBody) };
            }

            var result = await ApiHelper.PutAsync(client, $"pages/{Uri.EscapeDataString(id)}", payload, ct);
            if (result == null) return;

            OutputService.Print(new
            {
                Status = "updated",
                Id = result.Value.GetString("id"),
                Title = result.Value.GetString("title"),
                SpaceId = result.Value.GetString("spaceId"),
                Version = result.Value.GetString("version", "number")
            }, format);
        });
        return cmd;
    }
}
