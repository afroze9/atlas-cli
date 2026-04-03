using System.Text.Json;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AtlasCli.Services;

public static class AdfConverter
{
    public static object CreatePlainTextAdf(string text)
    {
        return new
        {
            type = "doc",
            version = 1,
            content = new[]
            {
                new
                {
                    type = "paragraph",
                    content = new[]
                    {
                        new { type = "text", text }
                    }
                }
            }
        };
    }

    public static object ConvertMarkdownToAdf(string markdown)
    {
        var pipeline = new MarkdownPipelineBuilder().Build();
        var doc = Markdown.Parse(markdown, pipeline);

        var blocks = new List<object>();
        foreach (var block in doc)
        {
            var converted = ConvertBlock(block);
            if (converted != null)
                blocks.Add(converted);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "doc",
            ["version"] = 1,
            ["content"] = blocks
        };
    }

    private static object? ConvertBlock(Block block)
    {
        return block switch
        {
            HeadingBlock h => ConvertHeading(h),
            ParagraphBlock p => ConvertParagraph(p),
            ListBlock l => ConvertList(l),
            FencedCodeBlock fc => ConvertFencedCode(fc),
            CodeBlock c => ConvertCodeBlock(c),
            ThematicBreakBlock => new Dictionary<string, object> { ["type"] = "rule" },
            _ => ConvertFallbackBlock(block)
        };
    }

    private static object ConvertHeading(HeadingBlock heading)
    {
        var result = new Dictionary<string, object>
        {
            ["type"] = "heading",
            ["attrs"] = new { level = heading.Level }
        };

        if (heading.Inline != null)
        {
            var inlines = ConvertInlines(heading.Inline, []);
            if (inlines.Count > 0)
                result["content"] = inlines;
        }

        return result;
    }

    private static object? ConvertParagraph(ParagraphBlock paragraph)
    {
        if (paragraph.Inline == null) return null;

        var inlines = ConvertInlines(paragraph.Inline, []);
        if (inlines.Count == 0) return null;

        return new Dictionary<string, object>
        {
            ["type"] = "paragraph",
            ["content"] = inlines
        };
    }

    private static object ConvertList(ListBlock list)
    {
        var listType = list.IsOrdered ? "orderedList" : "bulletList";
        var items = new List<object>();

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
                items.Add(ConvertListItem(listItem));
        }

