namespace AtlasCli.Services;

public static class PermissionService
{
    public static object List(string? type = null)
    {
        var list = AllowedSpacesService.Load();
        var spaces = list.Spaces.AsEnumerable();
        if (!string.IsNullOrEmpty(type))
            spaces = spaces.Where(s => s.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

        return spaces.Select(s => new
        {
            s.Identifier,
            s.DisplayName,
            s.Type,
            AllowedActions = string.Join(", ", s.AllowedActions)
        }).ToList();
    }

    public static object Allow(string identifier, string actions, string? name = null, string type = "jira")
    {
        identifier = identifier.ToUpperInvariant();
        var actionList = actions.Split(',').Select(a => a.Trim().ToLowerInvariant()).Where(a => !string.IsNullOrEmpty(a)).ToList();

        var list = AllowedSpacesService.Load();
        var existing = list.FindSpace(identifier, type);

        if (existing != null)
        {
            if (!string.IsNullOrEmpty(name)) existing.DisplayName = name;
            existing.AllowedActions = actionList;
        }
        else
        {
            list.Spaces.Add(new AllowedSpace
            {
                Identifier = identifier,
                DisplayName = name ?? identifier,
                Type = type,
                AllowedActions = actionList
            });
        }

        AllowedSpacesService.Save(list);
        return new { status = "allowed", identifier, type, actions = string.Join(", ", actionList) };
    }

    public static object Remove(string identifier, string type = "jira")
    {
        var list = AllowedSpacesService.Load();
        var space = list.FindSpace(identifier, type);

        if (space == null)
            throw new InvalidOperationException($"'{identifier}' not found in allowed list.");

        list.Spaces.Remove(space);
        AllowedSpacesService.Save(list);
        return new { status = "removed", identifier };
    }
}
