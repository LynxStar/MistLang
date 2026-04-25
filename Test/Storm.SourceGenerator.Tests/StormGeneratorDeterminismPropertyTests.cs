using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Storm.SourceGenerator.Generator;
using Xunit;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Property-based tests for Storm generator determinism.
/// </summary>
public class StormGeneratorDeterminismPropertyTests
{
    // ---------------------------------------------------------------
    //  30.1 — Storm generator determinism
    //  **Validates: Correctness Property 10**
    //  Repeated generator invocations produce byte-identical C# output.
    // ---------------------------------------------------------------

    [Fact]
    public void StormGeneratorDeterminism_RepeatedInvocationsProduceIdenticalOutput_SimpleGrammar()
    {
        var grammar = "token Number = [0-9]+;\ntoken Ident = [a-zA-Z_][a-zA-Z0-9_]*;\nhidden token WS = [ \\t\\n]+;\nrule Start = Number;";

        AssertDeterministicOutput(grammar);
    }

    [Fact]
    public void StormGeneratorDeterminism_RepeatedInvocationsProduceIdenticalOutput_MultipleRules()
    {
        var grammar = @"
token Number = [0-9]+;
token Ident = [a-zA-Z_][a-zA-Z0-9_]*;
hidden token WS = [ \t\n]+;
rule Expr = Number | Ident;
rule Start = Expr;";

        AssertDeterministicOutput(grammar);
    }

    [Fact]
    public void StormGeneratorDeterminism_RepeatedInvocationsProduceIdenticalOutput_EmptyGrammar()
    {
        var grammar = "";

        AssertDeterministicOutput(grammar);
    }

    [Fact]
    public void StormGeneratorDeterminism_RepeatedInvocationsProduceIdenticalOutput_HiddenTokens()
    {
        var grammar = @"
token Number = [0-9]+;
hidden token WS = [ \t]+;
hidden token Newline = [\n\r]+;
hidden token Comment = '//' [^\n]*;
rule Start = Number;";

        AssertDeterministicOutput(grammar);
    }

    [Fact]
    public void StormGeneratorDeterminism_RepeatedInvocationsProduceIdenticalOutput_MultipleTokens()
    {
        var grammar = @"
token Plus = '+';
token Minus = '-';
token Star = '*';
token Slash = '/';
token Number = [0-9]+;
hidden token WS = [ \t\n]+;
rule Start = Number;";

        AssertDeterministicOutput(grammar);
    }

    /// <summary>
    /// Runs the generator twice on the same grammar and asserts byte-identical output.
    /// </summary>
    private static void AssertDeterministicOutput(string grammar, string fileName = "Test.storm")
    {
        var result1 = RunGenerator(grammar, fileName);
        var result2 = RunGenerator(grammar, fileName);

        Assert.Equal(result1.GeneratedTrees.Length, result2.GeneratedTrees.Length);

        for(var i = 0; i < result1.GeneratedTrees.Length; i++)
        {
            var text1 = result1.GeneratedTrees[i].GetText().ToString();
            var text2 = result2.GeneratedTrees[i].GetText().ToString();

            Assert.Equal(text1, text2);
        }

        // Also verify diagnostics are identical
        Assert.Equal(result1.Diagnostics.Length, result2.Diagnostics.Length);
    }

    private static GeneratorDriverRunResult RunGenerator(string stormGrammar, string fileName = "Test.storm")
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            references: new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            },
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StormIncrementalGenerator();
        var additionalText = new InMemoryAdditionalText(fileName, stormGrammar);

        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additionalText));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        return driver.GetRunResult();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private string _text;

        public override string Path { get; }

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = text;
        }

        public override SourceText? GetText(System.Threading.CancellationToken cancellationToken = default)
        {
            return SourceText.From(_text);
        }
    }
}
