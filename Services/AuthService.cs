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
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(store, WriteOptions));
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
}
