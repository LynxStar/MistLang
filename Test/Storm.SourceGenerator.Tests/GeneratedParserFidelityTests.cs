// The generated parser is compiled with Roslyn and run in-process, because what these tests pin -- a follower token
// consumed, a failed alternative rolled back so that a later one parses, Children() walking each real node once --
// is behaviour of the generated code, which reading its text cannot prove. One grammar holds every shape, so the
// class costs two compilations: one plain, one with the tested rules memoized.

using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Storm.SourceGenerator.Analyze;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;

namespace Storm.SourceGenerator.Tests;

[Trait("Tier", "4")]
public sealed class GeneratedParserFidelityTests
{
    // Outer reads Item twice at the same position. With Item memoized the second read is served from the cache,
    // so the position a failed follower leaves behind is the one the cache replays.
    private const string Grammar =
        "token IDENT = [a-z]+;\n" +
        "token LT = \"<\";\n" +
        "token GT = \">\";\n" +
        "token SEMI = \";\";\n" +
        "token COMMA = \",\";\n" +
        "token LP = \"(\";\n" +
        "token RP = \")\";\n" +
        "hidden token WS = [ ]+;\n" +
        "rule TypeArgs = LT IDENT GT;\n" +
        "@memo\nrule Item = name:IDENT typeArgs:TypeArgs (SEMI | COMMA) as Grouped | name:IDENT as Plain;\n" +
        "rule Outer = item:Item RP as Closed | item:Item as Open;\n" +
        "rule Elem = IDENT;\n" +
        "@memo\nrule List = first:Elem (COMMA rest:Elem)*;\n" +
        "@memo\nrule Run = first:Elem (rest:Elem)*;\n" +
        "@memo\nrule Tuple = LP first:Elem COMMA rest:Elem (COMMA additional:Elem)* RP;\n";

    private static readonly string[] MemoizedRules = ["Item", "List", "Run", "Tuple"];

    private static readonly Lazy<Assembly> PlainParser = new(() => Compile(memoized: false));

    private static readonly Lazy<Assembly> MemoizedParser = new(() => Compile(memoized: true));

    [Theory]
    [InlineData("a<b>;", "OpenOuterNode", "GroupedItemNode", 5)]
    [InlineData("a<b>,", "OpenOuterNode", "GroupedItemNode", 5)]
    [InlineData("a<b>;)", "ClosedOuterNode", "GroupedItemNode", 6)]
    [InlineData("a<b>(", "OpenOuterNode", "PlainItemNode", 1)]
    [InlineData("a<b>", "OpenOuterNode", "PlainItemNode", 1)]
    public void AFollowerTokenIsConsumed_AndAMissingOneFallsBackToTheNextAlternative(string input, string outer, string item, int consumed)
    {
        foreach(var (memoized, parser) in Parsers())
        {
            var node = Parse(parser, "Outer", input, out var position);
            var inner = node?.GetType().GetProperty("Item")!.GetValue(node);

            Assert.Equal((memoized, outer, item, consumed), (memoized, node?.GetType().Name, inner?.GetType().Name, position));
        }
    }

    [Theory]
    [InlineData("List", "a", "a")]
    [InlineData("List", "a,b", "a b")]
    [InlineData("List", "a,b,c", "a b c")]
    [InlineData("Run", "a b c", "a b c")]
    [InlineData("Tuple", "(a,b)", "a b")]
    [InlineData("Tuple", "(a,b,c,d)", "a b c d")]
    public void ChildrenYieldsEachRepeatedRuleReferenceOnce_InSourceOrder(string rule, string input, string children)
    {
        foreach(var (memoized, parser) in Parsers())
        {
            var node = Parse(parser, rule, input, out _);

            Assert.NotNull(node);
            Assert.Equal((memoized, children), (memoized, string.Join(" ", ChildrenOf(node).Select(child => TextOf(child, input)))));
        }
    }

    private static (bool Memoized, Assembly Parser)[] Parsers()
    {
        return [(false, PlainParser.Value), (true, MemoizedParser.Value)];
    }

    private static object? Parse(Assembly parser, string rule, string input, out int position)
    {
        var source = Activator.CreateInstance(parser.GetType("Storm.Runtime.SourceText")!, "test.storm", input)!;
        var diagnostics = Activator.CreateInstance(parser.GetType("Storm.Runtime.DiagnosticBag")!)!;
        var tokens = parser.GetType("Test.Generated.TestLexer")!.GetMethod("Lex")!.Invoke(null, [source, diagnostics]);
        var parserType = parser.GetType("Test.Generated.TestParser")!;
        var node = parserType.GetMethod("Parse" + rule)!.Invoke(null, [tokens, diagnostics]);
        position = (int)parserType.GetProperty("LastPosition")!.GetValue(null)!;

        return node;
    }

    private static IEnumerable<object?> ChildrenOf(object node)
    {
        return ((IEnumerable)node.GetType().GetMethod("Children")!.Invoke(node, null)!).Cast<object?>();
    }

    private static string TextOf(object? node, string input)
    {
        if(node == null)
        {
            return "null";
        }

        var span = node.GetType().GetProperty("Span")!.GetValue(node)!;
        var start = (int)span.GetType().GetProperty("Start")!.GetValue(span)!;
        var length = (int)span.GetType().GetProperty("Length")!.GetValue(span)!;

        return input.Substring(start, length);
    }

    private static Assembly Compile(bool memoized)
    {
        var lowering = GrammarIRLowering.Lower(ValidationTestHelpers.BuildGrammar(memoized ? Grammar : Grammar.Replace("@memo\n", "")));

        Assert.True(lowering.Succeeded, string.Join("\n", lowering.Diagnostics.Select(d => d.Message)));

        var plan = memoized ? MemoizationAnalysisPass.Run(lowering.Grammar!) : MemoizationPlan.Empty;

        Assert.All(MemoizedRules, rule => Assert.Equal(memoized, plan.IsMemoized(rule)));

        var context = new EmissionContext("Test.Generated", "Test", lowering.Grammar!, plan, emitObsoleteShims: false);
        string[] sources =
        [
            AstEmitter.EmitRuntimeTypes(),
            AstEmitter.EmitAstNodes(context),
            LexerEmitter.Emit(context),
            ParserEmitter.Emit(context),
            "namespace Mist.Common.Source { public abstract record NodeOrigin { } }",
        ];

        var references = new[] { "System.Runtime", "System.Collections", "System.Collections.Immutable", "System.Linq", "System.Text.RegularExpressions" }
            .Select(name => (MetadataReference)MetadataReference.CreateFromFile(Assembly.Load(name).Location))
            .Append(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "FidelityParser_" + (memoized ? "Memoized" : "Plain"),
            sources.Select(source => CSharpSyntaxTree.ParseText(source)),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);

        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return Assembly.Load(stream.ToArray());
    }
}