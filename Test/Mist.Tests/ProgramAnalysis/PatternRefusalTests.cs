// Tier 1 decisions and Tier 2 walks distinguish impossible tests from indeterminate inputs.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
    [InlineData("type-test-never-matches", "Int32", "String", "MIST0193")]
    [InlineData("narrows-to-unknown", "Int32", "unknown", "MIST0194")]
    [InlineData("conjunct-never-matches", "Dog", "Cat", "MIST0193")]
    [InlineData("negated-test-never-matches", "Dog", "Cat", "MIST0193")]
    [InlineData("tuple-element-never-matches", "Int32", "String", "MIST0193")]
    [InlineData("subpattern-never-matches", "String", "Int32", "MIST0193")]
    [InlineData("typed-property-pattern-never-matches", "Int32", "Dog", "MIST0193")]
    [InlineData("sealed-class-against-abstract", "Rock", "IShape", "MIST0193")]
    [InlineData("newtype-tested-against-its-source", "Position", "Point", "MIST0193")]
    public void ImpossibleTypeKeepsItsDiagnostic(string caseName, string input, string tested, string expected)
    {
        var fixture = Types();
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), fixture.Types[tested], null);
        var finding = PatternAnalyzer.TypeFinding(pattern, fixture.Types[input], fixture.Model.Interner);
        Assert.True(finding?.Number.Value == expected, $"{caseName}: {finding?.Number.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("bound-not-constant", "Int32", "Int32", true, 1, "MIST0198")]
    [InlineData("constant-mismatch", "Int32", "String", false, 0, "MIST0197")]
    [InlineData("relational-not-comparable", "String", "Int32", false, 2, "MIST0196")]
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
        Assert.True(finding?.Number.Value == expected, $"{caseName}: {finding?.Number.Value ?? "silence"}");
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
        Assert.Equal("MIST0199", PatternAnalyzer.ArmFinding([first], next, fixture.Types[earlier], fixture.Model.Interner)?.Number.Value);
        Assert.Null(PatternAnalyzer.ArmFinding([], next, fixture.Types[earlier], fixture.Model.Interner));
        Assert.NotEmpty(caseName);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("identity", true)]
    [InlineData("derived-after-base", true)]
    [InlineData("conformer-after-abstract", true)]
    [InlineData("refined-after-its-base", true)]
    [InlineData("anything-after-unknown", true)]
    [InlineData("sized-after-unsized", true)]
    [InlineData("base-after-derived", false)]
    [InlineData("int-after-long", false)]
    [InlineData("int-constituent-after-long-constituent", false)]
    [InlineData("converted-after-its-implicit-target", false)]
    public void TypeTestArmCoversOnlyWhatItsValueIs(string shape, bool covered)
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var wide = fixture.Types["Int64"];
        var input = (IMistType)UnknownType.Instance;
        static TypeTestPattern test(IMistType tested, IMistType? constituent = null)
        {
            return new TypeTestPattern(HandBuiltModel.Node(), tested, null, constituent);
        }

        var shapeType = (AbstractType)fixture.Types["IShape"];
        var square = HandBuiltModel.Class("Square");
        square.BaseAbstractTypes.Add(shapeType);
        var celsius = HandBuiltModel.Class("Celsius");
        var kelvin = HandBuiltModel.Class("Kelvin");
        _ = HandBuiltModel.Conversion(celsius, celsius, kelvin);
        var adult = new HandBuiltRefinements().Refine(integer, ComparisonKind.GreaterThanOrEqual, 18, "AdultAge");
        var rack = HandBuiltModel.Class("Rack");
        var sized = fixture.Model.Interner.GetOrCreateConstructedType(rack, [integer, new LiftedType(integer, 3L)]);
        var unsized = fixture.Model.Interner.GetOrCreateConstructedType(rack, [integer, new UnknownValueType(integer)]);
        if(shape == "int-constituent-after-long-constituent")
        {
            input = new UnionType([integer, wide]);
        }

        var (earlier, later) = shape switch
        {
            "identity" => (test(integer), test(integer)),
            "derived-after-base" => (test(fixture.Types["Animal"]), test(fixture.Types["Dog"])),
            "conformer-after-abstract" => (test(shapeType), test(square)),
            "refined-after-its-base" => (test(integer), new TypeTestPattern(HandBuiltModel.Node(), adult, null, refinement: adult.Predicate)),
            "anything-after-unknown" => (test(UnknownType.Instance), test(fixture.Types["Dog"])),
            "sized-after-unsized" => (test(unsized), test(sized)),
            "base-after-derived" => (test(fixture.Types["Dog"]), test(fixture.Types["Animal"])),
            "int-after-long" => (test(wide), test(integer)),
            "int-constituent-after-long-constituent" => (test(wide, wide), test(integer, integer)),
            "converted-after-its-implicit-target" => (test(kelvin), test(celsius)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var finding = PatternAnalyzer.ArmFinding([earlier], later, input, fixture.Model.Interner);
        Assert.Equal(covered ? "MIST0199" : null, finding?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("strict-containment")]
    [InlineData("inclusive-endpoint")]
    [InlineData("constant-inside-interval")]
    [InlineData("union-of-earlier-arms")]
    [InlineData("negated-earlier")]
    [InlineData("negated-later")]
    [InlineData("disjunction")]
    [InlineData("conjunction-later")]
    [InlineData("type-test-before-relational")]
    [InlineData("refined-type-test")]
    [InlineData("refined-over-refined")]
    [InlineData("ulong-domain")]
    [InlineData("ulong-beyond-long")]
    [InlineData("optional-held-value")]
    [InlineData("byte-domain")]
    [InlineData("bool-domain")]
    [InlineData("char-range")]
    [InlineData("long-domain")]
    [InlineData("double-domain")]
    [InlineData("decimal-domain")]
    public void RelationalArmEarlierArmsContainIsUnreachable(string shape)
    {
        var (input, earlier, later, interner) = RelationalArms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, interner);
        Assert.True(finding?.Number.Value == "MIST0199", $"{shape}: {finding?.Number.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("exclusive-endpoint")]
    [InlineData("partial-overlap")]
    [InlineData("no-earlier-arm")]
    [InlineData("nan-reaches-catch-all")]
    [InlineData("empty-later")]
    [InlineData("symbolic-bound")]
    [InlineData("unreadable-later")]
    [InlineData("type-test-of-another-domain")]
    [InlineData("refined-over-refined-below-its-base")]
    [InlineData("enum-has-no-domain")]
    [InlineData("ulong-below-the-sign-boundary")]
    [InlineData("optional-absence-reaches-catch-all")]
    [InlineData("unknown-input")]
    public void RelationalArmSomeValueStillReachesStaysSilent(string shape)
    {
        var (input, earlier, later, interner) = RelationalArms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, interner);
        Assert.True(finding is null, $"{shape}: {finding?.Number.Value}");
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void RelationalArmReportsAtTheArmUnlessTheCoveringArmIsGuarded(bool guarded)
    {
        var h = new HandBuiltRefinements();
        var input = h.Int;
        var arms = new[]
        {
            new SwitchArm(HandBuiltModel.Node(), h.Compare(input, ComparisonKind.GreaterThan, 0), guarded ? h.Literal(true) : null, h.Literal(1)),
            new SwitchArm(HandBuiltModel.Node(37), h.Compare(input, ComparisonKind.GreaterThan, 10), null, h.Literal(2)),
            new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), input, null), null, h.Literal(3)),
        };
        var findings = h.Built.Analyze(new PatternAnalyzer(), new SwitchExpression(HandBuiltModel.Node(), h.Literal(5), arms, input));

        if(guarded)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST0199", finding.Number.Value);
        Assert.Equal(38, finding.Position().Start.Column);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("mismatched-constant", null)]
    [InlineData("mismatched-constant-beneath-a-refinement", null)]
    [InlineData("non-constant-bound", "MIST0198")]
    public void ARefinementItsDeclarationRefuses_IsNotJudgedAgainAtEachTest(string shape, string? expected)
    {
        var h = new HandBuiltRefinements();
        var method = new Method("Max", h.Int, [], default);
        var bound = shape == "non-constant-bound"
            ? (Expression)new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), [], h.Int, method, [])
            : h.Literal("x");
        var declared = HandBuiltRefinements.RefinePattern(h.Int, h.Compare(h.Int, ComparisonKind.Equal, bound), "Declared");
        var tested = shape == "mismatched-constant-beneath-a-refinement" ? h.Refine(declared, ComparisonKind.LessThan, 10L, "Beneath") : declared;
        var pattern = new TypeTestPattern(HandBuiltModel.Node(), tested, null,
            refinement: PatternBinder.ComposeRefinement(tested, HandBuiltModel.Node(), refined => refined.Predicate));

        var findings = h.Built.Analyze(new PatternAnalyzer(), new IsPatternExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(h.Int), pattern, h.Bool));

        Assert.Equal(expected, findings.SingleOrDefault()?.Number.Value);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("element", "MIST0193", "int")]
    [InlineData("slice", "MIST0193", "Numbers")]
    [InlineData("nested-list", "MIST0193", "int")]
    [InlineData("list-in-tuple", "MIST0193", "int")]
    [InlineData("list-in-property", "MIST0193", "int")]
    [InlineData("tuple-in-list", "MIST0193", "int")]
    [InlineData("negated-element", "MIST0193", "int")]
    [InlineData("disjunction-element", "MIST0193", "int")]
    [InlineData("conjunction-element", "MIST0193", "int")]
    [InlineData("constant-element", "MIST0197", "int")]
    [InlineData("constant-in-nested-list", "MIST0197", "int")]
    [InlineData("switch-arm-element", "MIST0193", "int")]
    public void ListSubpatternReportsAtTheFailingSubpattern(string site, string expected, string judgedAgainst)
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var text = fixture.Types["String"];
        var numbers = HandBuiltModel.Class("Numbers");
        Pattern rejected = expected == "MIST0197"
            ? new ComparisonPattern(HandBuiltModel.Node(20), integer, ComparisonKind.Equal, HandBuiltModel.Literal(text, "x"))
            : new TypeTestPattern(HandBuiltModel.Node(20), text, null);
        IMistType input = numbers;
        ListPattern list(params ListPatternElement[] elements)
        {
            return new ListPattern(HandBuiltModel.Node(), elements, numbers);
        }

        Pattern pattern = site switch
        {
            "element" or "constant-element" or "switch-arm-element" => list(new ListPatternElement(rejected, false, integer)),
            "slice" => list(
                new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), integer, null), false, integer),
                new ListPatternElement(rejected, true, numbers)),
            "nested-list" or "constant-in-nested-list" => list(new ListPatternElement(list(new ListPatternElement(rejected, false, integer)), false, numbers)),
            "negated-element" => list(new ListPatternElement(new NegatedPattern(HandBuiltModel.Node(), integer, rejected), false, integer)),
            "disjunction-element" => list(new ListPatternElement(new DisjunctionPattern(HandBuiltModel.Node(), integer,
                [new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.Equal, HandBuiltModel.Literal(integer, 1)), rejected]), false, integer)),
            "conjunction-element" => list(new ListPatternElement(new ConjunctionPattern(HandBuiltModel.Node(), integer,
                [new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThan, HandBuiltModel.Literal(integer, 0)), rejected]), false, integer)),
            "tuple-in-list" => list(new ListPatternElement(new TuplePattern(HandBuiltModel.Node(),
                [rejected, new CatchAllPattern(HandBuiltModel.Node(), integer, null)], fixture.Model.Interner.GetOrCreateTupleType([integer, integer]), null, null),
                false, fixture.Model.Interner.GetOrCreateTupleType([integer, integer]))),
            _ => list(new ListPatternElement(rejected, false, integer)),
        };

        if(site == "list-in-tuple")
        {
            input = fixture.Model.Interner.GetOrCreateTupleType([numbers, integer]);
            pattern = new TuplePattern(HandBuiltModel.Node(), [pattern, new CatchAllPattern(HandBuiltModel.Node(), integer, null)], input, null, null);
        }
        else if(site == "list-in-property")
        {
            input = fixture.Types["Animal"];
            pattern = new PropertyPattern(HandBuiltModel.Node(), input, [new PropertySubpattern(HandBuiltModel.Node(), "Scores", pattern, numbers, new NotFoundResult())]);
        }

        Expression expression = site == "switch-arm-element"
            ? new SwitchExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input), [new SwitchArm(HandBuiltModel.Node(), pattern, null, HandBuiltModel.Literal(integer, 1))], integer)
            : new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(input), pattern, fixture.Types["Boolean"]);
        var finding = Assert.Single(fixture.Model.Analyze(new PatternAnalyzer(), expression));

        Assert.Equal(expected, finding.Number.Value);
        Assert.Equal(21, finding.Position().Start.Column);
        Assert.Contains($"'{judgedAgainst}'", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("valid-relational-element")]
    [InlineData("valid-type-test-element")]
    [InlineData("derived-element")]
    [InlineData("custom-shape-element")]
    [InlineData("unknown-element")]
    [InlineData("error-element")]
    [InlineData("bare-slice")]
    [InlineData("already-refused-list")]
    public void ListSubpatternSomeElementCouldPassStaysSilent(string shape)
    {
        var fixture = Types();
        var integer = fixture.Types["Int32"];
        var text = fixture.Types["String"];
        var numbers = HandBuiltModel.Class("Numbers");
        static ListPatternElement element(Pattern subpattern, IMistType scrutinee)
        {
            return new ListPatternElement(subpattern, false, scrutinee);
        }

        var pattern = shape switch
        {
            "already-refused-list" => new ListPattern(HandBuiltModel.Node(), [], ErrorType.Instance),
            _ => new ListPattern(HandBuiltModel.Node(), shape switch
            {
                "valid-relational-element" => [element(new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThan, HandBuiltModel.Literal(integer, 0)), integer)],
                "valid-type-test-element" => [element(new TypeTestPattern(HandBuiltModel.Node(), integer, null), integer)],
                "derived-element" => [element(new TypeTestPattern(HandBuiltModel.Node(), fixture.Types["Dog"], null), fixture.Types["Animal"])],
                "custom-shape-element" => [element(new ComparisonPattern(HandBuiltModel.Node(), text, ComparisonKind.Equal, HandBuiltModel.Literal(text, "v")), text)],
                "unknown-element" => [element(new TypeTestPattern(HandBuiltModel.Node(), text, null), UnknownType.Instance)],
                "error-element" => [element(new TypeTestPattern(HandBuiltModel.Node(), text, null), ErrorType.Instance)],
                "bare-slice" => [element(new CatchAllPattern(HandBuiltModel.Node(), integer, null), integer), new ListPatternElement(null, true, null)],
                _ => throw new ArgumentOutOfRangeException(nameof(shape)),
            }, numbers),
        };

        Assert.Empty(fixture.Model.Analyze(new PatternAnalyzer(),
            new IsPatternExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(numbers), pattern, fixture.Types["Boolean"])));
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
        Assert.Equal("MIST0193", finding.Number.Value);
        Assert.Equal(21, finding.Position().Start.Column);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("bound-not-constant", "MIST0198")]
    [InlineData("constant-mismatch", "MIST0197")]
    [InlineData("relational-not-comparable", "MIST0196")]
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

        Assert.Equal(expected, finding.Number.Value);
        Assert.Equal(21, finding.Position().Start.Column);
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

        Assert.Equal("MIST0199", finding.Number.Value);
        Assert.Equal(38, finding.Position().Start.Column);
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

    private static (IMistType Input, Pattern[] Earlier, Pattern Later, TypeInterner Interner) RelationalArms(string shape)
    {
        var h = new HandBuiltRefinements();
        var input = shape switch
        {
            "byte-domain" => h.Built.Type("Byte"),
            "bool-domain" => h.Bool,
            "char-range" => h.Built.Type("Char"),
            "long-domain" => h.Built.Type("Int64"),
            "double-domain" or "nan-reaches-catch-all" => h.Double,
            "decimal-domain" => h.Built.Type("Decimal"),
            "enum-has-no-domain" => HandBuiltModel.Enum("Color", h.Int),
            "ulong-domain" or "ulong-beyond-long" or "ulong-below-the-sign-boundary" => h.Built.Type("UInt64"),
            "optional-held-value" or "optional-absence-reaches-catch-all" => new UnionType([h.Int, h.Built.Type("Void")]),
            "unknown-input" => UnknownType.Instance,
            _ => h.Int,
        };
        Pattern compare(ComparisonKind kind, object bound)
        {
            return h.Compare(input, kind, bound);
        }

        Pattern equal(object bound)
        {
            return compare(ComparisonKind.Equal, bound);
        }

        Pattern both(params Pattern[] operands)
        {
            return new ConjunctionPattern(HandBuiltModel.Node(), input, operands);
        }

        Pattern unsigned(ComparisonKind kind, ulong bound)
        {
            return compare(kind, h.Literal(bound, input));
        }

        TypeTestPattern refined(RefinedType type)
        {
            return new TypeTestPattern(HandBuiltModel.Node(), type, null,
                refinement: PatternBinder.ComposeRefinement(type, HandBuiltModel.Node(), declared => declared.Predicate));
        }

        var adult = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 18, "AdultAge");
        var senior = HandBuiltRefinements.RefinePattern(adult, h.Compare(adult, ComparisonKind.GreaterThanOrEqual, 65), "Senior");
        var young = HandBuiltRefinements.RefinePattern(adult, h.Compare(adult, ComparisonKind.LessThan, 30), "Young");
        var (earlier, later) = shape switch
        {
            "strict-containment" or "long-domain" or "ulong-domain" or "optional-held-value" or "unknown-input"
                => (new[] { compare(ComparisonKind.GreaterThan, 0) }, compare(ComparisonKind.GreaterThan, 10)),
            "ulong-beyond-long" => ([unsigned(ComparisonKind.GreaterThanOrEqual, 9223372036854775808UL)], unsigned(ComparisonKind.Equal, ulong.MaxValue)),
            "ulong-below-the-sign-boundary"
                => ([unsigned(ComparisonKind.GreaterThanOrEqual, 9223372036854775808UL)], unsigned(ComparisonKind.Equal, 9223372036854775807UL)),
            "optional-absence-reaches-catch-all"
                => ([compare(ComparisonKind.LessThan, 0), compare(ComparisonKind.GreaterThanOrEqual, 0)], new CatchAllPattern(HandBuiltModel.Node(), input, null)),
            "inclusive-endpoint" => ([compare(ComparisonKind.GreaterThanOrEqual, 0)], equal(0)),
            "exclusive-endpoint" => ([compare(ComparisonKind.GreaterThan, 0)], equal(0)),
            "constant-inside-interval" => ([both(compare(ComparisonKind.GreaterThan, 0), compare(ComparisonKind.LessThan, 10))], equal(5)),
            "partial-overlap" => ([both(compare(ComparisonKind.GreaterThan, 0), compare(ComparisonKind.LessThan, 10))], compare(ComparisonKind.GreaterThan, 5)),
            "union-of-earlier-arms" => ([compare(ComparisonKind.LessThan, 0), compare(ComparisonKind.GreaterThanOrEqual, 0)], new CatchAllPattern(HandBuiltModel.Node(), input, null)),
            "negated-earlier" => ([new NegatedPattern(HandBuiltModel.Node(), input, equal(0))], equal(5)),
            "negated-later" => ([compare(ComparisonKind.GreaterThan, 0), compare(ComparisonKind.LessThan, 0)], new NegatedPattern(HandBuiltModel.Node(), input, equal(0))),
            "disjunction" => ([new DisjunctionPattern(HandBuiltModel.Node(), input, [compare(ComparisonKind.LessThan, 0), compare(ComparisonKind.GreaterThan, 10)])], equal(20)),
            "conjunction-later" => ([compare(ComparisonKind.GreaterThan, 0)], both(compare(ComparisonKind.GreaterThan, 5), compare(ComparisonKind.LessThan, 8))),
            "type-test-before-relational" => ([new TypeTestPattern(HandBuiltModel.Node(), h.Int, null)], compare(ComparisonKind.GreaterThan, 5)),
            "type-test-of-another-domain" => ([new TypeTestPattern(HandBuiltModel.Node(), h.Built.Type("Int64"), null)], equal(5)),
            "refined-type-test" => ([new TypeTestPattern(HandBuiltModel.Node(), adult, null, refinement: adult.Predicate)], equal(30)),
            "refined-over-refined" => ([refined(senior)], equal(70)),
            "refined-over-refined-below-its-base" => ([refined(young)], equal(5)),
            "byte-domain" => ([compare(ComparisonKind.GreaterThanOrEqual, 0)], new CatchAllPattern(HandBuiltModel.Node(), input, null)),
            "bool-domain" => ([equal(true), equal(false)], new CatchAllPattern(HandBuiltModel.Node(), input, null)),
            "char-range" => ([both(compare(ComparisonKind.GreaterThanOrEqual, 'a'), compare(ComparisonKind.LessThanOrEqual, 'z'))], equal('q')),
            "double-domain" => ([compare(ComparisonKind.GreaterThan, 0.0)], compare(ComparisonKind.GreaterThan, 1.5)),
            "nan-reaches-catch-all" => ([compare(ComparisonKind.LessThan, 0.0), compare(ComparisonKind.GreaterThanOrEqual, 0.0)], new CatchAllPattern(HandBuiltModel.Node(), input, null)),
            "decimal-domain" => ([compare(ComparisonKind.GreaterThan, 0m)], compare(ComparisonKind.GreaterThan, 10m)),
            "no-earlier-arm" => ([], equal(5)),
            "empty-later" => ([compare(ComparisonKind.GreaterThan, 5)], both(compare(ComparisonKind.GreaterThan, 10), compare(ComparisonKind.LessThan, 3))),
            "symbolic-bound" => ([compare(ComparisonKind.GreaterThan, new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("floor", h.Int)))], compare(ComparisonKind.GreaterThan, 10)),
            "unreadable-later" => ([compare(ComparisonKind.GreaterThan, 0)], new PropertyPattern(HandBuiltModel.Node(), input, [])),
            "enum-has-no-domain" => ([compare(ComparisonKind.GreaterThanOrEqual, 0)], equal(5)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        return (input, earlier, later, h.Built.Interner);
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
            ["Int64"] = model.Type("Int64"),
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