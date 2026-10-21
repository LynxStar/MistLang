using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Storm.SourceGenerator.Analyze;

namespace MemoProfile;

internal sealed class ParserVariant
{
    public const string MistCore = "mist-core";

    private const string AdapterSource = """
        using System.Collections.Immutable;
        using Storm.Runtime;

        namespace MemoProfile.Adapter
        {
            public static class ParserAdapter
            {
                public static object MakeTokens(string path, string contents, int[] kind, int[] start, int[] length, int[] startLine, int[] startColumn, int[] endLine, int[] endColumn, bool[] hidden, string[] lexeme)
                {
                    var builder = ImmutableArray.CreateBuilder<Token>(kind.Length);
                    for (var i = 0; i < kind.Length; i++)
                    {
                        builder.Add(new Token(kind[i], new SourceSpan(start[i], length[i]), new LinePosition(startLine[i], startColumn[i]), new LinePosition(endLine[i], endColumn[i]), hidden[i] ? TokenChannel.Hidden : TokenChannel.Default, lexeme[i]));
                    }
                    return new TokenStream(new SourceText(path, contents), builder.MoveToImmutable());
                }

                public static object NewBag() => new DiagnosticBag();

                public static object? Parse(object tokens, object bag) => Mist.Generated.MistParser.ParseCompilationUnit((TokenStream)tokens, (DiagnosticBag)bag);

                public static int LastPosition() => Mist.Generated.MistParser.LastPosition;

                public static string[] Diagnostics(object bag)
                {
                    var entries = ((DiagnosticBag)bag).Entries;
                    var result = new string[entries.Count];
                    for (var i = 0; i < entries.Count; i++)
                    {
                        result[i] = entries[i].Line + ":" + entries[i].Column + " " + entries[i].Message;
                    }
                    return result;
                }
            }
        }
        """;

    private readonly Func<TokenData, object> makeTokens;
    private readonly Func<object> newBag;
    private readonly Func<object, object, object?> parse;
    private readonly Func<int> lastPosition;
    private readonly Func<object, string[]> diagnostics;
    private readonly FieldInfo? memoField;

    private ParserVariant(string name, MemoizationPlan plan, Assembly assembly, double compileMs, Func<TokenData, object> makeTokens, Func<object> newBag, Func<object, object, object?> parse, Func<int> lastPosition, Func<object, string[]> diagnostics)
    {
        Name = name;
        Plan = plan;
        Assembly = assembly;
        CompileMs = compileMs;
        this.makeTokens = makeTokens;
        this.newBag = newBag;
        this.parse = parse;
        this.lastPosition = lastPosition;
        this.diagnostics = diagnostics;
        memoField = assembly.GetType("Mist.Generated.MistParser")!.GetField("_memo", BindingFlags.NonPublic | BindingFlags.Static);
    }

    public string Name { get; }

    public MemoizationPlan Plan { get; }

    public Assembly Assembly { get; }

    public double CompileMs { get; }

    public static ParserVariant FromMistCore(MemoizationPlan productionPlan)
    {
        static object make(TokenData t)
        {
            var tokens = System.Collections.Immutable.ImmutableArray.CreateBuilder<Storm.Runtime.Token>(t.Kind.Length);
            for(var i = 0; i < t.Kind.Length; i++)
            {
                tokens.Add(new Storm.Runtime.Token(t.Kind[i], new Storm.Runtime.SourceSpan(t.Start[i], t.Length[i]), new Storm.Runtime.LinePosition(t.StartLine[i], t.StartColumn[i]), new Storm.Runtime.LinePosition(t.EndLine[i], t.EndColumn[i]), t.Hidden[i] ? Storm.Runtime.TokenChannel.Hidden : Storm.Runtime.TokenChannel.Default, t.Lexeme[i]));
            }

            return new Storm.Runtime.TokenStream(new Storm.Runtime.SourceText(t.Path, t.Contents), tokens.MoveToImmutable());
        }

        return new ParserVariant(
            MistCore,
            productionPlan,
            typeof(Mist.Generated.MistParser).Assembly,
            0,
            make,
            () => new Storm.Runtime.DiagnosticBag(),
            (tokens, bag) => Mist.Generated.MistParser.ParseCompilationUnit((Storm.Runtime.TokenStream)tokens, (Storm.Runtime.DiagnosticBag)bag),
            () => Mist.Generated.MistParser.LastPosition,
            bag => ((Storm.Runtime.DiagnosticBag)bag).Entries.Select(e => e.Line + ":" + e.Column + " " + e.Message).ToArray());
    }

    public static ParserVariant Compile(VariantSources sources)
    {
        var stopwatch = Stopwatch.StartNew();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources.Files.Select(f => CSharpSyntaxTree.ParseText(f.Text, parseOptions, path: f.HintName))
            .Append(CSharpSyntaxTree.ParseText(AdapterSource, parseOptions, path: "ParserAdapter.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            "MemoProfile.Variant." + sources.Name,
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        if(!emit.Success)
        {
            throw new InvalidOperationException($"variant {sources.Name} failed to compile: " + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5)));
        }

        _ = image.Seek(0, SeekOrigin.Begin);
        var assembly = new AssemblyLoadContext("MemoProfile." + sources.Name).LoadFromStream(image);
        stopwatch.Stop();

        var adapter = assembly.GetType("MemoProfile.Adapter.ParserAdapter")!;
        var makeTokens = adapter.GetMethod("MakeTokens")!.CreateDelegate<Func<string, string, int[], int[], int[], int[], int[], int[], int[], bool[], string[], object>>();
        return new ParserVariant(
            sources.Name,
            sources.Plan,
            assembly,
            stopwatch.Elapsed.TotalMilliseconds,
            t => makeTokens(t.Path, t.Contents, t.Kind, t.Start, t.Length, t.StartLine, t.StartColumn, t.EndLine, t.EndColumn, t.Hidden, t.Lexeme),
            adapter.GetMethod("NewBag")!.CreateDelegate<Func<object>>(),
            adapter.GetMethod("Parse")!.CreateDelegate<Func<object, object, object?>>(),
            adapter.GetMethod("LastPosition")!.CreateDelegate<Func<int>>(),
            adapter.GetMethod("Diagnostics")!.CreateDelegate<Func<object, string[]>>());
    }

    public object MakeTokens(TokenData data)
    {
        return makeTokens(data);
    }

    public object? Parse(object tokens)
    {
        return parse(tokens, newBag());
    }

    public ParseOutcome ParseForCheck(object tokens)
    {
        var bag = newBag();
        var root = parse(tokens, bag);
        return new ParseOutcome(root is null ? null : TreeFingerprint.Of(root), lastPosition(), diagnostics(bag), MemoRuleCounts());
    }

    private Dictionary<string, int> MemoRuleCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if(memoField?.GetValue(null) is not IDictionary memo)
        {
            return counts;
        }

        foreach(long key in memo.Keys)
        {
            var rule = Plan.MemoizedRuleNames[(int)(key >> 32)];
            counts[rule] = counts.GetValueOrDefault(rule) + 1;
        }

        return counts;
    }
}

internal sealed record ParseOutcome(TreeFingerprint? Tree, int LastPosition, string[] Diagnostics, Dictionary<string, int> MemoEntriesByRule);