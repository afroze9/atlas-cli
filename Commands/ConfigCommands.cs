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
            OutputService.Print(new
            {
                PermissionChecksEnabled = AuthService.ArePermissionChecksEnabled(),
                StoryPointsField = config?.StoryPointsField,
                StartDateField = config?.StartDateField
            }, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildSet()
    {
        var cmd = new Command("set", "Set a configuration value");
        cmd.Subcommands.Add(BuildSetPermissionChecks());
        cmd.Subcommands.Add(BuildSetStoryPointsField());
        cmd.Subcommands.Add(BuildSetStartDateField());
        return cmd;
    }

    private static Command BuildSetPermissionChecks()
    {
        var stateArg = new Argument<string>("state") { Description = "enabled or disabled" };
        var cmd = new Command("permission-checks", "Enable or disable all Jira and Confluence permission checks") { stateArg };
        cmd.SetAction((parseResult, _) =>
        {
            var state = parseResult.GetValue(stateArg)!;
            var enabled = state.ToLowerInvariant() switch
            {
                "enabled" or "enable" or "true" or "on" => true,
                "disabled" or "disable" or "false" or "off" => false,
                _ => (bool?)null
            };

            if (enabled == null)
            {
                OutputService.PrintError("invalid_value", "State must be 'enabled' or 'disabled'.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            AuthService.SetPermissionChecksEnabled(enabled.Value);
            OutputService.Print(new { permissionChecksEnabled = enabled.Value });
            return Task.CompletedTask;
        });
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

    private static Command BuildSetStartDateField()
    {
        var fieldArg = new Argument<string>("field-id") { Description = "Jira custom field ID for start date (e.g. customfield_13503)" };
        var cmd = new Command("start-date-field", "Set the Jira custom field ID used for start date in team-managed projects") { fieldArg };
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

            config.StartDateField = fieldId;
            AuthService.SaveConfig(config);
            Console.WriteLine($"Start date field set to: {fieldId}");
            return Task.CompletedTask;
        });
        return cmd;
    }
}
