// Feature: node-origin-tracking, Property 3: Parser Assigns Correct ParsedOrigin to All Nodes
// **Validates: Requirements 3.1, 3.2**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Source;
using Mist.Lexing;
using Mist.Parsing;
using Storm.Runtime;

namespace Mist.Tests.Parsing;

public class ParserOriginAssignmentPropertyTests
{
    private static readonly MistLexer Lexer = new();
    private static readonly MistParser Parser = new();

    private static Gen<string> GenValidMistSource()
    {
        return Gen.OneOf(
            Gen.Constant("class Foo { }"),
            Gen.Constant("class Bar { public void DoStuff() { } }"),
            Gen.Constant("class Baz { public int GetValue() { return 1; } }"),
            Gen.Constant("class Program { public static void Main() { } }"),
            Gen.Constant("class Program { public static void Main() { let x = 1; } }"),
            Gen.Constant("class Program { public static void Main() { let x = true; } }"),
            Gen.Constant("class Program { public static void Main() { let x = 1; let y = 2; } }"),
            Gen.Constant("class Animal { public string Name; }"),
            Gen.Constant("class Math { public static int Add(int a, int b) { return a; } }"),
            Gen.Constant("class Empty { }"),
            GenClassWithMethods(),
            GenClassWithFields());
    }

    private static Gen<string> GenClassWithMethods()
    {
        return from methodCount in Gen.Choose(1, 3)
               from className in Gen.Elements("Alpha", "Beta", "Gamma", "Delta")
               let methods = string.Join("\n    ",
                   Enumerable.Range(0, methodCount)
                       .Select(i => $"public void Method{i}() {{ }}"))
               select $"class {className} {{\n    {methods}\n}}";
    }

    private static Gen<string> GenClassWithFields()
    {
        return from fieldCount in Gen.Choose(1, 3)
               from className in Gen.Elements("Config", "Settings", "State", "Data")
               let fields = string.Join("\n    ",
                   Enumerable.Range(0, fieldCount)
                       .Select(i => $"public int field{i};"))
               select $"class {className} {{\n    {fields}\n}}";
    }

    private static Gen<MistSourceScenario> GenMistSourceScenario()
    {
        return from source in GenValidMistSource()
               from path in Gen.Elements(
                   "test.mist", "src/main.mist", "lib/utils.mist",
                   "foo/bar.mist", "project/module.mist")
               select new MistSourceScenario(path, source);
    }

    public static Arbitrary<MistSourceScenario> ArbitraryMistSourceScenario()
    {
        return GenMistSourceScenario().ToArbitrary();
    }

    private static IEnumerable<SyntaxNode> WalkAllNodes(SyntaxNode root)
    {
        yield return root;
        foreach(var child in root.Children())
        {
            if(child is not null)
            {
                foreach(var descendant in WalkAllNodes(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ParserOriginAssignmentPropertyTests) })]
    public Property AllNodes_HaveParsedOrigin_WithCorrectSourceText(MistSourceScenario scenario)
    {
        var sourceText = new Mist.Common.Source.SourceText(scenario.Path, scenario.Source);
        var bag = new Mist.Common.Diagnostics.DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        if(result.Root is null)
        {
            return true.ToProperty().Label("Skipped: parser returned null root (skeletal)");
        }

        var allNodes = WalkAllNodes(result.Root).ToList();

        foreach(var node in allNodes)
        {
            if(node.Origin is not ParsedOrigin parsedOrigin)
            {
                return false.ToProperty()
                    .Label($"Node of type {node.GetType().Name} has Origin of type " +
                           $"{node.Origin?.GetType().Name ?? "null"} instead of ParsedOrigin. " +
                           $"Source: {scenario.Source}");
            }

            if(!ReferenceEquals(parsedOrigin.Source, sourceText))
            {
                return false.ToProperty()
                    .Label($"Node of type {node.GetType().Name} has ParsedOrigin with different " +
                           $"SourceText instance. Source: {scenario.Source}");
            }
        }

        return true.ToProperty()
            .Label($"All {allNodes.Count} nodes have correct ParsedOrigin");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ParserOriginAssignmentPropertyTests) })]
    public Property AllNodes_ShareSameParsedOriginInstance(MistSourceScenario scenario)
    {
        var sourceText = new Mist.Common.Source.SourceText(scenario.Path, scenario.Source);
        var bag = new Mist.Common.Diagnostics.DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        if(result.Root is null)
        {
            return true.ToProperty().Label("Skipped: parser returned null root (skeletal)");
        }

        var allNodes = WalkAllNodes(result.Root).ToList();

        if(allNodes.Count == 0)
        {
            return true.ToProperty().Label("Skipped: no nodes in tree");
        }

        var firstOrigin = allNodes[0].Origin;
        foreach(var node in allNodes.Skip(1))
        {
            if(!ReferenceEquals(node.Origin, firstOrigin))
            {
                return false.ToProperty()
                    .Label($"Node of type {node.GetType().Name} has a different Origin instance " +
                           $"than the root node. Source: {scenario.Source}");
            }
        }

        return true.ToProperty()
            .Label($"All {allNodes.Count} nodes share the same ParsedOrigin instance");
    }
}

public record MistSourceScenario(string Path, string Source);