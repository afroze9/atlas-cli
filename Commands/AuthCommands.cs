using System.CommandLine;
using AtlasCli.Services;

namespace AtlasCli.Commands;

public static class AuthCommands
{
    public static Command Build(Option<string> formatOption)
    {
        var cmd = new Command("auth", "Authenticate to Atlassian Cloud");
        cmd.Subcommands.Add(BuildLogin(formatOption));
        cmd.Subcommands.Add(BuildStatus(formatOption));
        cmd.Subcommands.Add(BuildLogout(formatOption));
        cmd.Subcommands.Add(BuildSwitch(formatOption));
        cmd.Subcommands.Add(BuildList(formatOption));
        return cmd;
    }

    private static Command BuildLogin(Option<string> formatOption)
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
            var format = parseResult.GetValue(formatOption)!;

            // Validate domain before using in URL
            try { AuthService.ValidateDomain(domain); }
            catch (ArgumentException ex)
            {
                OutputService.PrintError("invalid_domain", ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            // Validate credentials by calling /myself
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
                    AccountId = result.Value.GetString("accountId"),
                    Active = true
                }, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError("login_failed", ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildStatus(Option<string> formatOption)
    {
        var cmd = new Command("status", "Show all logged-in accounts and which is active");
        cmd.SetAction((parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var (accounts, activeKey) = AuthService.GetAllAccounts();

            if (accounts.Count == 0)
            {
                OutputService.Print(new { IsLoggedIn = false, Message = "Not logged in. Run 'atlas-cli auth login'." }, format);
                return Task.CompletedTask;
            }

            var list = accounts.Select(kvp => new
            {
                Account = kvp.Key,
                kvp.Value.Domain,
                kvp.Value.Email,
                Url = $"https://{kvp.Value.Domain}.atlassian.net",
                Active = kvp.Key == activeKey
            }).ToArray();

            OutputService.Print(list, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildList(Option<string> formatOption)
    {
        var cmd = new Command("list", "List all logged-in accounts");
        cmd.SetAction((parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var (accounts, activeKey) = AuthService.GetAllAccounts();

            if (accounts.Count == 0)
            {
                OutputService.Print(new { Message = "No accounts found. Run 'atlas-cli auth login'." }, format);
                return Task.CompletedTask;
            }

            var list = accounts.Select(kvp => new
            {
                Account = kvp.Key,
                kvp.Value.Domain,
                kvp.Value.Email,
                Active = kvp.Key == activeKey
            }).ToArray();

            OutputService.Print(list, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildSwitch(Option<string> formatOption)
    {
        var accountArg = new Argument<string>("account") { Description = "Account to switch to (domain/email)" };
        var cmd = new Command("switch", "Switch active account") { accountArg };
        cmd.SetAction((parseResult, ct) =>
        {
            var account = parseResult.GetValue(accountArg)!;
            var format = parseResult.GetValue(formatOption)!;

            if (AuthService.Switch(account))
            {
                OutputService.Print(new { Status = "switched", ActiveAccount = account }, format);
            }
            else
            {
                var (accounts, _) = AuthService.GetAllAccounts();
                OutputService.PrintError("account_not_found",
                    $"Account '{account}' not found. Available accounts: {string.Join(", ", accounts.Keys)}");
                Environment.ExitCode = 1;
            }
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildLogout(Option<string> formatOption)
    {
        var accountArg = new Argument<string>("account") { Description = "Account to log out (domain/email). Omit to log out the active account.", Arity = ArgumentArity.ZeroOrOne };
        var cmd = new Command("logout", "Remove saved credentials for an account") { accountArg };
        cmd.SetAction((parseResult, ct) =>
        {
            var account = parseResult.GetValue(accountArg);
            var format = parseResult.GetValue(formatOption)!;

            var removed = AuthService.Logout(account);
            if (removed != null)
            {
                var (accounts, activeKey) = AuthService.GetAllAccounts();
                OutputService.Print(new
                {
                    Status = "logged_out",
                    RemovedAccount = removed,
                    RemainingAccounts = accounts.Count,
                    ActiveAccount = activeKey
                }, format);
            }
            else
            {
                OutputService.PrintError("not_logged_in", "No matching account found.");
                Environment.ExitCode = 1;
            }
            return Task.CompletedTask;
        });
        return cmd;
    }
}
