using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class ConfigCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("config", "Manage atlas-cli configuration");
        cmd.Subcommands.Add(BuildGet(formatOption));
        cmd.Subcommands.Add(BuildSet());
        return cmd;
    }

    private static Command BuildGet(Option<string> formatOption)
    {
        var cmd = new Command("get", "Show current configuration values");
        cmd.SetAction((parseResult, _) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var config = AuthService.GetStatus();
            if (config == null)
            {
                OutputService.PrintError("not_configured", "No configuration found. Run 'atlas-cli auth login' first.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            OutputService.Print(new
            {
                config.StoryPointsField
            }, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildSet()
    {
        var cmd = new Command("set", "Set a configuration value");
        cmd.Subcommands.Add(BuildSetStoryPointsField());
        return cmd;
    }

    private static Command BuildSetStoryPointsField()
    {
        var fieldArg = new Argument<string>("field-id") { Description = "Jira custom field ID for story points (e.g. customfield_10016)" };
        var cmd = new Command("story-points-field", "Set the Jira custom field ID used for story points") { fieldArg };
        cmd.SetAction((parseResult, _) =>
        {
            var fieldId = parseResult.GetValue(fieldArg)!;
            var config = AuthService.GetStatus();
            if (config == null)
            {
                OutputService.PrintError("not_configured", "No configuration found. Run 'atlas-cli auth login' first.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            config.StoryPointsField = fieldId;
            AuthService.SaveConfig(config);
            Console.WriteLine($"Story points field set to: {fieldId}");
            return Task.CompletedTask;
        });
        return cmd;
    }
}
