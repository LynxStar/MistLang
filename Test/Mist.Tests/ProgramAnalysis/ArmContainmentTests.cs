using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class ArmContainmentTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("optional-every-partition")]
    [InlineData("optional-negation-holds-absence")]
    [InlineData("optional-reference-partitions")]
    [InlineData("optional-ulong-beyond-long")]
    [InlineData("carrier-constituent-twice")]
    [InlineData("carrier-refined-constituent-twice")]
    [InlineData("carrier-empty-property-pattern-after-constituent")]
    [InlineData("carrier-derived-test-after-its-whole-constituent")]
    [InlineData("tuple-product")]
    [InlineData("tuple-covered-only-by-arms-together")]
    [InlineData("tuple-catch-all-after-every-box")]
    [InlineData("tuple-type-test-of-the-tuple")]
    [InlineData("tuple-nested")]
    [InlineData("tuple-optional-element")]
    [InlineData("tuple-ulong-element")]
    [InlineData("tuple-negated-box")]
    public void AnArmEarlierArmsContain_IsUnreachable(string shape)
    {
        var (input, earlier, later) = Arms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, h.Built.Interner);
        Assert.True(finding?.Number.Value == "MIST_PA_SWITCH_ARM_UNREACHABLE", $"{shape}: {finding?.Number.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("optional-partial-overlap")]
    [InlineData("optional-value-arms-leave-absence")]
    [InlineData("optional-reference-value-unread")]
    [InlineData("carrier-catch-all-after-every-constituent")]
    [InlineData("carrier-other-constituent")]
    [InlineData("carrier-derived-test-covers-part-of-a-constituent")]
    [InlineData("tuple-partial")]
    [InlineData("tuple-gap-between-arms")]
    [InlineData("tuple-unreadable-element")]
    [InlineData("tuple-ulong-below-the-sign-boundary")]
    [InlineData("tuple-negated-box-leaves-its-complement")]
    public void AnArmSomeValueStillReaches_StaysSilent(string shape)
    {
        var (input, earlier, later) = Arms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, h.Built.Interner);
        Assert.True(finding is null, $"{shape}: {finding?.Number.Value}");
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("optional-every-partition", false)]
    [InlineData("optional-every-partition", true)]
    [InlineData("optional-ulong-beyond-long", false)]
    [InlineData("optional-ulong-beyond-long", true)]
    [InlineData("carrier-refined-constituent-twice", false)]
    [InlineData("carrier-refined-constituent-twice", true)]
    [InlineData("tuple-covered-only-by-arms-together", false)]
    [InlineData("tuple-covered-only-by-arms-together", true)]
    public void TheLaterArmIsReported_UnlessTheArmsCoveringItAreGuarded(string shape, bool guarded)
    {
        var (input, earlier, later) = Arms(shape);
        var arms = earlier.Select((pattern, i) => new SwitchArm(HandBuiltModel.Node(), pattern, guarded ? h.Literal(true) : null, h.Literal(i)))
            .Append(new SwitchArm(HandBuiltModel.Node(41), later, null, h.Literal(9)))
            .ToList();
        var findings = h.Built.Analyze(new PatternAnalyzer(), new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(input), arms, h.Int));

        if(guarded)
        {
            Assert.Empty(findings);
            return;
        }

        var finding = Assert.Single(findings);
        Assert.Equal("MIST_PA_SWITCH_ARM_UNREACHABLE", finding.Number.Value);
        Assert.Equal(42, finding.Position().Start.Column);
    }

    private (IMistType Input, Pattern[] Earlier, Pattern Later) Arms(string shape)
    {
        var integer = h.Int;
        var truth = h.Bool;
        var unsigned = h.Built.Type("UInt64");
        var none = h.Built.Type("Void");
        var optional = new UnionType([integer, none]);
        var small = h.Refine(integer, ComparisonKind.LessThan, 10, "Small");
        var big = h.Refine(integer, ComparisonKind.GreaterThan, 100, "Big");
        var carrier = new UnionType([big, small]);
        var animal = HandBuiltModel.Class("Animal");
        var dog = HandBuiltModel.Class("Dog");
        dog.BaseType = animal;
        var color = HandBuiltModel.Enum("Color", integer);
        var pair = h.Built.Interner.GetOrCreateTupleType([truth, truth]);
        var numbers = h.Built.Interner.GetOrCreateTupleType([integer, integer]);

        Pattern any(IMistType type)
        {
            return new CatchAllPattern(HandBuiltModel.Node(), type, null);
        }

        Pattern compare(IMistType type, ComparisonKind kind, object bound)
        {
            return h.Compare(type, kind, bound is ulong value ? h.Literal(value, unsigned) : bound);
        }

        Pattern equal(IMistType type, object bound)
        {
            return compare(type, ComparisonKind.Equal, bound);
        }

        Pattern test(IMistType type, IMistType? constituent = null)
        {
            return new TypeTestPattern(HandBuiltModel.Node(), type, null, constituent,
                PatternBinder.ComposeRefinement(type, HandBuiltModel.Node(), refined => refined.Predicate));
        }

        Pattern absent()
        {
            return new TypeTestPattern(HandBuiltModel.Node(), none, null) { TestsAbsence = true };
        }

        Pattern not(IMistType type, Pattern operand)
        {
            return new NegatedPattern(HandBuiltModel.Node(), type, operand);
        }

        TuplePattern tuple(IMistType type, params Pattern[] elements)
        {
            return new TuplePattern(HandBuiltModel.Node(), elements, type, null, null);
        }

        var shallow = h.Built.Interner.GetOrCreateTupleType([pair, truth]);
        var withOptional = h.Built.Interner.GetOrCreateTupleType([optional, truth]);
        var withUnsigned = h.Built.Interner.GetOrCreateTupleType([unsigned, truth]);
        var withColor = h.Built.Interner.GetOrCreateTupleType([color, truth]);
        var optionalUnsigned = new UnionType([unsigned, none]);
        var text = new UnionType([h.String, none]);
        var animals = new UnionType([animal, h.String]);
        const ulong sign = 9223372036854775808UL;

        return shape switch
        {
            "optional-every-partition" => (optional, [test(integer), absent()], any(optional)),
            "optional-negation-holds-absence"
                => (optional, [not(optional, compare(optional, ComparisonKind.GreaterThanOrEqual, 0)), compare(optional, ComparisonKind.GreaterThanOrEqual, 0)], any(optional)),
            "optional-reference-partitions" => (text, [test(h.String), absent()], any(text)),
            "optional-ulong-beyond-long"
                => (optionalUnsigned, [compare(optionalUnsigned, ComparisonKind.GreaterThan, sign)], equal(optionalUnsigned, ulong.MaxValue)),
            "optional-partial-overlap" => (optional, [compare(optional, ComparisonKind.GreaterThan, 0)], compare(optional, ComparisonKind.GreaterThanOrEqual, 0)),
            "optional-value-arms-leave-absence"
                => (optional, [compare(optional, ComparisonKind.LessThan, 0), compare(optional, ComparisonKind.GreaterThanOrEqual, 0)], any(optional)),
            "optional-reference-value-unread" => (text, [equal(text, "a"), absent()], any(text)),
            "carrier-constituent-twice" => (animals, [test(h.String, h.String)], test(h.String, h.String)),
            "carrier-refined-constituent-twice" => (carrier, [test(small, small)], test(small, small)),
            "carrier-empty-property-pattern-after-constituent"
                => (carrier, [test(big, big)], new PropertyPattern(HandBuiltModel.Node(), big, [], typeTest: (TypeTestPattern)test(big, big))),
            "carrier-catch-all-after-every-constituent" => (carrier, [test(small, small), test(big, big)], any(carrier)),
            "carrier-other-constituent" => (carrier, [test(small, small)], test(big, big)),
            "carrier-derived-test-covers-part-of-a-constituent" => (animals, [test(dog, animal)], test(animal, animal)),
            "carrier-derived-test-after-its-whole-constituent" => (animals, [test(animal, animal)], test(dog, animal)),
            "tuple-product" => (pair, [tuple(pair, equal(truth, true), any(truth))], tuple(pair, equal(truth, true), equal(truth, false))),
            "tuple-covered-only-by-arms-together"
                => (numbers, [tuple(numbers, compare(integer, ComparisonKind.GreaterThan, 0), any(integer)),
                    tuple(numbers, compare(integer, ComparisonKind.LessThanOrEqual, 0), compare(integer, ComparisonKind.GreaterThan, 5))],
                    tuple(numbers, equal(integer, 0), equal(integer, 7))),
            "tuple-catch-all-after-every-box"
                => (pair, [tuple(pair, equal(truth, true), any(truth)), tuple(pair, equal(truth, false), any(truth))], any(pair)),
            "tuple-type-test-of-the-tuple" => (pair, [test(pair)], tuple(pair, equal(truth, true), equal(truth, true))),
            "tuple-nested"
                => (shallow, [tuple(shallow, tuple(pair, equal(truth, true), any(truth)), any(truth))],
                    tuple(shallow, tuple(pair, equal(truth, true), equal(truth, false)), equal(truth, true))),
            "tuple-optional-element"
                => (withOptional, [tuple(withOptional, absent(), any(truth)), tuple(withOptional, test(integer), any(truth))],
                    tuple(withOptional, any(optional), equal(truth, true))),
            "tuple-ulong-element"
                => (withUnsigned, [tuple(withUnsigned, compare(unsigned, ComparisonKind.GreaterThanOrEqual, sign), any(truth)),
                    tuple(withUnsigned, compare(unsigned, ComparisonKind.LessThan, sign), any(truth))],
                    tuple(withUnsigned, any(unsigned), equal(truth, false))),
            "tuple-negated-box"
                => (pair, [not(pair, tuple(pair, equal(truth, true), equal(truth, true)))], tuple(pair, equal(truth, false), any(truth))),
            "tuple-partial" => (pair, [tuple(pair, equal(truth, true), any(truth))], tuple(pair, equal(truth, false), equal(truth, false))),
            "tuple-gap-between-arms"
                => (numbers, [tuple(numbers, compare(integer, ComparisonKind.GreaterThan, 0), any(integer)),
                    tuple(numbers, compare(integer, ComparisonKind.LessThanOrEqual, 0), compare(integer, ComparisonKind.GreaterThan, 5))],
                    tuple(numbers, equal(integer, 0), equal(integer, 5))),
            "tuple-unreadable-element" => (withColor, [tuple(withColor, equal(color, 0), any(truth))], tuple(withColor, equal(color, 0), equal(truth, true))),
            "tuple-ulong-below-the-sign-boundary"
                => (withUnsigned, [tuple(withUnsigned, compare(unsigned, ComparisonKind.GreaterThanOrEqual, sign), any(truth))],
                    tuple(withUnsigned, equal(unsigned, sign - 1), any(truth))),
            "tuple-negated-box-leaves-its-complement"
                => (pair, [not(pair, tuple(pair, equal(truth, true), equal(truth, true)))], tuple(pair, equal(truth, true), any(truth))),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}