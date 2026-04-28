using System.Text.Json;

namespace AtlasCli.Services;

public static class BitbucketService
{
    private static readonly string[] ValidSlugChars = ["-", "_", "."];

    private static void ValidateSlug(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName} cannot be empty.");
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && !ValidSlugChars.Contains(c.ToString()))
                throw new ArgumentException(
                    $"Invalid {fieldName} '{value}'. Only letters, digits, '-', '_' and '.' allowed.");
        }
    }

    public static async Task<object> WorkspaceListAsync(int limit = 25, CancellationToken ct = default)
    {
        using var client = AtlasClientFactory.CreateBitbucketClient();
        var data = await ApiHelper.GetOrThrowAsync(client, $"workspaces?pagelen={limit}", ct);
        return data.GetProperty("values").EnumerateArray().Select(w => new
        {
            Slug = w.GetString("slug"),
            Name = w.GetString("name"),
            Uuid = w.GetString("uuid"),
            Type = w.GetString("type"),
        }).ToList();
    }

    public static async Task<object> RepoListAsync(string workspace, int limit = 25, string? query = null, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        // RepoList is workspace-scoped (no specific repo); use account-level/default creds.
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace);
        var url = $"repositories/{Uri.EscapeDataString(workspace)}?pagelen={limit}&sort=-updated_on";
        if (!string.IsNullOrEmpty(query))
            url += $"&q={Uri.EscapeDataString($"name~\"{query}\"")}";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);
        return data.GetProperty("values").EnumerateArray().Select(r => new
        {
            Slug = r.GetString("slug"),
            Name = r.GetString("name"),
            Project = r.GetString("project", "key"),
            MainBranch = r.GetString("mainbranch", "name"),
            Private = r.GetString("is_private"),
            UpdatedOn = r.GetString("updated_on"),
        }).ToList();
    }

    public static async Task<object> RepoViewAsync(string workspace, string repo, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);
        var r = await ApiHelper.GetOrThrowAsync(client,
            $"repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(repo)}", ct);
        return new
        {
            Slug = r.GetString("slug"),
            FullName = r.GetString("full_name"),
            Name = r.GetString("name"),
            Description = r.GetString("description"),
            Project = r.GetString("project", "key"),
            MainBranch = r.GetString("mainbranch", "name"),
            Language = r.GetString("language"),
            Private = r.GetString("is_private"),
            Size = r.GetString("size"),
            CreatedOn = r.GetString("created_on"),
            UpdatedOn = r.GetString("updated_on"),
            Url = r.GetString("links", "html", "href"),
        };
    }

    public static async Task<object> PipelineListAsync(string workspace, string repo, string? branch = null,
        string? status = null, int limit = 25, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);

        var url = $"repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(repo)}/pipelines/?pagelen={limit}&sort=-created_on";
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(branch))
            filters.Add($"target.ref_name=\"{branch}\"");
        if (!string.IsNullOrEmpty(status))
            filters.Add($"state.name=\"{status.ToUpperInvariant()}\"");
        if (filters.Count > 0)
            url += $"&q={Uri.EscapeDataString(string.Join(" AND ", filters))}";

        var data = await ApiHelper.GetOrThrowAsync(client, url, ct);
        return data.GetProperty("values").EnumerateArray().Select(FormatPipelineSummary).ToList();
    }

    public static async Task<object> PipelineViewAsync(string workspace, string repo, string id, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);
        var p = await ApiHelper.GetOrThrowAsync(client, BuildPipelineUrl(workspace, repo, id), ct);
        return FormatPipelineDetail(p);
    }

    public static async Task<object> PipelineStepsAsync(string workspace, string repo, string id, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);
        var data = await ApiHelper.GetOrThrowAsync(client, $"{BuildPipelineUrl(workspace, repo, id)}/steps/", ct);
        return data.GetProperty("values").EnumerateArray().Select(FormatStep).ToList();
    }

    public static async Task<string> PipelineLogAsync(string workspace, string repo, string id,
        string? stepUuid = null, int? tail = null, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);

        var resolvedStep = stepUuid;
        if (string.IsNullOrEmpty(resolvedStep))
            resolvedStep = await ResolveLogTargetStep(client, workspace, repo, id, ct);

        if (string.IsNullOrEmpty(resolvedStep))
            return "(no steps available)";

        var url = $"{BuildPipelineUrl(workspace, repo, id)}/steps/{Uri.EscapeDataString(resolvedStep)}/log";
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw AtlasApiException.FromResponse(response.StatusCode, body);
        }
        var text = await response.Content.ReadAsStringAsync(ct);

        if (tail.HasValue && tail.Value > 0)
        {
            var lines = text.Split('\n');
            if (lines.Length > tail.Value)
                text = string.Join('\n', lines[^tail.Value..]);
        }
        return text;
    }

    public static async Task<object> PipelineStopAsync(string workspace, string repo, string id, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);
        var url = $"{BuildPipelineUrl(workspace, repo, id)}/stopPipeline";
        var response = await client.PostAsync(url, content: null, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw AtlasApiException.FromResponse(response.StatusCode, body);
        }
        return new { Status = "stop_requested", PipelineId = id };
    }

    public static async Task<object> PipelineRunAsync(string workspace, string repo, string? branch,
        string? commit, string? customName, CancellationToken ct = default)
    {
        ValidateSlug(workspace, "workspace");
        ValidateSlug(repo, "repo");
        if (string.IsNullOrEmpty(branch) && string.IsNullOrEmpty(commit))
            throw new ArgumentException("Specify --branch or --commit (or both).");

        using var client = AtlasClientFactory.CreateBitbucketClient(workspace, repo);

        object target;
        if (!string.IsNullOrEmpty(customName))
        {
            target = new
            {
                type = "pipeline_ref_target",
                ref_type = "branch",
                ref_name = branch ?? "",
                selector = new { type = "custom", pattern = customName }
            };
        }
        else if (!string.IsNullOrEmpty(commit) && !string.IsNullOrEmpty(branch))
        {
            target = new
            {
                type = "pipeline_commit_target",
                commit = new { type = "commit", hash = commit },
                selector = new { type = "branches", pattern = branch }
            };
        }
        else if (!string.IsNullOrEmpty(commit))
        {
            target = new { type = "pipeline_commit_target", commit = new { type = "commit", hash = commit } };
        }
        else
        {
            target = new { type = "pipeline_ref_target", ref_type = "branch", ref_name = branch! };
        }

        var data = await ApiHelper.PostOrThrowAsync(client,
            $"repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(repo)}/pipelines/",
            new { target }, ct);
        return FormatPipelineDetail(data);
    }

    private static string BuildPipelineUrl(string workspace, string repo, string id)
    {
        var idPart = id.StartsWith('{') && id.EndsWith('}') ? id : id;
        return $"repositories/{Uri.EscapeDataString(workspace)}/{Uri.EscapeDataString(repo)}/pipelines/{Uri.EscapeDataString(idPart)}";
    }

    private static async Task<string?> ResolveLogTargetStep(HttpClient client, string workspace, string repo, string id, CancellationToken ct)
    {
        var data = await ApiHelper.GetOrThrowAsync(client, $"{BuildPipelineUrl(workspace, repo, id)}/steps/", ct);
        var steps = data.GetProperty("values").EnumerateArray().ToList();
        if (steps.Count == 0) return null;

        var failed = steps.FirstOrDefault(s => s.GetString("state", "result", "name") == "FAILED");
        if (failed.ValueKind != JsonValueKind.Undefined)
            return failed.GetString("uuid");

        return steps[0].GetString("uuid");
    }

    private static object FormatPipelineSummary(JsonElement p) => new
    {
        BuildNumber = p.GetString("build_number"),
        Uuid = p.GetString("uuid"),
        State = p.GetString("state", "name"),
        Result = p.GetString("state", "result", "name") ?? p.GetString("state", "stage", "name"),
        Branch = p.GetString("target", "ref_name") ?? p.GetString("target", "branch"),
        Commit = TrimSha(p.GetString("target", "commit", "hash")),
        Trigger = p.GetString("trigger", "name"),
        Creator = p.GetString("creator", "display_name"),
        DurationSec = p.GetString("duration_in_seconds"),
        CreatedOn = p.GetString("created_on"),
    };

    private static object FormatPipelineDetail(JsonElement p) => new
    {
        BuildNumber = p.GetString("build_number"),
        Uuid = p.GetString("uuid"),
        State = p.GetString("state", "name"),
        Result = p.GetString("state", "result", "name") ?? p.GetString("state", "stage", "name"),
        Branch = p.GetString("target", "ref_name") ?? p.GetString("target", "branch"),
        Commit = p.GetString("target", "commit", "hash"),
        CommitMessage = p.GetString("target", "commit", "message"),
        Selector = p.GetString("target", "selector", "pattern"),
        Trigger = p.GetString("trigger", "name"),
        Creator = p.GetString("creator", "display_name"),
        CreatorEmail = p.GetString("creator", "nickname"),
        DurationSec = p.GetString("duration_in_seconds"),
        BuildSeconds = p.GetString("build_seconds_used"),
        CreatedOn = p.GetString("created_on"),
        CompletedOn = p.GetString("completed_on"),
        ErrorKey = p.GetString("state", "result", "error", "key"),
        ErrorMessage = p.GetString("state", "result", "error", "message"),
        Url = p.GetString("links", "self", "href"),
    };

    private static object FormatStep(JsonElement s) => new
    {
        Uuid = s.GetString("uuid"),
        Name = s.GetString("name"),
        State = s.GetString("state", "name"),
        Result = s.GetString("state", "result", "name") ?? s.GetString("state", "stage", "name"),
        DurationSec = s.GetString("duration_in_seconds"),
        StartedOn = s.GetString("started_on"),
        CompletedOn = s.GetString("completed_on"),
        ErrorKey = s.GetString("state", "result", "error", "key"),
        ErrorMessage = s.GetString("state", "result", "error", "message"),
    };

    private static string? TrimSha(string? sha) =>
        string.IsNullOrEmpty(sha) ? sha : sha.Length > 7 ? sha[..7] : sha;
}
