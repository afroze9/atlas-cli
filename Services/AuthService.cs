using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AtlasCli.Services;

public class AuthService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atlas-cli");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>
    /// Returns an account key in the form "domain/email".
    /// </summary>
    public static string AccountKey(string domain, string email) => $"{domain}/{email}";

    public static void Login(string domain, string email, string apiToken)
    {
        ValidateDomain(domain);
        var store = LoadStore();
        var key = AccountKey(domain, email);

        if (store.Accounts.TryGetValue(key, out var existing))
        {
            // Update existing account
            existing.Domain = domain;
            existing.Email = email;
            existing.ApiToken = apiToken;
        }
        else
        {
            store.Accounts[key] = new AtlasConfig
            {
                Domain = domain,
                Email = email,
                ApiToken = apiToken
            };
        }

        store.ActiveAccount = key;
        SaveStore(store);
    }

    /// <summary>
    /// Validates that a domain string is safe for URL construction.
    /// Only allows alphanumeric characters and hyphens (standard subdomain format).
    /// </summary>
    public static void ValidateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Domain cannot be empty.");

        foreach (var c in domain)
        {
            if (!char.IsLetterOrDigit(c) && c != '-')
                throw new ArgumentException(
                    $"Invalid domain '{domain}'. Domain must contain only letters, digits, and hyphens (e.g. 'mycompany').");
        }
    }

    /// <summary>
    /// Returns the active account config, or null if not logged in.
    /// </summary>
    public static AtlasConfig? GetStatus()
    {
        var store = LoadStore();
        return GetActiveAccount(store);
    }

    /// <summary>
    /// Returns all accounts and which one is active.
    /// </summary>
    public static (Dictionary<string, AtlasConfig> Accounts, string? ActiveAccount) GetAllAccounts()
    {
        var store = LoadStore();
        return (store.Accounts, store.ActiveAccount);
    }

    /// <summary>
    /// Removes a specific account. If it was active, switches to the next available or clears active.
    /// Returns the key of the removed account, or null if not found.
    /// </summary>
    public static string? Logout(string? accountKey = null)
    {
        var store = LoadStore();

        if (store.Accounts.Count == 0)
            return null;

        // If no key specified, logout the active account
        var key = accountKey ?? store.ActiveAccount;
        if (key == null || !store.Accounts.ContainsKey(key))
            return null;

        store.Accounts.Remove(key);

        if (store.ActiveAccount == key)
        {
            // Switch to the first remaining account, or clear
            store.ActiveAccount = store.Accounts.Keys.FirstOrDefault();
        }

        if (store.Accounts.Count == 0)
        {
            // No accounts left, delete the config file
            if (File.Exists(ConfigPath))
                File.Delete(ConfigPath);
            return key;
        }

        SaveStore(store);
        return key;
    }

    /// <summary>
    /// Switches the active account. Returns true if successful.
    /// </summary>
    public static bool Switch(string accountKey)
    {
        var store = LoadStore();
        if (!store.Accounts.ContainsKey(accountKey))
            return false;

        store.ActiveAccount = accountKey;
        SaveStore(store);
        return true;
    }

    public static AtlasConfig LoadConfig()
    {
        var store = LoadStore();
        var config = GetActiveAccount(store);
        if (config != null) return config;

        var envDomain = Environment.GetEnvironmentVariable("ATLAS_CLI_DOMAIN");
        var envEmail = Environment.GetEnvironmentVariable("ATLAS_CLI_EMAIL");
        var envToken = Environment.GetEnvironmentVariable("ATLAS_CLI_API_TOKEN");

        if (!string.IsNullOrEmpty(envDomain) && !string.IsNullOrEmpty(envEmail) && !string.IsNullOrEmpty(envToken))
        {
            var envConfig = new AtlasConfig
            {
                Domain = envDomain,
                Email = envEmail,
                ApiToken = envToken
            };
            ApplyEnvOverrides(envConfig);
            return envConfig;
        }

        Console.Error.WriteLine("Not logged in. Run 'atlas-cli auth login' or set environment variables:");
        Console.Error.WriteLine("  ATLAS_CLI_DOMAIN, ATLAS_CLI_EMAIL, ATLAS_CLI_API_TOKEN");
        Environment.Exit(1);
        return null!; // unreachable
    }

    /// <summary>
    /// Stores Bitbucket-specific credentials on the active account.
    /// Mode: "basic" (email + scoped API token) or "bearer" (workspace/repo access token).
    /// </summary>
    public static void BitbucketLogin(string mode, string? email, string token, string? workspace)
    {
        var store = LoadStore();
        if (store.ActiveAccount == null || !store.Accounts.TryGetValue(store.ActiveAccount, out var config))
            throw new InvalidOperationException("No active Atlassian account. Run 'atlas-cli auth login' first.");

        // The store loaded above has encrypted tokens; preserve them by re-loading the active config via GetActiveAccount
        // semantics: write to the in-memory config, then SaveStore which re-encrypts.
        config.ApiToken = UnprotectToken(config.ApiToken);
        DecryptBitbucketTokens(config);

        config.BitbucketAuthMode = mode;
        config.BitbucketEmail = email ?? "";
        config.BitbucketToken = token;
        if (!string.IsNullOrEmpty(workspace))
            config.BitbucketWorkspace = workspace;

        SaveStore(store);
    }

    public static void BitbucketSetWorkspace(string workspace)
    {
        var store = LoadStore();
        if (store.ActiveAccount == null || !store.Accounts.TryGetValue(store.ActiveAccount, out var config))
            throw new InvalidOperationException("No active Atlassian account. Run 'atlas-cli auth login' first.");

        config.ApiToken = UnprotectToken(config.ApiToken);
        DecryptBitbucketTokens(config);

        config.BitbucketWorkspace = workspace;
        SaveStore(store);
    }

    public static void BitbucketLogout()
    {
        var store = LoadStore();
        if (store.ActiveAccount == null || !store.Accounts.TryGetValue(store.ActiveAccount, out var config))
            return;

        config.ApiToken = UnprotectToken(config.ApiToken);
        DecryptBitbucketTokens(config);
        config.BitbucketAuthMode = "";
        config.BitbucketEmail = "";
        config.BitbucketToken = "";
        config.BitbucketWorkspace = "";
        config.BitbucketRepoTokens = new();
        SaveStore(store);
    }

    public static string RepoTokenKey(string workspace, string repo) =>
        $"{workspace.ToLowerInvariant()}/{repo.ToLowerInvariant()}";

    public static void BitbucketAddRepoToken(string workspace, string repo, string token)
    {
        var store = LoadStore();
        if (store.ActiveAccount == null || !store.Accounts.TryGetValue(store.ActiveAccount, out var config))
            throw new InvalidOperationException("No active Atlassian account. Run 'atlas-cli auth login' first.");

        config.ApiToken = UnprotectToken(config.ApiToken);
        DecryptBitbucketTokens(config);

        config.BitbucketRepoTokens[RepoTokenKey(workspace, repo)] = token;
        SaveStore(store);
    }

    public static bool BitbucketRemoveRepoToken(string workspace, string repo)
    {
        var store = LoadStore();
        if (store.ActiveAccount == null || !store.Accounts.TryGetValue(store.ActiveAccount, out var config))
            return false;

        config.ApiToken = UnprotectToken(config.ApiToken);
        DecryptBitbucketTokens(config);

        var removed = config.BitbucketRepoTokens.Remove(RepoTokenKey(workspace, repo));
        if (removed) SaveStore(store);
        return removed;
    }

    public static IReadOnlyList<string> BitbucketListRepoTokenKeys()
    {
        var config = GetStatus();
        if (config == null) return Array.Empty<string>();
        return config.BitbucketRepoTokens.Keys.OrderBy(k => k).ToList();
    }

    private static void DecryptBitbucketTokens(AtlasConfig config)
    {
        if (!string.IsNullOrEmpty(config.BitbucketToken))
            config.BitbucketToken = UnprotectToken(config.BitbucketToken);
        if (config.BitbucketRepoTokens.Count > 0)
        {
            config.BitbucketRepoTokens = config.BitbucketRepoTokens.ToDictionary(
                kvp => kvp.Key,
                kvp => UnprotectToken(kvp.Value));
        }
    }

    public static void SaveConfig(AtlasConfig config)
    {
        var store = LoadStore();
        var key = AccountKey(config.Domain, config.Email);
        store.Accounts[key] = config;
        if (store.ActiveAccount == null)
            store.ActiveAccount = key;
        SaveStore(store);
    }

    private static AtlasConfig? GetActiveAccount(ConfigStore store)
    {
        if (store.ActiveAccount != null && store.Accounts.TryGetValue(store.ActiveAccount, out var config))
        {
            config.ApiToken = UnprotectToken(config.ApiToken);
            if (!string.IsNullOrEmpty(config.BitbucketToken))
                config.BitbucketToken = UnprotectToken(config.BitbucketToken);
            if (config.BitbucketRepoTokens.Count > 0)
            {
                config.BitbucketRepoTokens = config.BitbucketRepoTokens.ToDictionary(
                    kvp => kvp.Key,
                    kvp => UnprotectToken(kvp.Value));
            }
            ApplyEnvOverrides(config);
            return config;
        }
        return null;
    }

    private static ConfigStore LoadStore()
    {
        if (!File.Exists(ConfigPath))
            return new ConfigStore();

        var json = File.ReadAllText(ConfigPath);

        // Try loading as the new multi-account format first
        try
        {
            var store = JsonSerializer.Deserialize<ConfigStore>(json);
            if (store?.Accounts.Count > 0)
                return store;
        }
        catch { }

        // Fall back to legacy single-account format and migrate
        try
        {
            var legacy = JsonSerializer.Deserialize<AtlasConfig>(json);
            if (legacy != null && !string.IsNullOrEmpty(legacy.Domain) && !string.IsNullOrEmpty(legacy.Email))
            {
                var store = new ConfigStore();
                var key = AccountKey(legacy.Domain, legacy.Email);
                store.Accounts[key] = legacy;
                store.ActiveAccount = key;
                // Migrate on disk
                SaveStore(store);
                return store;
            }
        }
        catch { }

        return new ConfigStore();
    }

    private static void SaveStore(ConfigStore store)
    {
        Directory.CreateDirectory(ConfigDir);
        // Encrypt API tokens before writing
        var storeToSave = new ConfigStore
        {
            ActiveAccount = store.ActiveAccount,
            Accounts = store.Accounts.ToDictionary(
                kvp => kvp.Key,
                kvp => new AtlasConfig
                {
                    Domain = kvp.Value.Domain,
                    Email = kvp.Value.Email,
                    ApiToken = ProtectToken(kvp.Value.ApiToken),
                    StoryPointsField = kvp.Value.StoryPointsField,
                    StartDateField = kvp.Value.StartDateField,
                    BitbucketAuthMode = kvp.Value.BitbucketAuthMode,
                    BitbucketEmail = kvp.Value.BitbucketEmail,
                    BitbucketToken = string.IsNullOrEmpty(kvp.Value.BitbucketToken)
                        ? ""
                        : ProtectToken(kvp.Value.BitbucketToken),
                    BitbucketWorkspace = kvp.Value.BitbucketWorkspace,
                    BitbucketRepoTokens = kvp.Value.BitbucketRepoTokens.ToDictionary(
                        e => e.Key,
                        e => string.IsNullOrEmpty(e.Value) ? "" : ProtectToken(e.Value)),
                })
        };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(storeToSave, WriteOptions));
    }

    private static string ProtectToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.StartsWith("enc:"))
            return token;

        if (!OperatingSystem.IsWindows())
            return token; // DPAPI is Windows-only; other platforms keep plaintext for now

        var bytes = Encoding.UTF8.GetBytes(token);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return "enc:" + Convert.ToBase64String(encrypted);
    }

    private static string UnprotectToken(string token)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith("enc:"))
            return token; // plaintext (legacy or non-Windows)

        if (!OperatingSystem.IsWindows())
            return token;

        var encrypted = Convert.FromBase64String(token["enc:".Length..]);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static void ApplyEnvOverrides(AtlasConfig config)
    {
        var envSpField = Environment.GetEnvironmentVariable("ATLAS_CLI_STORY_POINTS_FIELD");
        if (!string.IsNullOrEmpty(envSpField))
            config.StoryPointsField = envSpField;

        var envSdField = Environment.GetEnvironmentVariable("ATLAS_CLI_START_DATE_FIELD");
        if (!string.IsNullOrEmpty(envSdField))
            config.StartDateField = envSdField;
    }
}

public class ConfigStore
{
    public string? ActiveAccount { get; set; }
    public Dictionary<string, AtlasConfig> Accounts { get; set; } = new();
}

public class AtlasConfig
{
    public string Domain { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string StoryPointsField { get; set; } = "customfield_10016";
    public string StartDateField { get; set; } = "customfield_13503";

    // Bitbucket-specific credentials (optional). When unset, Bitbucket falls back to the shared Atlassian creds above.
    // AuthMode: "basic" (email + scoped API token) or "bearer" (workspace/repo access token).
    public string BitbucketAuthMode { get; set; } = "";
    public string BitbucketEmail { get; set; } = "";
    public string BitbucketToken { get; set; } = "";
    public string BitbucketWorkspace { get; set; } = "";

    /// <summary>
    /// Optional per-repo Bitbucket access tokens, keyed by "workspace/repo".
    /// When making a request for a specific repo, this is preferred over BitbucketToken.
    /// Tokens are stored encrypted on disk on Windows (DPAPI), same as ApiToken/BitbucketToken.
    /// </summary>
    public Dictionary<string, string> BitbucketRepoTokens { get; set; } = new();
}
