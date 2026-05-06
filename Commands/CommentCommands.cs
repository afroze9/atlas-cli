using System.CommandLine;
using System.Text.Json;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class CommentCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("comment", "Issue comment operations");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildCreate(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var keyOption = new Option<string>("--key") { Description = "Work item key (e.g. PROJ-123)",  Required = true };
        var cmd = new Command("list", "List comments on a work item") { keyOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyOption)!;

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "read")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var data = await ApiHelper.GetAsync(client, $"issue/{Uri.EscapeDataString(key)}/comment", ct);
            if (data == null) return;

            var comments = data.Value.GetProperty("comments").EnumerateArray().Select(c => new
            {
                Id = c.GetString("id"),
                Author = c.GetString("author", "displayName"),
                AuthorId = c.GetString("author", "accountId"),
                Created = c.GetString("created"),
                Updated = c.GetString("updated"),
                Body = ExtractPlainText(c)
            });

            OutputService.Print(comments, format);
        });
        return cmd;
    }

    private static Command BuildCreate(Option<string> formatOption)
    {
        var keyOption = new Option<string>("--key") { Description = "Work item key (e.g. PROJ-123)",  Required = true };
        var bodyOption = new Option<string>("--body") { Description = "Comment text",  Required = true };
        var bodyFormatOption = new Option<string>("--body-format") { Description = "Body format: plain, markdown, or adf", DefaultValueFactory = _ => "plain" };
        var mentionOption = new Option<string?>("--mention") { Description = "Comma-separated Atlassian account IDs to @-mention (prepended to body). Use 'jira user search' to find IDs." };
        var cmd = new Command("create", "Add a comment to a work item") { keyOption, bodyOption, bodyFormatOption, mentionOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyOption)!;
            var body = parseResult.GetValue(bodyOption)!;
            var bodyFormat = parseResult.GetValue(bodyFormatOption)!;
            var mentions = parseResult.GetValue(mentionOption);

            var projectKey = AllowedSpacesService.ExtractProjectKey(key);
            if (!AllowedSpacesService.CheckAndPrompt(projectKey, "write")) { Environment.ExitCode = 1; return; }

            using var client = AtlasClientFactory.CreateJiraClient();
            var adf = bodyFormat switch
            {
                "markdown" => AdfConverter.ConvertMarkdownToAdf(body),
                "adf" => AdfConverter.ParseRawAdf(body),
                _ => AdfConverter.CreatePlainTextAdf(body)
            };

            if (!string.IsNullOrEmpty(mentions))
            {
                var ids = mentions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                adf = AdfConverter.PrependMentions(adf, ids);
            }

            var payload = new { body = adf };

            var result = await ApiHelper.PostAsync(client, $"issue/{Uri.EscapeDataString(key)}/comment", payload, ct);
            if (result == null) return;

            OutputService.Print(new
            {
                Status = "created",
                Id = result.Value.GetString("id"),
                Key = key
            }, format);
        });
        return cmd;
    }

    private static string? ExtractPlainText(JsonElement comment)
    {
        if (!comment.TryGetProperty("body", out var body)) return null;
        if (!body.TryGetProperty("content", out var content)) return null;

        var texts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("content", out var inlineContent)) continue;
            foreach (var inline in inlineContent.EnumerateArray())
            {
                if (inline.TryGetProperty("text", out var text))
                    texts.Add(text.GetString() ?? "");
            }
        }
        return texts.Count > 0 ? string.Join(" ", texts) : null;
    }
}
