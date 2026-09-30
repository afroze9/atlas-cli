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

    public static bool IsBypassed => !AuthService.ArePermissionChecksEnabled();

    /// <summary>
    /// Checks if a space/project is allowed for the given action.
    /// Returns immediately without prompting so non-interactive callers cannot block.
    /// </summary>
    public static bool CheckAndPrompt(string identifier, string action, string type = "jira")
    {
        if (IsBypassed) return true;

        var list = Load();
        var space = list.FindSpace(identifier, type);

        if (space != null && space.AllowedActions.Contains(action, StringComparer.OrdinalIgnoreCase))
            return true;

        Console.Error.WriteLine(GetAccessDeniedMessage(identifier, action, type));
        return false;
    }

    public static UnauthorizedAccessException CreateAccessDeniedException(
        string identifier, string action, string type = "jira") =>
        new(GetAccessDeniedMessage(identifier, action, type));

    public static string GetAccessDeniedMessage(string identifier, string action, string type = "jira")
    {
        var resource = type.Equals("confluence", StringComparison.OrdinalIgnoreCase)
            ? "Confluence space"
            : "Jira project";

        return $"Access denied by atlas-cli: {resource} '{identifier}' is not allowed for action '{action}'.{Environment.NewLine}" +
            $"Grant access, then retry: atlas-cli permissions allow {identifier} --type {type.ToLowerInvariant()} --actions {action.ToLowerInvariant()}";
    }

    /// <summary>
    /// Extracts the project key from a work item key (e.g., "PROJ-123" -> "PROJ").
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
    public string Identifier { get; set; } = "";    // Jira project key (e.g. "PROJ") or Confluence space ID/key
    public string DisplayName { get; set; } = "";
    public string Type { get; set; } = "jira";       // "jira" or "confluence"
    public List<string> AllowedActions { get; set; } = []; // "read", "write", "delete"
}
