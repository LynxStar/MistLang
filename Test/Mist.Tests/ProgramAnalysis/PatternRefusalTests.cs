// Tier 1 decisions and Tier 2 walks distinguish impossible tests from indeterminate inputs.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class PatternRefusalTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("type-test-never-matches", "Int32", "String", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("narrows-to-unknown", "Int32", "unknown", "MIST_PA_PATTERN_NARROWS_TO_UNKNOWN")]
    [InlineData("conjunct-never-matches", "Dog", "Cat", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("negated-test-never-matches", "Dog", "Cat", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("tuple-element-never-matches", "Int32", "String", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("subpattern-never-matches", "String", "Int32", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("typed-property-pattern-never-matches", "Int32", "Dog", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("sealed-class-against-abstract", "Rock", "IShape", "MIST_PA_PATTERN_NEVER_MATCHES")]
    [InlineData("newtype-tested-against-its-source", "Position", "Point", "MIST_PA_PATTERN_NEVER_MATCHES")]
    public void ImpossibleTypeKeepsItsDiagnostic(string caseName, string input, string tested, string expected)
    {
        var fixture = Types();
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), fixture.Types[tested], null);
        var finding = PatternAnalyzer.TypeFinding(pattern, fixture.Types[input], fixture.Model.Interner);
        Assert.True(finding?.Id.Value == expected, $"{caseName}: {finding?.Id.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bound-not-constant", "Int32", "Int32", true, 1, "MIST_PA_PATTERN_BOUND_NOT_CONSTANT")]
    [InlineData("constant-mismatch", "Int32", "String", false, 0, "MIST_PA_PATTERN_CONSTANT_MISMATCH")]
    [InlineData("relational-not-comparable", "String", "Int32", false, 2, "MIST_PA_PATTERN_NOT_COMPARABLE")]
    public void InvalidBoundKeepsItsDiagnostic(string caseName, string input, string bound, bool dynamic, int comparison, string expected)
    {
        var fixture = Types();
        var type = fixture.Types[bound];
        var method = new Method("Max", type, [], default);
        Expression value = dynamic
            ? new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], type, method, [])
            : HandBuiltModel.Literal(type, bound == "String" ? "a" : 3);
        var pattern = new ComparisonPattern(HandBuiltModel.Node(), fixture.Types[input], (ComparisonKind)comparison, value);
        var finding = PatternAnalyzer.ComparisonFinding(pattern, fixture.Types[input], fixture.Model.Interner);
        Assert.True(finding?.Id.Value == expected, $"{caseName}: {finding?.Id.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("switch-arm-unreachable", "Int32", "Int32")]
    [InlineData("switch-arm-after-catch-all", "Animal", "Cat")]
    public void CoveredArmKeepsItsDiagnostic(string caseName, string earlier, string later)
    {
        var fixture = Types();
        var first = new TypeTestPattern(HandBuiltModel.Node(), fixture.Types[earlier], null);
        var next = new TypeTestPattern(HandBuiltModel.Node(), fixture.Types[later], null);
        Assert.Equal("MIST_PA_SWITCH_ARM_UNREACHABLE", PatternAnalyzer.ArmFinding([first], next, fixture.Model.Interner)?.Id.Value);
        Assert.Null(PatternAnalyzer.ArmFinding([], next, fixture.Model.Interner));
        Assert.NotEmpty(caseName);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("type")]
    [InlineData("negated")]
    [InlineData("conjunction")]
    [InlineData("property")]
    [InlineData("typed-property")]
    [InlineData("tuple")]
    [InlineData("switch")]
    public void WalkReportsAtTheFailingSubpattern(string site)
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var text = fixture.Types["String"];
        var rejected = new TypeTestPattern(HandBuiltModel.Node(20), text, null);
        var input = integer;
        Pattern pattern;
        switch(site)
        {
            case "type": pattern = rejected; break;
            case "negated": pattern = new NegatedPattern(HandBuiltModel.Node(), integer, rejected); break;
            case "conjunction":
                input = fixture.Types["Animal"];
                rejected = new TypeTestPattern(HandBuiltModel.Node(20), fixture.Types["Cat"], null);
                pattern = new ConjunctionPattern(HandBuiltModel.Node(), fixture.Types["Dog"],
                    [new TypeTestPattern(HandBuiltModel.Node(), fixture.Types["Dog"], null), new NegatedPattern(HandBuiltModel.Node(), input, rejected)]);
                break;
            case "property":
                input = fixture.Types["Animal"];
                pattern = new PropertyPattern(HandBuiltModel.Node(), input, [new PropertySubpattern(HandBuiltModel.Node(), "Age", rejected, integer, new NotFoundResult())]);
                break;
            case "typed-property": pattern = new PropertyPattern(HandBuiltModel.Node(), text, [], typeTest: rejected); break;
            case "tuple":
                input = fixture.Model.Interner.GetOrCreateTupleType([integer, integer]);
                pattern = new TuplePattern(HandBuiltModel.Node(), [rejected, new CatchAllPattern(HandBuiltModel.Node(), integer, null)], input, null, null);
                break;
            case "switch": pattern = rejected; break;
            default: throw new ArgumentOutOfRangeException(nameof(site));
        }

        Expression expression = site == "switch"
            ? new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input), [new SwitchArm(HandBuiltModel.Node(), pattern, null, HandBuiltModel.Literal(integer, 1))], integer)
            : new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input), pattern, fixture.Types["Boolean"]);
        var finding = Assert.Single(fixture.Model.Analyze(new PatternAnalyzer(), expression));
        Assert.Equal("MIST_PA_PATTERN_NEVER_MATCHES", finding.Id.Value);
        Assert.Equal(21, finding.Location.Start.Column);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("bound-not-constant", "MIST_PA_PATTERN_BOUND_NOT_CONSTANT")]
    [InlineData("constant-mismatch", "MIST_PA_PATTERN_CONSTANT_MISMATCH")]
    [InlineData("relational-not-comparable", "MIST_PA_PATTERN_NOT_COMPARABLE")]
    public void ComparisonReportsAtItsBoundPattern(string shape, string expected)
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var text = fixture.Types["String"];
        var input = shape == "relational-not-comparable" ? text : integer;
        var method = new Method("Max", integer, [], default);
        Expression value = shape switch
        {
            "bound-not-constant" => new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], integer, method, []),
            "constant-mismatch" => HandBuiltModel.Literal(text, "a"),
            _ => HandBuiltModel.Literal(integer, 3),
        };
        var pattern = new ComparisonPattern(HandBuiltModel.Node(20), input,
            shape == "constant-mismatch" ? ComparisonKind.Equal : ComparisonKind.GreaterThan, value);
        var expression = new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input), pattern, fixture.Types["Boolean"]);
        var finding = Assert.Single(fixture.Model.Analyze(new PatternAnalyzer(), expression));

        Assert.Equal(expected, finding.Id.Value);
        Assert.Equal(21, finding.Location.Start.Column);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void CoveredSwitchArmReportsAtTheArm()
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var input = fixture.Types["Animal"];
        SwitchArm arm(IMistType tested, int offset = 0)
        {
            return new SwitchArm(HandBuiltModel.Node(offset), new TypeTestPattern(HandBuiltModel.Node(), tested, null), null, HandBuiltModel.Literal(integer, 1));
        }

        var expression = new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input),
            [arm(fixture.Types["Dog"]), arm(input), arm(fixture.Types["Cat"], 37)], integer);
        var finding = Assert.Single(fixture.Model.Analyze(new PatternAnalyzer(), expression));

        Assert.Equal("MIST_PA_SWITCH_ARM_UNREACHABLE", finding.Id.Value);
        Assert.Equal(38, finding.Location.Start.Column);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("unknown", "String")]
    [InlineData("error", "String")]
    [InlineData("Dog", "Animal")]
    [InlineData("Animal", "Dog")]
    [InlineData("Animal", "IShape")]
    [InlineData("Int32", "Int32")]
    public void PossibleOrIndeterminateInputStaysSilent(string input, string tested)
    {
        var fixture = Types();
        Assert.Null(PatternAnalyzer.TypeFinding(new TypeTestPattern(HandBuiltModel.Node(), fixture.Types[tested], null), fixture.Types[input], fixture.Model.Interner));
    }

    private static (HandBuiltModel Model, Dictionary<string, IMistType> Types) Types()
    {
        var model = new HandBuiltModel();
        var animal = HandBuiltModel.Class("Animal");
        var point = HandBuiltModel.Struct("Point");
        var clone = HandBuiltModel.Struct("Position");
        clone.CloneSource = point;
        clone.CloneSourceType = point;
        var rock = HandBuiltModel.Class("Rock");
        rock.Modifiers = new MemberModifiers { IsSealed = true };
        return (model, new Dictionary<string, IMistType>(StringComparer.Ordinal)
        {
            ["Int32"] = model.Type("Int32"),
            ["String"] = model.Type("String"),
            ["Boolean"] = model.Type("Boolean"),
            ["unknown"] = UnknownType.Instance,
            ["error"] = ErrorType.Instance,
            ["Animal"] = animal,
            ["Dog"] = HandBuiltModel.Class("Dog", animal),
            ["Cat"] = HandBuiltModel.Class("Cat", animal),
            ["IShape"] = new AbstractType("IShape", "Probe", new Scope(null), null, null),
            ["Rock"] = rock,
            ["Point"] = point,
            ["Position"] = clone,
        });
    }
}