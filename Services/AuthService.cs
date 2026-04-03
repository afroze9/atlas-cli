using System.Text.Json;

namespace AtlasCli.Services;

public class AuthService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atlas-cli");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static void Login(string domain, string email, string apiToken)
    {
        var config = new AtlasConfig
        {
            Domain = domain,
            Email = email,
            ApiToken = apiToken
        };

        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, WriteOptions));
    }

    public static AtlasConfig? GetStatus()
    {
        return LoadFromFile();
    }

    public static void Logout()
    {
        if (File.Exists(ConfigPath))
            File.Delete(ConfigPath);
    }

    public static AtlasConfig LoadConfig()
    {
        var config = LoadFromFile();
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
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, WriteOptions));
    }

    private static AtlasConfig? LoadFromFile()
    {
        if (!File.Exists(ConfigPath)) return null;
        var json = File.ReadAllText(ConfigPath);
        var config = JsonSerializer.Deserialize<AtlasConfig>(json);
        if (config != null)
            ApplyEnvOverrides(config);
        return config;
    }

    private static void ApplyEnvOverrides(AtlasConfig config)
    {
        var envSpField = Environment.GetEnvironmentVariable("ATLAS_CLI_STORY_POINTS_FIELD");
        if (!string.IsNullOrEmpty(envSpField))
            config.StoryPointsField = envSpField;
    }
}

public class AtlasConfig
{
    public string Domain { get; set; } = "";
    public string Email { get; set; } = "";
    public string ApiToken { get; set; } = "";
    public string StoryPointsField { get; set; } = "customfield_10016";
}
