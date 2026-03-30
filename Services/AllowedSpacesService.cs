using System.Text.Json;
using System.Text.Json.Serialization;

namespace AtlasCli.Services;

public static class AllowedSpacesService
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".atlas-cli");
    private static readonly string SpacesPath = Path.Combine(ConfigDir, "allowed-spaces.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AllowedSpacesList Load()
    {
        if (!File.Exists(SpacesPath))
            return new AllowedSpacesList();

        var json = File.ReadAllText(SpacesPath);
        return JsonSerializer.Deserialize<AllowedSpacesList>(json, JsonOptions) ?? new AllowedSpacesList();
    }

    public static void Save(AllowedSpacesList list)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(SpacesPath, JsonSerializer.Serialize(list, JsonOptions));
    }

    /// <summary>
    /// Checks if a space/project is allowed for the given action.
    /// If not found or not allowed, prompts interactively.
    /// Returns true if allowed, false if denied.
    /// </summary>
    public static bool CheckAndPrompt(string identifier, string action, string type = "jira", bool interactive = true)
    {
        var list = Load();
        var space = list.FindSpace(identifier, type);

        if (space != null && space.AllowedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            return true;

        if (!interactive || Console.IsInputRedirected)
        {
            Console.Error.WriteLine($"{type} '{identifier}' is not allowed for action '{action}'. " +
                "Run 'atlas-cli permissions allow' to add it.");
            return false;
        }

        // Interactive prompt
        Console.Error.WriteLine();
        Console.Error.WriteLine($"  {type} '{identifier}' is not allowed for '{action}'.");
        Console.Error.WriteLine();
        Console.Error.Write($"  Allow {action} for '{identifier}'? [y/N/a] (y=yes once, a=allow and save): ");

        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "y")
            return true;

        if (response == "a")
        {
            if (space != null)
            {
                if (!space.AllowedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
                    space.AllowedActions.Add(action);
            }
            else
            {
                Console.Error.Write($"  Display name [{identifier}]: ");
                var name = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(name)) name = identifier;

                space = new AllowedSpace
                {
                    Identifier = identifier.ToUpperInvariant(),
                    DisplayName = name,
                    Type = type,
                    AllowedActions = [action]
                };
                list.Spaces.Add(space);
            }

            Save(list);
            Console.Error.WriteLine($"  Saved. '{identifier}' is now allowed for '{action}'.");
            return true;
        }

        Console.Error.WriteLine("  Denied.");
        return false;
    }

    /// <summary>
    /// Extracts the project key from a work item key (e.g., "TWM-123" -> "TWM").
    /// </summary>
    public static string ExtractProjectKey(string issueKey)
    {
        var dashIndex = issueKey.IndexOf('-');
        return dashIndex > 0 ? issueKey[..dashIndex].ToUpperInvariant() : issueKey.ToUpperInvariant();
    }
}

public class AllowedSpacesList
{
    public List<AllowedSpace> Spaces { get; set; } = [];

    public AllowedSpace? FindSpace(string identifier, string? type = null)
    {
        return Spaces.FirstOrDefault(s =>
            s.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)
            && (type == null || s.Type.Equals(type, StringComparison.OrdinalIgnoreCase)));
    }
}

public class AllowedSpace
{
    public string Identifier { get; set; } = "";    // Jira project key (e.g. "TWM") or Confluence space ID/key
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "jira";       // "jira" or "confluence"
    public List<string> AllowedActions { get; set; } = []; // "read", "write", "delete"
}
