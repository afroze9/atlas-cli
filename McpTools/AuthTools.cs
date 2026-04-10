using System.ComponentModel;
using AtlasCli.Services;
using ModelContextProtocol.Server;

namespace AtlasCli.McpTools;

[McpServerToolType]
public static class AuthTools
{
    [McpServerTool(Name = "auth_status"), Description("Show current authentication status and active Atlassian account")]
    public static Task<string> Status()
    {
        try
        {
            var (accounts, activeKey) = AuthService.GetAllAccounts();
            if (accounts.Count == 0)
                return Task.FromResult(McpAtlasHelper.ToJson(new { isLoggedIn = false, message = "Not logged in." }));

            var list = accounts.Select(kvp => new
            {
                Account = kvp.Key,
                kvp.Value.Domain,
                kvp.Value.Email,
                Url = $"https://{kvp.Value.Domain}.atlassian.net",
                Active = kvp.Key == activeKey
            }).ToArray();

            return Task.FromResult(McpAtlasHelper.ToJson(list));
        }
        catch (Exception ex) { return Task.FromResult(McpAtlasHelper.HandleException(ex)); }
    }

    [McpServerTool(Name = "auth_switch"), Description("Switch the active Atlassian account")]
    public static Task<string> Switch(
        [Description("Account key to switch to (domain/email)")] string account)
    {
        try
        {
            if (AuthService.Switch(account))
                return Task.FromResult(McpAtlasHelper.ToJson(new { status = "switched", activeAccount = account }));

            var (accounts, _) = AuthService.GetAllAccounts();
            return Task.FromResult(McpAtlasHelper.Error("account_not_found",
                $"Account '{account}' not found. Available: {string.Join(", ", accounts.Keys)}"));
        }
        catch (Exception ex) { return Task.FromResult(McpAtlasHelper.HandleException(ex)); }
    }
}
