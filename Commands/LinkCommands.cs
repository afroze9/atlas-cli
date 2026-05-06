using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class LinkCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("link", "Issue link operations (e.g. blocks, relates to, clones)");
        cmd.Subcommands.Add(BuildTypes(formatOption));
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildCreate(formatOption));
        cmd.Subcommands.Add(BuildDelete(formatOption));
        return cmd;
    }

    private static Command BuildTypes(Option<string> formatOption)
    {
        var cmd = new Command("types", "List available issue link types");
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            try
            {
                var result = await LinkService.ListTypesAsync(ct);
                OutputService.Print(result, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError(ex.GetType().Name, ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var keyOption = new Option<string>("--key") { Description = "Work item key (e.g. PROJ-123)", Required = true };
        var cmd = new Command("list", "List links on a work item") { keyOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var key = parseResult.GetValue(keyOption)!;
            try
            {
                var result = await LinkService.ListAsync(key, ct);
                OutputService.Print(result, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError(ex.GetType().Name, ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildCreate(Option<string> formatOption)
    {
        var fromOption = new Option<string>("--from") { Description = "Source issue key (the one performing the relationship, e.g. 'X' in 'X blocks Y')", Required = true };
        var toOption = new Option<string>("--to") { Description = "Target issue key (the one receiving the relationship, e.g. 'Y' in 'X blocks Y')", Required = true };
        var typeOption = new Option<string>("--type") { Description = "Link type name (e.g. Blocks, Relates, Cloners, Duplicate). Use 'link types' to list available types.", Required = true };
        var commentOption = new Option<string?>("--comment") { Description = "Optional comment to add when creating the link" };
        var cmd = new Command("create", "Create a link between two work items") { fromOption, toOption, typeOption, commentOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var from = parseResult.GetValue(fromOption)!;
            var to = parseResult.GetValue(toOption)!;
            var type = parseResult.GetValue(typeOption)!;
            var comment = parseResult.GetValue(commentOption);
            try
            {
                var result = await LinkService.CreateAsync(from, to, type, comment, ct);
                OutputService.Print(result, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError(ex.GetType().Name, ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildDelete(Option<string> formatOption)
    {
        var idOption = new Option<string>("--id") { Description = "Link ID (from 'link list')", Required = true };
        var cmd = new Command("delete", "Remove a link between two work items") { idOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var id = parseResult.GetValue(idOption)!;
            try
            {
                var result = await LinkService.DeleteAsync(id, ct);
                OutputService.Print(result, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError(ex.GetType().Name, ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }
}
