using System.CommandLine;
using AtlasCli;
using AtlasCli.Commands;

var rootCommand = new RootCommand("Atlassian CLI - interact with Jira and Confluence");
rootCommand.Options.Add(GlobalOptions.Format);

var jiraCommand = new Command("jira", "Jira Cloud operations");
jiraCommand.Subcommands.Add(WorkItemCommands.Build(GlobalOptions.Format));
jiraCommand.Subcommands.Add(ProjectCommands.Build(GlobalOptions.Format));

var confluenceCommand = new Command("confluence", "Confluence Cloud operations");
confluenceCommand.Subcommands.Add(SpaceCommands.Build(GlobalOptions.Format));
confluenceCommand.Subcommands.Add(PageCommands.Build(GlobalOptions.Format));

rootCommand.Subcommands.Add(AuthCommands.Build());
rootCommand.Subcommands.Add(jiraCommand);
rootCommand.Subcommands.Add(confluenceCommand);
rootCommand.Subcommands.Add(PermissionCommands.Build(GlobalOptions.Format));

return await rootCommand.Parse(args).InvokeAsync();
