// Tier 3: sequence lengths follow resolved builtin operations rather than method spelling.

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class SequenceLengthDecisionTests
{
    [Fact]
    public void ResolvedClrExtensions_PreserveCapturedArrayAndStringLengths()
    {
        var diagnostics = new DiagnosticBag();
        using var ambient = AmbientTypeProviderV2Factory.Create("net10.0", [], diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Empty(Findings("""
            int[] values = [10, 20, 30];
            Span<int> view = values.AsSpan();
            var last = view[2];
            var copy = values.ToArray();
            var copiedLast = copy[2];
            var letters = "abc".AsSpan();
            var finalLetter = letters[2];
            """, "using System; using System.Linq;", ambient));
    }

    [Theory]
    [InlineData("var custom = new Custom(); var values = custom.Slice(0, 3); var last = values[2];", "public class Custom { public int[] Slice(int start, int count) { return [10]; } }")]
    [InlineData("int[] values = [10, 20, 30]; var copy = values.ToArray(); var last = copy[2];", "extension(int[] self) { public int[] ToArray() { return [10]; } }")]
    [InlineData("int[] values = [10, 20, 30]; var copy = values.AsSpan(); var last = copy[2];", "extension(int[] self) { public Span<int> AsSpan() { return new Span<int>([10]); } }")]
    [InlineData("int[] values = [10, 20, 30]; var copy = values.ToCharArray(); var last = copy[2];", "extension(int[] self) { public char[] ToCharArray() { return ['x']; } }")]
    public void UserMethods_DoNotManufactureLengthFacts(string statements, string declarations)
    {
        Assert.Equal("MIST_PA_INDEX_UNPROVEN", Assert.Single(Findings(statements, declarations)).Id.Value);
    }

    [Theory]
    [InlineData("var values = new Span<int>([10, 20, 30]); var last = values[2];")]
    [InlineData("var values = new ReadOnlySpan<int>([10, 20, 30]); var last = values[2];")]
    [InlineData("var values = new Span<int>([10, 20, 30]); var copy = values.Slice(1, 2); var last = copy[1];")]
    [InlineData("var values = new ReadOnlySpan<int>([10, 20, 30]); var copy = values.Slice(1, 2); var last = copy[1];")]
    [InlineData("var copy = \"abc\".ToCharArray(); var last = copy[2];")]
    public void ResolvedBuiltinOperations_PreserveSupportedLengths(string statements)
    {
        Assert.Empty(Findings(statements));
    }

    private static ImmutableArray<Diagnostic> Findings(string statements, string declarations = "", IAmbientTypeProvider? ambient = null)
    {
        var model = StandardModelProbe.Build(statements, out var binding, declarations, ambient);
        Assert.Empty(binding.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var nodes = BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(probe.MethodsList.Single(method => method.Name == "Run").Body)).ToList();
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        Assert.NotEmpty(nodes.OfType<IndexExpression>());

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}