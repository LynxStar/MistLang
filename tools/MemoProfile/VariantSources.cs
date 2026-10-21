using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Storm.SourceGenerator.Analyze;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.Generator;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace MemoProfile;

internal sealed record GeneratedFile(string HintName, string Text);

internal sealed record VariantSources(
    string Name,
    string Derivation,
    MemoizationPlan Plan,
    IReadOnlyList<GeneratedFile> Files,
    double GenerationMs);

internal sealed record GrammarFile(string Path, string Text);

internal static class VariantSourceBuilder
{
    public const string Production = "production";
    public const string AutoOnly = "auto-only";
    public const string ExplicitOnly = "explicit-only";
    public const string NoMemo = "no-memo";

    public static IReadOnlyList<GrammarFile> ReadGrammar(string repoRoot)
    {
        var grammarDir = Path.Combine(repoRoot, "Source", "Mist", "Grammar");
        return Directory.EnumerateFiles(grammarDir, "*.storm", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => new GrammarFile(p, File.ReadAllText(p)))
            .ToList();
    }

    public static IReadOnlyList<GrammarFile> StripMemoAnnotations(IReadOnlyList<GrammarFile> grammar, out int removed)
    {
        var count = 0;
        var stripped = grammar.Select(file =>
        {
            var lines = file.Text.Split('\n');
            var kept = lines.Where(line => line.Trim() != "@memo").ToArray();
            count += lines.Length - kept.Length;
            return file with { Text = string.Join('\n', kept) };
        }).ToList();
        removed = count;
        return stripped;
    }

    public static VariantSources Build(string variant, IReadOnlyList<GrammarFile> grammar)
    {
        switch(variant)
        {
            case Production:
                {
                    var (files, ms) = RunGenerator(grammar);
                    return new VariantSources(variant, "StormIncrementalGenerator over the grammar as committed", AnalyzePlan(grammar), files, ms);
                }
            case AutoOnly:
                {
                    var stripped = StripMemoAnnotations(grammar, out var removed);
                    var (files, ms) = RunGenerator(stripped);
                    return new VariantSources(variant, $"StormIncrementalGenerator over the grammar with its {removed} '@memo' lines removed", AnalyzePlan(stripped), files, ms);
                }
            case ExplicitOnly:
                {
                    var plan = new MemoizationPlan(LoweredRules(grammar).Where(r => r.IsMemo).Select(r => r.Name).ToList());
                    return Experimental(variant, grammar, plan, "the grammar's @memo-annotated rules only");
                }
            case NoMemo:
                return Experimental(variant, grammar, MemoizationPlan.Empty, "MemoizationPlan.Empty");
            default:
                throw new ArgumentException($"unknown variant '{variant}'");
        }
    }

    private static VariantSources Experimental(string variant, IReadOnlyList<GrammarFile> grammar, MemoizationPlan plan, string planDescription)
    {
        var stopwatch = Stopwatch.StartNew();
        var (files, _) = RunGenerator(grammar);
        var parser = EmitParser(grammar, plan);
        stopwatch.Stop();
        var swapped = files.Select(f => f.HintName.EndsWith(".Parser.g.cs", StringComparison.Ordinal) ? f with { Text = parser } : f).ToList();
        return new VariantSources(variant, $"EXPERIMENTAL: production generator output with Mist.Parser.g.cs replaced by ParserEmitter.Emit over the same lowered grammar and {planDescription}", plan, swapped, stopwatch.Elapsed.TotalMilliseconds);
    }

    public static string EmitParser(IReadOnlyList<GrammarFile> grammar, MemoizationPlan plan)
    {
        var (stormGrammar, lowered, name) = Lower(grammar);
        var traits = TraitIRBuilder.Build(stormGrammar);
        var context = new EmissionContext(name + ".Generated", name, lowered, plan, traits.Traits, traits.RuleTraitBindings, emitObsoleteShims: false);
        return ParserEmitter.Emit(context);
    }

    public static MemoizationPlan AnalyzePlan(IReadOnlyList<GrammarFile> grammar)
    {
        return MemoizationAnalysisPass.Run(Lower(grammar).Lowered);
    }

    public static IReadOnlyList<(string Name, bool IsMemo)> LoweredRules(IReadOnlyList<GrammarFile> grammar)
    {
        return Lower(grammar).Lowered.Rules.Select(r => (r.Name, r.IsMemo)).ToList();
    }

    private static (StormGrammar Grammar, GrammarIR Lowered, string Name) Lower(IReadOnlyList<GrammarFile> grammar)
    {
        var byPath = grammar.ToDictionary(f => StormImportResolver.CanonicalizePath(f.Path), f => f.Text, StringComparer.OrdinalIgnoreCase);
        var root = grammar.Single(f => Path.GetFileName(f.Path) == "Mist.storm");
        var resolution = new StormImportResolver(p => byPath.TryGetValue(StormImportResolver.CanonicalizePath(p), out var text) ? text : null)
            .Resolve(StormImportResolver.CanonicalizePath(root.Path));
        var stormGrammar = StormGrammar.Build(resolution);
        var lowering = GrammarIRLowering.Lower(stormGrammar);
        if(!lowering.Succeeded)
        {
            throw new InvalidOperationException("grammar lowering failed: " + string.Join("; ", lowering.Diagnostics.Select(d => d.Message)));
        }

        return (stormGrammar, lowering.Grammar!, "Mist");
    }

    private static (IReadOnlyList<GeneratedFile> Files, double Ms) RunGenerator(IReadOnlyList<GrammarFile> grammar)
    {
        var stopwatch = Stopwatch.StartNew();
        var texts = grammar.Select(f => (AdditionalText)new InMemoryAdditionalText(f.Path, f.Text)).ToImmutableArray();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new StormIncrementalGenerator().AsSourceGenerator()], texts);
        driver = driver.RunGenerators(CSharpCompilation.Create("MemoProfileSeed"));
        var result = driver.GetRunResult().Results.Single();
        stopwatch.Stop();
        if(result.Exception != null)
        {
            throw new InvalidOperationException("Storm generator threw", result.Exception);
        }

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if(errors.Count > 0)
        {
            throw new InvalidOperationException("Storm generator reported errors: " + string.Join("; ", errors));
        }

        var files = result.GeneratedSources.Select(s => new GeneratedFile(s.HintName, s.SourceText.ToString())).ToList();
        return (files, stopwatch.Elapsed.TotalMilliseconds);
    }

    private sealed class InMemoryAdditionalText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return SourceText.From(text);
        }
    }
}