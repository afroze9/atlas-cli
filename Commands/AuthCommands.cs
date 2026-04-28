using System.CommandLine;
using System.Net.Http.Json;
using System.Text.Json;
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
        cmd.Subcommands.Add(BuildBitbucketLogin(formatOption));
        cmd.Subcommands.Add(BuildBitbucketLogout(formatOption));
        cmd.Subcommands.Add(BuildBitbucketSetWorkspace(formatOption));
        cmd.Subcommands.Add(BuildBitbucketAddRepoToken(formatOption));
        cmd.Subcommands.Add(BuildBitbucketRemoveRepoToken(formatOption));
        cmd.Subcommands.Add(BuildBitbucketListTokens(formatOption));
        return cmd;
    }

    private static Command BuildBitbucketAddRepoToken(Option<string> formatOption)
    {
        var workspaceOption = new Option<string>("--workspace") { Description = "Workspace slug", Required = true };
        var repoOption = new Option<string>("--repo") { Description = "Repository slug", Required = true };
        var tokenOption = new Option<string>("--token") { Description = "Repository access token (Bearer)", Required = true };

        var cmd = new Command("bitbucket-add-repo-token", "Add a repository-scoped Bitbucket access token (used in preference to the account-level token for that repo)") { workspaceOption, repoOption, tokenOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var workspace = parseResult.GetValue(workspaceOption)!;
            var repo = parseResult.GetValue(repoOption)!;
            var token = parseResult.GetValue(tokenOption)!;
            var format = parseResult.GetValue(formatOption)!;

            // Validate by hitting the repo endpoint with this token
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.bitbucket.org/2.0/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            try
            {
                var probe = await client.GetAsync($"repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(repo)}", ct);
                if (!probe.IsSuccessStatusCode)
                {
                    var body = await probe.Content.ReadAsStringAsync(ct);
                    throw AtlasApiException.FromResponse(probe.StatusCode, body);
                }

                AuthService.BitbucketAddRepoToken(workspace, repo, token);
                OutputService.Print(new
                {
                    Status = "repo_token_added",
                    Workspace = workspace,
                    Repo = repo,
                    Key = AuthService.RepoTokenKey(workspace, repo),
                }, format);
            }
            catch (AtlasApiException ex)
            {
                OutputService.PrintError(((int)ex.StatusCode).ToString(), ex.Message);
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                OutputService.PrintError("add_failed", ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildBitbucketRemoveRepoToken(Option<string> formatOption)
    {
        var workspaceOption = new Option<string>("--workspace") { Description = "Workspace slug", Required = true };
        var repoOption = new Option<string>("--repo") { Description = "Repository slug", Required = true };

        var cmd = new Command("bitbucket-remove-repo-token", "Remove a repository-scoped Bitbucket access token") { workspaceOption, repoOption };
        cmd.SetAction((parseResult, ct) =>
        {
            var workspace = parseResult.GetValue(workspaceOption)!;
            var repo = parseResult.GetValue(repoOption)!;
            var format = parseResult.GetValue(formatOption)!;

            var removed = AuthService.BitbucketRemoveRepoToken(workspace, repo);
            if (!removed)
            {
                OutputService.PrintError("not_found", $"No repo token stored for {workspace}/{repo}.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }
            OutputService.Print(new { Status = "repo_token_removed", Workspace = workspace, Repo = repo }, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildBitbucketListTokens(Option<string> formatOption)
    {
        var cmd = new Command("bitbucket-list-tokens", "Show stored Bitbucket credentials and per-repo tokens for the active account");
        cmd.SetAction((parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var config = AuthService.GetStatus();
            if (config == null)
            {
                OutputService.PrintError("not_logged_in", "Not logged in.");
                Environment.ExitCode = 1;
                return Task.CompletedTask;
            }

            OutputService.Print(new
            {
                AccountKeyConfigured = !string.IsNullOrEmpty(config.BitbucketToken),
                AccountAuthMode = string.IsNullOrEmpty(config.BitbucketAuthMode) ? "(falls back to Atlassian)" : config.BitbucketAuthMode,
                DefaultWorkspace = string.IsNullOrEmpty(config.BitbucketWorkspace) ? null : config.BitbucketWorkspace,
                RepoTokens = config.BitbucketRepoTokens.Keys.OrderBy(k => k).ToList(),
            }, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildBitbucketLogin(Option<string> formatOption)
    {
        var modeOption = new Option<string>("--mode") { Description = "Auth mode: 'basic' (email + scoped API token) or 'bearer' (workspace/repo access token)", DefaultValueFactory = _ => "bearer" };
        var emailOption = new Option<string?>("--email") { Description = "Atlassian email (required for --mode basic)" };
        var tokenOption = new Option<string>("--token") { Description = "Bitbucket access token or scoped API token", Required = true };
        var workspaceOption = new Option<string?>("--workspace") { Description = "Default workspace slug (optional)" };

        var cmd = new Command("bitbucket-login", "Save Bitbucket-scoped credentials on the active account") { modeOption, emailOption, tokenOption, workspaceOption };
        cmd.SetAction(async (parseResult, ct) =>
        {
            var mode = (parseResult.GetValue(modeOption) ?? "bearer").ToLowerInvariant();
            var email = parseResult.GetValue(emailOption);
            var token = parseResult.GetValue(tokenOption)!;
            var workspace = parseResult.GetValue(workspaceOption);
            var format = parseResult.GetValue(formatOption)!;

            if (mode != "basic" && mode != "bearer")
            {
                OutputService.PrintError("invalid_mode", "--mode must be 'basic' or 'bearer'.");
                Environment.ExitCode = 1;
                return;
            }
            if (mode == "basic" && string.IsNullOrEmpty(email))
            {
                OutputService.PrintError("missing_email", "--email is required when --mode=basic.");
                Environment.ExitCode = 1;
                return;
            }

            // Validate by hitting /user
            using var client = new HttpClient();
            client.BaseAddress = new Uri("https://api.bitbucket.org/2.0/");
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            if (mode == "bearer")
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                var creds = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{email}:{token}"));
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
            }

            try
            {
                // Workspace access tokens often do not have access to /user; try /user first, fall back to /workspaces.
                var probe = await client.GetAsync("user", ct);
                JsonElement? me = null;
                if (probe.IsSuccessStatusCode)
                {
                    me = await probe.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                }
                else if (probe.StatusCode != System.Net.HttpStatusCode.Forbidden && probe.StatusCode != System.Net.HttpStatusCode.Unauthorized)
                {
                    var body = await probe.Content.ReadAsStringAsync(ct);
                    throw AtlasApiException.FromResponse(probe.StatusCode, body);
                }
                else
                {
                    // 401/403: token may be workspace/repo-scoped. Validate against /workspaces or the configured workspace.
                    var url = string.IsNullOrEmpty(workspace) ? "workspaces?pagelen=1" : $"workspaces/{Uri.EscapeDataString(workspace)}";
                    var resp = await client.GetAsync(url, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync(ct);
                        throw AtlasApiException.FromResponse(resp.StatusCode, body);
                    }
                }

                AuthService.BitbucketLogin(mode, email, token, workspace);

                OutputService.Print(new
                {
                    Status = "bitbucket_logged_in",
                    Mode = mode,
                    Email = email,
                    Workspace = workspace,
                    DisplayName = me?.GetString("display_name"),
                    AccountId = me?.GetString("account_id"),
                }, format);
            }
            catch (AtlasApiException ex)
            {
                OutputService.PrintError(((int)ex.StatusCode).ToString(), ex.Message);
                Environment.ExitCode = 1;
            }
            catch (Exception ex)
            {
                OutputService.PrintError("login_failed", ex.Message);
                Environment.ExitCode = 1;
            }
        });
        return cmd;
    }

    private static Command BuildBitbucketLogout(Option<string> formatOption)
    {
        var cmd = new Command("bitbucket-logout", "Remove Bitbucket credentials from the active account");
        cmd.SetAction((parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            AuthService.BitbucketLogout();
            OutputService.Print(new { Status = "bitbucket_logged_out" }, format);
            return Task.CompletedTask;
        });
        return cmd;
    }

    private static Command BuildBitbucketSetWorkspace(Option<string> formatOption)
    {
        var workspaceArg = new Argument<string>("workspace") { Description = "Default Bitbucket workspace slug" };
        var cmd = new Command("bitbucket-set-workspace", "Set the default Bitbucket workspace for the active account") { workspaceArg };
        cmd.SetAction((parseResult, ct) =>
        {
            var format = parseResult.GetValue(formatOption)!;
            var ws = parseResult.GetValue(workspaceArg)!;
            try
            {
                AuthService.BitbucketSetWorkspace(ws);
                OutputService.Print(new { Status = "workspace_set", Workspace = ws }, format);
            }
            catch (Exception ex)
            {
                OutputService.PrintError("set_failed", ex.Message);
                Environment.ExitCode = 1;
            }
            return Task.CompletedTask;
        });
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
