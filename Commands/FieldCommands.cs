using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class FieldCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("field", "Discover Jira fields and context-specific field metadata");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildCreateMetadata(formatOption));
        cmd.Subcommands.Add(BuildEditMetadata(formatOption));
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var queryOption = new Option<string?>("--query") { Description = "Filter by field name or description" };
        var typeOption = new Option<string>("--type")
        {
            Description = "Field type: all, custom, or system",
            DefaultValueFactory = _ => "all"
        };
        var cmd = new Command("list", "List Jira system and custom fields") { queryOption, typeOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var type = parseResult.GetValue(typeOption)!.ToLowerInvariant();
            if (type is not ("all" or "custom" or "system"))
            {
                OutputService.PrintError("validation", "--type must be 'all', 'custom', or 'system'.");
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                var fields = await JiraFieldService.ListAsync(parseResult.GetValue(queryOption), type, ct);
                OutputService.Print(fields, parseResult.GetValue(formatOption)!);
            }
            catch (Exception ex) { PrintError(ex); }
        });
        return cmd;
    }

    private static Command BuildCreateMetadata(Option<string> formatOption)
    {
        var projectOption = new Option<string>("--project") { Description = "Jira project key", Required = true };
        var issueTypeOption = new Option<string>("--issue-type") { Description = "Issue type name or ID", Required = true };
        var cmd = new Command("create-meta", "List fields available when creating an issue")
        {
            projectOption,
            issueTypeOption
        };
        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var metadata = await JiraFieldService.GetCreateMetadataAsync(
                    parseResult.GetValue(projectOption)!, parseResult.GetValue(issueTypeOption)!, ct);
                OutputService.Print(metadata, parseResult.GetValue(formatOption)!);
            }
            catch (Exception ex) { PrintError(ex); }
        });
        return cmd;
    }

    private static Command BuildEditMetadata(Option<string> formatOption)
    {
        var keyArg = new Argument<string>("key") { Description = "Work item key (e.g. PROJ-123)" };
        var cmd = new Command("edit-meta", "List fields editable on a work item") { keyArg };
        cmd.SetAction(async (parseResult, ct) =>
        {
            try
            {
                var metadata = await JiraFieldService.GetEditMetadataAsync(parseResult.GetValue(keyArg)!, ct);
                OutputService.Print(metadata, parseResult.GetValue(formatOption)!);
            }
            catch (Exception ex) { PrintError(ex); }
        });
        return cmd;
    }

    private static void PrintError(Exception ex)
    {
        if (ex is AtlasApiException apiException)
            OutputService.PrintError(((int)apiException.StatusCode).ToString(), apiException.Message);
        else
            OutputService.PrintError("field_metadata", ex.Message);
        Environment.ExitCode = 1;
    }
}
