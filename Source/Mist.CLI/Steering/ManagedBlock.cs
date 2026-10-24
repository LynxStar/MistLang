namespace Mist.CLI.Steering;

public enum ManagedBlockOutcome
{
    Created,
    Appended,
    Replaced,
    Unchanged,
    Malformed,
}

public sealed record ManagedBlockMerge(ManagedBlockOutcome Outcome, string Text, string? Error);

public static class ManagedBlock
{
    public const string BeginMarker = "<!-- BEGIN MIST-MANAGED: written by `mist steering`; edits inside are overwritten -->";
    public const string EndMarker = "<!-- END MIST-MANAGED -->";

    private const string BeginPrefix = "<!-- BEGIN MIST-MANAGED";
    private const string EndPrefix = "<!-- END MIST-MANAGED";

    public static ManagedBlockMerge Merge(string? existing, string body)
    {
        var newline = existing is not null && existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var blockLines = Render(body, newline);

        if(existing is null)
        {
            return new(ManagedBlockOutcome.Created, string.Join(newline, blockLines) + newline, null);
        }

        var lines = existing.Split(newline);
        var begins = IndicesStartingWith(lines, BeginPrefix);
        var ends = IndicesStartingWith(lines, EndPrefix);

        if(begins.Count == 0 && ends.Count == 0)
        {
            var block = string.Join(newline, blockLines) + newline;
            var appended = string.IsNullOrWhiteSpace(existing) ? block : existing.TrimEnd() + newline + newline + block;
            return new(ManagedBlockOutcome.Appended, appended, null);
        }

        // A lone or repeated marker means someone edited the fence; guessing which lines are ours could delete theirs.
        if(begins.Count != 1 || ends.Count != 1 || begins[0] > ends[0])
        {
            return new(
                ManagedBlockOutcome.Malformed,
                existing,
                $"found {begins.Count} BEGIN and {ends.Count} END MIST-MANAGED markers; expected one of each, BEGIN first");
        }

        var merged = string.Join(newline, [.. lines[..begins[0]], .. blockLines, .. lines[(ends[0] + 1)..]]);
        var outcome = merged == existing ? ManagedBlockOutcome.Unchanged : ManagedBlockOutcome.Replaced;
        return new(outcome, merged, null);
    }

    public static bool ImportsAgentsMd(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token is "@AGENTS.md" or "@./AGENTS.md");
    }

    private static string[] Render(string body, string newline)
    {
        var bodyLines = body.Trim().ReplaceLineEndings(newline).Split(newline);
        return [BeginMarker, .. bodyLines, EndMarker];
    }

    private static List<int> IndicesStartingWith(string[] lines, string prefix)
    {
        var indices = new List<int>();
        for(var i = 0; i < lines.Length; i++)
        {
            if(lines[i].TrimStart().StartsWith(prefix, StringComparison.Ordinal))
            {
                indices.Add(i);
            }
        }

        return indices;
    }
}