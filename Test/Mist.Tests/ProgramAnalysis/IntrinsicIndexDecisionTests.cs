// Tier 3 (Component): an intrinsic array, span or string index is bound against Standard's declared
// parameter and judged like a user indexer's. Bounds remain the refinement prover's question. These
// fixtures use real Standard sources because a shell declares no indexer; Assignability states the
// admitted integral conversions.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class IntrinsicIndexDecisionTests
{
    private const string Receivers = """
        int[3] array = [10, 20, 30];
        int[] unsized = [10, 20, 30];
        Span<int> span = stackalloc int[3];
        ReadOnlySpan<int> readOnlySpan = stackalloc int[3];
        string word = "abc";
        """;

    private const string Types = """
        public enum Color : byte { Red, Green }

        public class Counter
        {
            public Counter() { }

            public static implicit operator int(Counter c) { return 1; }
        }
        """;

    private static readonly string[] BoundsIds = ["MIST_PA_INDEX_UNPROVEN", "MIST_PA_INDEX_OUT_OF_BOUNDS"];

    [Theory]
    [InlineData("var e = array[1];", "ArrayBounds")]
    [InlineData("var e = unsized[1];", "ArrayBounds")]
    [InlineData("var e = span[1];", "SpanBounds")]
    [InlineData("var e = readOnlySpan[1];", "SpanBounds")]
    [InlineData("var e = word[1];", "Standard.Int32")]
    public void AnIntrinsicElementAccess_RecordsItsDeclaredParameterAndNoIndexer(string statement, string parameter)
    {
        var read = Assert.IsType<IndexExpression>(LastLocal(Build(statement, out _)).Initializer);

        Assert.Null(read.Indexer);
        Assert.Equal(parameter, read.ParameterType?.FullyQualifiedName);
    }

    [Fact]
    public void AnIndexReachingTheParameterThroughADeclaredImplicit_RecordsTheConversion()
    {
        var read = Assert.IsType<IndexExpression>(LastLocal(Build("var e = word[new Counter()];", out _)).Initializer);

        Assert.Equal("Standard.Int32", Assert.IsType<ImplicitConversionExpression>(read.Index).Type.FullyQualifiedName);
    }

    [Theory]
    [InlineData("var e = array[^1];")]
    [InlineData("var e = word[^1];")]
    [InlineData("var e = array[0..2];")]
    [InlineData("var e = word[0..2];")]
    public void AFromEndIndexOrARange_RecordsNoSlotAndIsNotJudged(string statement)
    {
        Assert.Null(Assert.IsType<IndexExpression>(LastLocal(Build(statement, out _)).Initializer).ParameterType);
        Assert.Empty(Errors(Analyse(statement)));
    }

    [Theory]
    [InlineData("int i = 1;")]
    [InlineData("sbyte i = 1;")]
    [InlineData("byte i = 1;")]
    [InlineData("short i = 1;")]
    [InlineData("ushort i = 1;")]
    [InlineData("char i = 'a';")]
    [InlineData("Color i = Color.Green;")]
    [InlineData("var i = new Counter();")]
    public void AnIndexThatReachesInt_IsJudgedOnlyForItsBoundsAtEveryIntrinsicIndexer(string declaration)
    {
        AssertOnlyBoundsJudged(
            $"{declaration} var a = array[i]; var u = unsized[i]; var s = span[i]; var r = readOnlySpan[i]; var w = word[i]; array[i] = a; span[i] = s;");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("'a'")]
    public void AConstantThatReachesInt_IsJudgedOnlyForItsBoundsAtEveryIntrinsicIndexer(string constant)
    {
        AssertOnlyBoundsJudged(
            $"var a = array[{constant}]; var u = unsized[{constant}]; var s = span[{constant}]; var r = readOnlySpan[{constant}]; var w = word[{constant}];");
    }

    private static void AssertOnlyBoundsJudged(string statement)
    {
        Assert.DoesNotContain(Errors(Analyse(statement)), diagnostic => !BoundsIds.Contains(diagnostic.Number.Value));
    }

    private static List<Diagnostic> Analyse(string statement)
    {
        var model = Build(statement, out var buildDiagnostics);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        return [.. buildDiagnostics, .. findings.ToImmutableArray()];
    }

    private static ProgramModelClass Build(string statement, out ImmutableArray<Diagnostic> diagnostics)
    {
        return StandardModelProbe.Build(Receivers + statement, out diagnostics, Types);
    }

    private static LocalDeclarationStatement LastLocal(ProgramModelClass model)
    {
        var run = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run");

        return run.Body!.Statements.OfType<LocalDeclarationStatement>().Last();
    }

    private static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}