        return new Dictionary<string, object>
        {
            ["type"] = listType,
            ["content"] = items
        };
    }

    private static object ConvertListItem(ListItemBlock listItem)
    {
        var content = new List<object>();

        foreach (var block in listItem)
        {
            var converted = ConvertBlock(block);
            if (converted != null)
                content.Add(converted);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "listItem",
            ["content"] = content
        };
    }

    private static object ConvertFencedCode(FencedCodeBlock codeBlock)
    {
        var text = string.Join('\n', codeBlock.Lines.Lines
            .Take(codeBlock.Lines.Count)
            .Select(l => l.ToString()));
        // Trim trailing newline that Markdig adds
        text = text.TrimEnd('\n', '\r');

        var result = new Dictionary<string, object>
        {
            ["type"] = "codeBlock",
            ["content"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };

        var lang = codeBlock.Info;
        if (!string.IsNullOrEmpty(lang))
            result["attrs"] = new { language = lang };

        return result;
    }

    private static object ConvertCodeBlock(CodeBlock codeBlock)
    {
        var text = string.Join('\n', codeBlock.Lines.Lines
            .Take(codeBlock.Lines.Count)
            .Select(l => l.ToString()));
        text = text.TrimEnd('\n', '\r');

        return new Dictionary<string, object>
        {
            ["type"] = "codeBlock",
            ["content"] = new[]
            {
                new Dictionary<string, object>
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };
    }

    private static object? ConvertFallbackBlock(Block block)
    {
        // For unsupported block types, try to extract text
        if (block is LeafBlock leaf && leaf.Inline != null)
        {
            var inlines = ConvertInlines(leaf.Inline, []);
            if (inlines.Count > 0)
            {
                return new Dictionary<string, object>
                {
                    ["type"] = "paragraph",
                    ["content"] = inlines
                };
            }
        }
        return null;
    }

    private static List<object> ConvertInlines(ContainerInline container, List<Dictionary<string, string>> marks)
    {
        var result = new List<object>();

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    result.Add(CreateTextNode(literal.Content.ToString(), marks));
                    break;

                case EmphasisInline emphasis:
                    var newMarks = new List<Dictionary<string, string>>(marks);
                    var markType = emphasis.DelimiterCount >= 2 ? "strong" : "em";
                    newMarks.Add(new Dictionary<string, string> { ["type"] = markType });
                    result.AddRange(ConvertInlines(emphasis, newMarks));
                    break;

                case CodeInline code:
                    var codeMarks = new List<Dictionary<string, string>>(marks)
                    {
                        new() { ["type"] = "code" }
                    };
                    result.Add(CreateTextNode(code.Content, codeMarks));
                    break;

                case LineBreakInline:
                    result.Add(new Dictionary<string, object> { ["type"] = "hardBreak" });
                    break;

                case ContainerInline nestedContainer:
                    result.AddRange(ConvertInlines(nestedContainer, marks));
                    break;

                default:
                    // Extract text from unknown inline types
                    var text = inline.ToString();
                    if (!string.IsNullOrEmpty(text))
                        result.Add(CreateTextNode(text, marks));
                    break;
            }
        }

        return result;
    }

    private static object CreateTextNode(string text, List<Dictionary<string, string>> marks)
    {
        var node = new Dictionary<string, object>
        {
            ["type"] = "text",
            ["text"] = text
        };

        if (marks.Count > 0)
            node["marks"] = marks;

        return node;
    }

    // --- ADF to plain text / markdown ---

    public static string? ExtractPlainText(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined || fields.ValueKind == JsonValueKind.Null)
            return null;
        if (!fields.TryGetProperty("description", out var desc) || desc.ValueKind == JsonValueKind.Null)
            return null;
        if (!desc.TryGetProperty("content", out var content))
            return null;

        var texts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            var blockText = ExtractBlockText(block);
            if (!string.IsNullOrEmpty(blockText))
                texts.Add(blockText);
        }
        return texts.Count > 0 ? string.Join("\n", texts) : null;
    }

    public static object? ExtractRawAdf(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined || fields.ValueKind == JsonValueKind.Null)
            return null;
        if (!fields.TryGetProperty("description", out var desc) || desc.ValueKind == JsonValueKind.Null)
            return null;
        return desc;
    }

    public static string? ConvertAdfToMarkdown(JsonElement fields)
    {
        if (fields.ValueKind == JsonValueKind.Undefined || fields.ValueKind == JsonValueKind.Null)
            return null;
        if (!fields.TryGetProperty("description", out var desc) || desc.ValueKind == JsonValueKind.Null)
            return null;
        if (!desc.TryGetProperty("content", out var content))
            return null;

        var lines = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            var md = ConvertAdfBlockToMarkdown(block, "");
            if (md != null)
                lines.Add(md);
        }
        return lines.Count > 0 ? string.Join("\n\n", lines) : null;
    }

    private static string? ConvertAdfBlockToMarkdown(JsonElement block, string indent)
    {
        var type = block.GetString("type");
        return type switch
        {
            "heading" => ConvertAdfHeading(block),
            "paragraph" => ConvertAdfInlines(block),
            "bulletList" => ConvertAdfList(block, ordered: false, indent),
            "orderedList" => ConvertAdfList(block, ordered: true, indent),
            "codeBlock" => ConvertAdfCodeBlock(block),
            "rule" => "---",
            _ => ConvertAdfInlines(block) // fallback: extract text
        };
    }

    private static string? ConvertAdfHeading(JsonElement block)
    {
        var level = 1;
        if (block.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("level", out var lvl))
            level = lvl.GetInt32();

        var text = ConvertAdfInlines(block);
        if (string.IsNullOrEmpty(text)) return null;

        return $"{new string('#', level)} {text}";
    }

    private static string? ConvertAdfList(JsonElement block, bool ordered, string indent)
    {
        if (!block.TryGetProperty("content", out var items))
            return null;

        var lines = new List<string>();
        var index = 1;

        foreach (var item in items.EnumerateArray())
        {
            if (item.GetString("type") != "listItem") continue;
            if (!item.TryGetProperty("content", out var itemContent)) continue;

            foreach (var child in itemContent.EnumerateArray())
            {
                var childType = child.GetString("type");
                if (childType == "bulletList" || childType == "orderedList")
                {
                    var nested = ConvertAdfList(child, childType == "orderedList", indent + "  ");
                    if (nested != null)
                        lines.Add(nested);
                }
                else
                {
                    var text = ConvertAdfInlines(child);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var bullet = ordered ? $"{index}." : "-";
                        lines.Add($"{indent}{bullet} {text}");
                        index++;
                    }
                }
            }
        }

        return lines.Count > 0 ? string.Join("\n", lines) : null;
    }

    private static string? ConvertAdfCodeBlock(JsonElement block)
    {
        var lang = "";
        if (block.TryGetProperty("attrs", out var attrs) && attrs.TryGetProperty("language", out var l))
            lang = l.GetString() ?? "";

        var text = ConvertAdfInlines(block);
        return $"```{lang}\n{text}\n```";
    }

    private static string? ConvertAdfInlines(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
            return null;

        var parts = new List<string>();
        foreach (var inline in content.EnumerateArray())
        {
            var type = inline.GetString("type");
            if (type == "text")
            {
                var text = inline.GetString("text") ?? "";
                parts.Add(ApplyMarks(text, inline));
            }
            else if (type == "hardBreak")
            {
                parts.Add("  \n");
            }
            else
            {
                // Recurse for nested structures
                var nested = ConvertAdfInlines(inline);
                if (nested != null)
                    parts.Add(nested);
            }
        }

        return parts.Count > 0 ? string.Join("", parts) : null;
    }

    private static string ApplyMarks(string text, JsonElement inline)
    {
        if (!inline.TryGetProperty("marks", out var marks))
            return text;

        foreach (var mark in marks.EnumerateArray())
        {
            var markType = mark.GetString("type");
            text = markType switch
            {
                "strong" => $"**{text}**",
                "em" => $"*{text}*",
                "code" => $"`{text}`",
                _ => text
            };
        }

        return text;
    }

    private static string? ExtractBlockText(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
            return null;

        var texts = new List<string>();
        foreach (var inline in content.EnumerateArray())
        {
            var type = inline.GetString("type");
            if (type == "text")
            {
                var t = inline.GetString("text");
                if (t != null) texts.Add(t);
            }
            else
            {
                // Recurse for nested content (list items, etc.)
                var nested = ExtractBlockText(inline);
                if (nested != null) texts.Add(nested);
            }
        }
        return texts.Count > 0 ? string.Join("", texts) : null;
    }
}
