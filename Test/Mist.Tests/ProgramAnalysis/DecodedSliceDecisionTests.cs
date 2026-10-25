// Tier 3: escaped literals supply runtime character counts across predicate and slice proofs.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class DecodedSliceDecisionTests
{
    [Theory]
    [InlineData("var slice = \"a\\nb\"[0..3]; var last = slice.ToCharArray()[2];")]
    [InlineData("string text = \"a\\nb\"; Range window = 0..3; var slice = text[window]; var last = slice.ToCharArray()[2];")]
    [InlineData("string text = \"a\\'b\"; var copy = text; if(flag) { copy = \"a'b\"; } Range window = 0..3; var last = copy[window].ToCharArray()[2];")]
    [InlineData("string text = \"a\\nb\"; var copy = text; text = \"x\"; var last = copy[0..3].ToCharArray()[2];")]
    public void DecodedAndCapturedLengths_ProveOnlyRuntimeCharacters(string statements)
    {
        Assert.Empty(Findings(statements));
    }

    [Theory]
    [InlineData("var slice = \"a\\nb\"[0..4];")]
    [InlineData("string text = \"a\\nb\"; Range window = 0..4; var slice = text[window];")]
    [InlineData("var last = \"a\\nb\".ToCharArray()[3];")]
    [InlineData("string text = \"a\\'b\"; if(flag) { text = \"a'b\"; } var last = text.ToCharArray()[3];")]
    public void SourceEscapeCharacters_DoNotJustifyAnExtraElement(string statements)
    {
        Assert.Equal("MIST0274", Assert.Single(Findings(statements)).Number.Value);
    }

    private static ImmutableArray<Diagnostic> Findings(string statements)
    {
        var model = StandardModelProbe.Build("bool flag = true; " + statements, out var binding);
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