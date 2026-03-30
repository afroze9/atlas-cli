using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class PermissionCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("permissions", "Manage allowed spaces and projects");
        cmd.Subcommands.Add(BuildList(formatOption));
        cmd.Subcommands.Add(BuildAllow());
        cmd.Subcommands.Add(BuildRemove());
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var typeOption = new Option<string?>("--type") { Description = "Filter by type: jira or confluence" };
        var cmd = new Command("list", "List allowed spaces and projects") { typeOption };
        cmd.SetAction((parseResult, _) =>
        {
            var format = parseResult.GetValue(formatOption) ?? "json";
            var type = parseResult.GetValue(typeOption);
            var list = AllowedSpacesService.Load();

            var spaces = list.Spaces.AsEnumerable();
            if (!string.IsNullOrEmpty(type))
                spaces = spaces.Where(s => s.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

            var results = spaces.Select(s => new
            {
                s.Identifier,
                s.DisplayName,
                s.Type,
                AllowedActions = string.Join(", ", s.AllowedActions)
            }).ToList();

            OutputService.Print(results, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildAllow()
    {
        var identifierArg = new Argument<string>("identifier") { Description = "Jira project key (e.g. TWM) or Confluence space key/ID" };
        var nameOption = new Option<string?>("--name") { Description = "Display name" };
        var typeOption = new Option<string>("--type") { DefaultValueFactory = _ => "jira", Description = "Type: jira or confluence" };
        var actionsOption = new Option<string>("--actions") { Description = "Comma-separated allowed actions: read, write, delete", Required = true };
        var cmd = new Command("allow", "Add or update an allowed space/project") { identifierArg, nameOption, typeOption, actionsOption };
        cmd.SetAction((parseResult, _) =>
        {
            var identifier = parseResult.GetValue(identifierArg)!.ToUpperInvariant();
            var name = parseResult.GetValue(nameOption);
            var type = parseResult.GetValue(typeOption)!;
            var actions = parseResult.GetValue(actionsOption)!
                .Split(',')
                .Select(a => a.Trim().ToLowerInvariant())
                .Where(a => !string.IsNullOrEmpty(a))
                .ToList();

            var list = AllowedSpacesService.Load();
            var existing = list.FindSpace(identifier, type);

            if (existing != null)
            {
                if (!string.IsNullOrEmpty(name)) existing.DisplayName = name;
                existing.AllowedActions = actions;
            }
            else
            {
                list.Spaces.Add(new AllowedSpace
                {
                    Identifier = identifier,
                    DisplayName = name ?? identifier,
                    Type = type,
                    AllowedActions = actions
                });
            }

            AllowedSpacesService.Save(list);
            OutputService.Print(new { status = "allowed", identifier, type, actions = string.Join(", ", actions) });
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildRemove()
    {
        var identifierArg = new Argument<string>("identifier") { Description = "Jira project key or Confluence space key/ID to remove" };
        var typeOption = new Option<string>("--type") { DefaultValueFactory = _ => "jira", Description = "Type: jira or confluence" };
        var cmd = new Command("remove", "Remove a space/project from allowed list") { identifierArg, typeOption };
        cmd.SetAction((parseResult, _) =>
        {
            var identifier = parseResult.GetValue(identifierArg)!;
            var type = parseResult.GetValue(typeOption)!;
            var list = AllowedSpacesService.Load();
            var space = list.FindSpace(identifier, type);

            if (space == null)
            {
                OutputService.PrintError("not_found", $"'{identifier}' not found in allowed list.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            list.Spaces.Remove(space);
            AllowedSpacesService.Save(list);
            OutputService.Print(new { status = "removed", identifier });
            return Task.CompletedTask;
        });
        return cmd;
    }
}
