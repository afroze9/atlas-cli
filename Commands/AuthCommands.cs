using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class AuthCommands
{
    public static Command Build()
    {
        var cmd = new Command("auth", "Authenticate to Atlassian Cloud");
        cmd.Subcommands.Add(BuildLogin());
        cmd.Subcommands.Add(BuildStatus());
        cmd.Subcommands.Add(BuildLogout());
        return cmd;
    }

    private static Command BuildLogin()
    {
        var domainOption = new Option<string>("--domain") { Description = "Atlassian domain (e.g. 'mycompany' for mycompany.atlassian.net)",  Required = true };
        var emailOption = new Option<string>("--email") { Description = "Atlassian account email",  Required = true };
        var tokenOption = new Option<string>("--token") { Description = "Atlassian API token",  Required = true };

        var cmd = new Command("login", "Save Atlassian credentials and verify access") { domainOption, emailOption, tokenOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var domain = parseResult.GetValue(domainOption)!;
            var email = parseResult.GetValue(emailOption)!;
            var token = parseResult.GetValue(tokenOption)!;

            // Validate credentials by calling /myself
            var config = new AtlasConfig { Domain = domain, Email = email, ApiToken = token };
            using var client = new HttpClient();
            client.BaseAddress = new Uri($"https://{domain}.atlassian.net/rest/api/3/");
            var credentials = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{email}:{token}"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

            try
            {
                var result = await ApiHelper.GetAsync(client, "myself", ct);
                if (result == null) return;

                AuthService.Login(domain, email, token);

                OutputService.Print(new
                {
                    Status = "logged_in",
                    Domain = domain,
                    Email = email,
                    DisplayName = result.Value.GetString("displayName"),
                    AccountId = result.Value.GetString("accountId")
                });
            }
            catch (Exception ex)
            {
                OutputService.PrintError("login_failed", ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildStatus()
    {
        var cmd = new Command("status", "Show current login status");
        cmd.SetAction((parseResult, ct) =>
        {
            var config = AuthService.GetStatus();
            if (config == null)
            {
                OutputService.Print(new { IsLoggedIn = false, Message = "Not logged in. Run 'atlas-cli auth login'." });
            }
            else
            {
                OutputService.Print(new
                {
                    IsLoggedIn = true,
                    config.Domain,
                    config.Email,
                    Url = $"https://{config.Domain}.atlassian.net"
                });
            }
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildLogout()
    {
        var cmd = new Command("logout", "Remove saved credentials");
        cmd.SetAction((parseResult, ct) =>
        {
            AuthService.Logout();
            OutputService.Print(new { Status = "logged_out" });
            return Task.CompletedTask;
        });
        return cmd;
    }
}
