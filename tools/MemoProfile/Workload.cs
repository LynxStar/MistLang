using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace MemoProfile;

internal sealed record TokenData(
    string Path,
    string Contents,
    int[] Kind,
    int[] Start,
    int[] Length,
    int[] StartLine,
    int[] StartColumn,
    int[] EndLine,
    int[] EndColumn,
    bool[] Hidden,
    string[] Lexeme);

internal sealed record WorkloadInput(string Id, string Kind, string SourcePath, string Sha256, int Bytes, int LexDiagnostics, TokenData Tokens);

internal static class Workload
{
    public const int InvalidStride = 8;

    private static readonly MethodInfo Reclassify = typeof(Mist.Parsing.MistParser)
        .GetMethod("ReclassifyKeywordsInQualifiedNames", BindingFlags.NonPublic | BindingFlags.Static)!;

    public static IReadOnlyList<WorkloadInput> Load(string repoRoot)
    {
        var valid = EnumerateSources(repoRoot, "samples")
            .Concat(EnumerateSources(repoRoot, Path.Combine("Source", "Standard")))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var inputs = new List<WorkloadInput>();
        for(var i = 0; i < valid.Count; i++)
        {
            var relative = valid[i];
            var text = File.ReadAllText(Path.Combine(repoRoot, relative));
            inputs.Add(Make(relative, "valid", relative, text));
            if(i % InvalidStride == 0)
            {
                inputs.Add(Make(relative + "#truncate-half", "invalid-truncate-half", relative, text[..(text.Length / 2)]));
            }
            else if(i % InvalidStride == InvalidStride / 2 && text.Contains(')'))
            {
                var at = text.IndexOf(')');
                inputs.Add(Make(relative + "#drop-first-rparen", "invalid-drop-first-rparen", relative, text.Remove(at, 1)));
            }
        }

        return inputs;
    }

    private static IEnumerable<string> EnumerateSources(string repoRoot, string relativeDir)
    {
        var root = Path.Combine(repoRoot, relativeDir);
        return Directory.EnumerateFiles(root, "*.mist", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(repoRoot, p).Replace('\\', '/'))
            .Where(p => !p.Split('/').Any(segment => segment is "out" or "bin" or "obj"));
    }

    private static WorkloadInput Make(string id, string kind, string sourcePath, string text)
    {
        var bag = new Storm.Runtime.DiagnosticBag();
        var stream = Mist.Generated.MistLexer.Lex(new Storm.Runtime.SourceText(sourcePath, text), bag);
        var tokens = (ImmutableArray<Storm.Runtime.Token>)Reclassify.Invoke(null, [stream.AllTokens])!;
        var data = new TokenData(
            sourcePath,
            text,
            tokens.Select(t => t.Kind).ToArray(),
            tokens.Select(t => t.Span.Start).ToArray(),
            tokens.Select(t => t.Span.Length).ToArray(),
            tokens.Select(t => t.Start.Line).ToArray(),
            tokens.Select(t => t.Start.Column).ToArray(),
            tokens.Select(t => t.End.Line).ToArray(),
            tokens.Select(t => t.End.Column).ToArray(),
            tokens.Select(t => t.Channel == Storm.Runtime.TokenChannel.Hidden).ToArray(),
            tokens.Select(t => t.Lexeme).ToArray());
        var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        return new WorkloadInput(id, kind, sourcePath, sha, Encoding.UTF8.GetByteCount(text), bag.Count, data);
    }
}