using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class UserCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("user", "Jira user operations");
        cmd.Subcommands.Add(BuildSearch(formatOption));
        return cmd;
    }

    private static Command BuildSearch(Option<string> formatOption)
    {
        var queryOption = new Option<string>("--query") { Description = "Display name or email to search for", Required = true };
        var limitOption = new Option<int>("--limit") { Description = "Max results", DefaultValueFactory = _ => 50 };
        var cmd = new Command("search", "Search Jira users by display name or email") { queryOption, limitOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var query = parseResult.GetValue(queryOption)!;
            var limit = parseResult.GetValue(limitOption);
            try
            {
                var result = await UserService.SearchAsync(query, limit, ct);
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
