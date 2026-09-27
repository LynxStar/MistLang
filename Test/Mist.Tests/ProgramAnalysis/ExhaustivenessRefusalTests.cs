// Tier 1: finite missing cases and unbounded constant-only switches are judged directly.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ExhaustivenessRefusalTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bool-missing-a-value")]
    [InlineData("enum-missing-a-variant")]
    [InlineData("only-a-guarded-catch-all")]
    [InlineData("only-constants-over-an-unbounded-type")]
    [InlineData("union-missing-a-constituent")]
    public void MissingCaseKeepsItsDiagnostic(string caseName)
    {
        var expression = Switch(caseName);
        var finding = ExhaustivenessAnalyzer.Finding(expression);
        Assert.Equal("MIST_PA_SWITCH_NOT_EXHAUSTIVE", finding?.Id.Value);
        Assert.Equal(Mist.Common.Diagnostics.DiagnosticSeverity.Warning, finding?.DefaultSeverity);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bool-missing-a-value")]
    [InlineData("enum-missing-a-variant")]
    [InlineData("only-a-guarded-catch-all")]
    [InlineData("only-constants-over-an-unbounded-type")]
    [InlineData("union-missing-a-constituent")]
    public void UnguardedCatchAllCompletesEachSwitch(string caseName)
    {
        var input = Switch(caseName);
        var catchAll = new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), input.Scrutinee.Type, null), null, input.Scrutinee);
        var complete = new SwitchExpression(input.Node, input.Scrutinee, [.. input.Arms, catchAll], input.Type);
        Assert.Null(ExhaustivenessAnalyzer.Finding(complete));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ReportsTheSwitchSiteAndNamesTheMissingValue()
    {
        var input = Switch("bool-missing-a-value");
        var diagnostics = new HandBuiltModel().Analyze(new ExhaustivenessAnalyzer(), input);
        var finding = Assert.Single(diagnostics);
        Assert.Equal("MIST_PA_SWITCH_NOT_EXHAUSTIVE", finding.Id.Value);
        Assert.Equal(31, finding.Location.Start.Column);
        Assert.Contains("false", finding.Message, StringComparison.Ordinal);
    }

    private static SwitchExpression Switch(string caseName)
    {
        var model = new HandBuiltModel();
        var boolean = model.Type("Boolean");
        var integer = model.Type("Int32");
        var text = model.Type("String");
        var color = HandBuiltModel.Enum("Color", integer);
        color.MembersList.AddRange([new EnumMember("Red", false, null), new EnumMember("Green", false, null), new EnumMember("Blue", false, null)]);
        var union = model.Interner.GetOrCreateUnionType([integer, text]);
        IMistType input;
        IReadOnlyList<SwitchArm> arms;

        SwitchArm arm(Pattern pattern, Expression? guard = null)
        {
            return new SwitchArm(HandBuiltModel.Node(), pattern, guard, HandBuiltModel.Literal(text, "result"));
        }

        ComparisonPattern constant(IMistType type, object value)
        {
            return new ComparisonPattern(HandBuiltModel.Node(), type, ComparisonKind.Equal, HandBuiltModel.Literal(type, value));
        }

        switch(caseName)
        {
            case "bool-missing-a-value":
                input = boolean;
                arms = [arm(constant(boolean, true))];
                break;
            case "enum-missing-a-variant":
                input = color;
                arms = [.. new[] { "Red", "Green" }.Select(name => arm(new ComparisonPattern(HandBuiltModel.Node(), color, ComparisonKind.Equal,
                    new MemberAccessExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(color), name, color))))];
                break;
            case "only-a-guarded-catch-all":
                input = integer;
                arms = [arm(new CatchAllPattern(HandBuiltModel.Node(), integer, null), HandBuiltModel.Literal(boolean, true))];
                break;
            case "only-constants-over-an-unbounded-type":
                input = text;
                arms = [arm(constant(text, "a")), arm(constant(text, "b"))];
                break;
            case "union-missing-a-constituent":
                input = union;
                arms = [arm(new TypeTestPattern(HandBuiltModel.Node(), integer, null, integer))];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName));
        }

        return new SwitchExpression(HandBuiltModel.Node(30), HandBuiltModel.Literal(input), arms, text);
    }
}