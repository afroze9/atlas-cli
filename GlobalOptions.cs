using System.CommandLine;

namespace AtlasCli;

public static class GlobalOptions
{
    public static readonly Option<string> Format = new("--format")
    {
        Description = "Output format: json or table",
        DefaultValueFactory = _ => "json",
        Recursive = true
    };
}
