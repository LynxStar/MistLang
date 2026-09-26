// Tier 3: equivalent equality and inequality guards prove the same bound facts after clean binding.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class InequalityGuardDecisionTests : ProgramModelProbe
{
    public InequalityGuardDecisionTests()
        : base("MistInequalityGuard_")
    {
    }

    protected override string Fixtures { get; } = """
        type NonZero = int not 0;
        type Zero = int 0;
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("if(xs.Length != 0) { return xs[0]; } return 0;")]
    [InlineData("if(0 != xs.Length) { return xs[0]; } return 0;")]
    [InlineData("if(!(xs.Length == 0)) { return xs[0]; } return 0;")]
    [InlineData("if(!(0 == xs.Length)) { return xs[0]; } return 0;")]
    [InlineData("if(!(xs.Length != 0)) { return 0; } return xs[0];")]
    [InlineData("if(!(0 != xs.Length)) { return 0; } return xs[0];")]
    [InlineData("if(xs.Length != 0) { return xs[0]; } else { return 0; }")]
    [InlineData("if(xs.Length != 0) { return xs[^1]; } return 0;")]
    [InlineData("if(0 != xs.Length) { xs[0] = 1; return xs[0]; } return 0;")]
    [InlineData("if(xs.Length == 0) { return 0; } return xs[0];")]
    public void AnEquivalentNonemptyGuard_ProvesTheElementAccess(string body)
    {
        var findings = Analyse($"public static int At(int[] xs) {{ {body} }}", out var nodes);

        Assert.NotEmpty(nodes.OfType<IndexExpression>());
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("if(i != -1 && xs.Length >= 2) { return xs[i]; } return 0;")]
    [InlineData("if(-1 != i && xs.Length >= 2) { return xs[i]; } return 0;")]
    [InlineData("if(!(i == -1) && xs.Length >= 2) { return xs[i]; } return 0;")]
    [InlineData("if(!(i != -1)) { return 0; } if(xs.Length >= 2) { return xs[i]; } return 0;")]
    public void AnInequalityWithinAnIntegralBound_ProvesTheElementAccess(string body)
    {
        var findings = Analyse(
            $"public static int At(int[] xs, int i) where i >= -1 and <= 1 {{ {body} }}", out var nodes);

        Assert.NotEmpty(nodes.OfType<IndexExpression>());
        Assert.Empty(findings);
    }

    [Theory]
    [InlineData("if(x != 0) { NonZero copy = x; return copy; } return 1;")]
    [InlineData("if(0 != x) { NonZero copy = x; return copy; } return 1;")]
    [InlineData("if(!(x == 0)) { NonZero copy = x; return copy; } return 1;")]
    [InlineData("if(!(0 == x)) { NonZero copy = x; return copy; } return 1;")]
    [InlineData("if(!(x != 0)) { return 1; } NonZero copy = x; return copy;")]
    [InlineData("if(x != 0) { return 1; } Zero copy = x; return copy;")]
    [InlineData("if(0 != x) { return 1; } Zero copy = x; return copy;")]
    public void AnEquivalentIntegralGuard_ProvesTheLocalRefinement(string body)
    {
        var findings = Analyse($"public static int At(int x) {{ {body} }}", out var nodes);

        Assert.Contains(nodes, node => node is LocalDeclarationStatement { Local.Name: "copy" });
        Assert.Empty(findings);
    }

    private ImmutableArray<Diagnostic> Analyse(string members, out List<object> nodes)
    {
        var model = BuildModel("", $"public class Guarded {{ {members} }}", "", out var buildDiagnostics);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var guarded = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Guarded"]);
        nodes = [.. guarded.MethodsList.SelectMany(method => BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(method.Body)))];
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);
        return findings.ToImmutableArray();
    }
}