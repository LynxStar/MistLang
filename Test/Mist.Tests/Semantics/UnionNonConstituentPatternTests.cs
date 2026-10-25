using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Semantics;

public sealed class UnionNonConstituentPatternTests
{
    private const string Path = "Probe/Union.mist";

    [Fact]
    [Trait("Tier", "1")]
    public void ATypeNoConstituentHolds_IsAnError()
    {
        Assert.Equal("MIST_PM_PATTERN_TYPE_NOT_A_CONSTITUENT", ProgramModelDiagnostics.PatternTypeNotAConstituent.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, ProgramModelDiagnostics.PatternTypeNotAConstituent.DefaultSeverity);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("is")]
    [InlineData("switch")]
    public void ATypeNoConstituentHolds_IsReportedOnceAtThePatternsType(string form)
    {
        var f = Fixture();

        _ = Bind(f, form, Typed("Boolean", "b"));

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_PATTERN_TYPE_NOT_A_CONSTITUENT", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Path, diagnostic.Location.Path);
        Assert.Equal(new LinePositionSpan(new LinePosition(1, 41), new LinePosition(1, 42)), diagnostic.Position());
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("is")]
    [InlineData("switch")]
    public void AConstituentTypeTest_IsAdmittedAndNarrowsThroughIt(string form)
    {
        var f = Fixture();

        var bound = Bind(f, form, Typed("Int32", "n"));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var pattern = form == "is"
            ? Assert.IsType<IsPatternExpression>(bound).Pattern
            : Assert.Single(Assert.IsType<SwitchExpression>(bound).Arms).Pattern;
        Assert.Same(f.Built.Type("Int32"), Assert.IsType<TypeTestPattern>(pattern).Constituent);
    }

    private static BindingFragments Fixture()
    {
        var f = new BindingFragments();
        _ = f.Local("v", f.Built.Interner.GetOrCreateUnionType([f.Built.Type("Int32"), f.Built.Type("String")]));
        return f;
    }

    private static Expression Bind(BindingFragments f, string form, PrimaryPatternNode primary)
    {
        var pattern = new PatternNode
        {
            OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
        };

        if(form == "switch")
        {
            var selection = At<SwitchExpressionSuffixPostfixSuffixNode>(60);
            selection.Arms = new SwitchArmListNode
            {
                SwitchArm = new SwitchArmNode { Pattern = pattern, Result = BindingFragments.Expression(BindingFragments.Identifier("v")) },
            };
            return f.Bind(BindingFragments.Identifier("v"), selection);
        }

        var identifier = At<IdentifierExpressionPrimaryNode>(30);
        identifier.Name = BindingFragments.Token("v");
        var expression = (AssignmentFormExpressionNode)BindingFragments.Expression(identifier);
        var relational = (RelationalComparisonRelationalNode)expression.Assignment.Target.Left.Left.Left.Left;
        expression.Assignment.Target.Left.Left.Left.Left = new IsExpressionRelationalNode { Left = relational.Left, Pattern = pattern };
        return ExpressionBinder.BindExpression(expression, f.Binding, f.Builder);
    }

    private static TypePatternPrimaryPatternNode Typed(string type, string name)
    {
        var primary = At<TypePatternPrimaryPatternNode>();
        primary.Type = BindingFragments.TypeName(type);
        primary.Name = BindingFragments.Token(name);
        return primary;
    }

    private static T At<T>(int offset = 40) where T : SyntaxNode, new()
    {
        var location = HandBuiltModel.Node(offset, Path);
        return new T { Span = location.Span, Origin = location.Origin };
    }
}