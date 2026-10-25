using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class StructuralContainmentTests
{
    private readonly HandBuiltRefinements h = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("property-same-constant")]
    [InlineData("property-interval-holds-constant")]
    [InlineData("property-covered-only-by-arms-together")]
    [InlineData("property-field-member")]
    [InlineData("property-typed-with-the-input")]
    [InlineData("property-nested-member")]
    [InlineData("property-empty-after-type-test")]
    [InlineData("property-refined-type-test")]
    [InlineData("struct-catch-all-after-every-member-value")]
    [InlineData("struct-negated-property")]
    [InlineData("optional-held-property")]
    [InlineData("tuple-with-property-element")]
    [InlineData("list-same-pair")]
    [InlineData("list-open-prefix-before-pair")]
    [InlineData("list-empty-twice")]
    [InlineData("list-suffix-covers-pair")]
    [InlineData("list-open-slice-before-every-list")]
    [InlineData("list-covered-only-by-lengths-together")]
    [InlineData("list-bound-slice")]
    [InlineData("list-property-element")]
    [InlineData("list-of-lists")]
    [InlineData("list-tuple-element")]
    [InlineData("string-chars")]
    [InlineData("span-catch-all-after-every-length")]
    public void AnArmEarlierStructuralArmsContain_IsUnreachable(string shape)
    {
        var (input, earlier, later) = Arms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, h.Built.Interner);
        Assert.True(finding?.Number.Value == "MIST_PA_SWITCH_ARM_UNREACHABLE", $"{shape}: {finding?.Number.Value ?? "silence"}");
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("property-overlap-not-contained")]
    [InlineData("property-other-member")]
    [InlineData("property-hidden-member-shares-the-name")]
    [InlineData("property-derived-type-test-unread")]
    [InlineData("property-errored-member")]
    [InlineData("property-failed-pattern")]
    [InlineData("property-regex-member-earlier")]
    [InlineData("property-static-member")]
    [InlineData("class-catch-all-after-every-member-value")]
    [InlineData("list-pair-against-other-pair")]
    [InlineData("list-different-length")]
    [InlineData("list-empty-against-open")]
    [InlineData("list-prefix-against-suffix")]
    [InlineData("list-gap-between-lengths")]
    [InlineData("list-custom-indexer-opaque")]
    [InlineData("list-counted-slice-opaque")]
    [InlineData("list-custom-receiver-opaque")]
    [InlineData("list-nested-slice-unread")]
    [InlineData("list-failed")]
    [InlineData("list-enum-element-unread")]
    [InlineData("list-catch-all-after-every-length")]
    [InlineData("string-other-char")]
    public void AnArmSomeStructuralValueStillReaches_StaysSilent(string shape)
    {
        var (input, earlier, later) = Arms(shape);
        var finding = PatternAnalyzer.ArmFinding(earlier, later, input, h.Built.Interner);
        Assert.True(finding is null, $"{shape}: {finding?.Number.Value}");
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("property-same-constant", false)]
    [InlineData("property-same-constant", true)]
    [InlineData("property-interval-holds-constant", false)]
    [InlineData("property-interval-holds-constant", true)]
    [InlineData("list-same-pair", false)]
    [InlineData("list-same-pair", true)]
    [InlineData("list-open-prefix-before-pair", false)]
    [InlineData("list-open-prefix-before-pair", true)]
    public void TheLaterArmIsReported_UnlessTheArmsCoveringItAreGuarded(string shape, bool guarded)
    {
        var (input, earlier, later) = Arms(shape);
        var arms = earlier.Select((pattern, i) => new SwitchArm(HandBuiltModel.Node(), pattern, guarded ? h.Literal(true) : null, h.Literal(i)))
            .Append(new SwitchArm(HandBuiltModel.Node(41), later, null, h.Literal(9)))
            .Append(new SwitchArm(HandBuiltModel.Node(), new CatchAllPattern(HandBuiltModel.Node(), input, null), null, h.Literal(10)))
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

    [Fact]
    [Trait("Tier", "2")]
    public void AHoistedMemberPredicate_AddsNoCoverageToTheArmsAfterIt()
    {
        var point = Shape("Point");
        var name = Member(point, "Name", h.String);
        var arms = new[]
        {
            new SwitchArm(HandBuiltModel.Node(), Property(point, (name, new RegexPattern(HandBuiltModel.Node(), h.String, "a.*"))), null, h.Literal(1)),
            new SwitchArm(HandBuiltModel.Node(41), Property(point, (name, Any(h.String))), null, h.Literal(2)),
            new SwitchArm(HandBuiltModel.Node(), Any(point), null, h.Literal(3)),
        };
        var findings = h.Built.Analyze(new PatternAnalyzer(), new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(point), arms, h.Int));

        Assert.Empty(findings);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void AnErroredSubpattern_ProducesNoDerivativeFinding()
    {
        var point = Shape("Point");
        var x = Member(point, "X", h.Int);
        var failed = new PropertySubpattern(HandBuiltModel.Node(), "X", Equal(h.Int, 1), h.Int, new PropertyResult(x, point, [])) { IsMemberFailed = true };
        var arms = new[]
        {
            new SwitchArm(HandBuiltModel.Node(), new PropertyPattern(HandBuiltModel.Node(), point, [failed]) { IsFailed = true }, null, h.Literal(1)),
            new SwitchArm(HandBuiltModel.Node(41), Property(point, (x, Equal(h.Int, 1))), null, h.Literal(2)),
            new SwitchArm(HandBuiltModel.Node(), Any(point), null, h.Literal(3)),
        };
        var findings = h.Built.Analyze(new PatternAnalyzer(), new SwitchExpression(HandBuiltModel.Node(), HandBuiltRefinements.Local(point), arms, h.Int));

        Assert.Empty(findings);
    }

    private (IMistType Input, Pattern[] Earlier, Pattern Later) Arms(string shape)
    {
        var integer = h.Int;
        var none = h.Built.Type("Void");
        var point = Shape("Point");
        var x = Member(point, "X", integer);
        var y = Member(point, "Y", integer);
        var field = new Field("Z", integer, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = point };
        var inner = Shape("Inner");
        var innerX = Member(inner, "X", integer);
        var holder = Shape("Holder");
        var held = Member(holder, "Inner", inner);
        var pair = HandBuiltModel.Struct("Pair");
        var flag = Member(pair, "Flag", h.Bool);
        var dog = Shape("Dog");
        dog.BaseType = point;
        var numbers = h.Sequence();
        var text = h.String;
        var character = h.Built.Type("Char");
        var spanDefinition = new StructType("Span<,>", "Standard", new Scope(null), null, null);
        spanDefinition.TypeParametersList.Add(new TypeParameter("Element", 0, spanDefinition.Scope, spanDefinition));
        spanDefinition.TypeParametersList.Add(new TypeParameter("Length", 1, spanDefinition.Scope, spanDefinition) { IsDependent = true, Domain = h.Built.Type("Int32") });
        var span = h.Built.Interner.GetOrCreateConstructedType(spanDefinition, [integer, h.Built.Type("Unknown")]);
        var color = HandBuiltModel.Enum("Color", integer);
        var colors = h.Built.Interner.GetOrCreateConstructedType(h.Built.Type("Array<,>"), [color, h.Built.Type("Unknown")]);
        var points = h.Built.Interner.GetOrCreateConstructedType(h.Built.Type("Array<,>"), [point, h.Built.Type("Unknown")]);
        var nested = h.Built.Interner.GetOrCreateConstructedType(h.Built.Type("Array<,>"), [numbers, h.Built.Type("Unknown")]);
        var couple = h.Built.Interner.GetOrCreateTupleType([integer, integer]);
        var couples = h.Built.Interner.GetOrCreateConstructedType(h.Built.Type("Array<,>"), [couple, h.Built.Type("Unknown")]);
        var optional = new UnionType([point, none]);
        var labelled = h.Built.Interner.GetOrCreateTupleType([point, h.Bool]);
        var custom = Shape("Numbers");
        var count = Member(custom, "Count", integer);
        var indexer = new Indexer(integer, [new Parameter("at", integer)], new MemberModifiers()) { DeclaringType = custom };
        var origin = HandBuiltRefinements.RefinePattern(point, Property(point, (x, Equal(integer, 0))), "Origin");

        Pattern list(IMistType sequence, IMistType element, params Pattern?[] elements)
        {
            return List(sequence, element, elements);
        }

        Pattern tuple(IMistType type, params Pattern[] elements)
        {
            return new TuplePattern(HandBuiltModel.Node(), elements, type, null, null);
        }

        return shape switch
        {
            "property-same-constant" => (point, [Property(point, (x, Equal(integer, 1)))], Property(point, (x, Equal(integer, 1)))),
            "property-interval-holds-constant"
                => (point, [Property(point, (x, Compare(integer, ComparisonKind.GreaterThanOrEqual, 0)))], Property(point, (x, Equal(integer, 1)))),
            "property-covered-only-by-arms-together"
                => (point, [Property(point, (x, Compare(integer, ComparisonKind.GreaterThan, 0))),
                    Property(point, (x, Compare(integer, ComparisonKind.LessThanOrEqual, 0)), (y, Compare(integer, ComparisonKind.GreaterThan, 5)))],
                    Property(point, (x, Equal(integer, 0)), (y, Equal(integer, 7)))),
            "property-field-member" => (point, [Property(point, (field, Compare(integer, ComparisonKind.LessThan, 10)))], Property(point, (field, Equal(integer, 3)))),
            "property-typed-with-the-input" => (point, [Property(point, Test(point), (x, Equal(integer, 1)))], Property(point, (x, Equal(integer, 1)))),
            "property-nested-member"
                => (holder, [Property(holder, (held, Property(inner, (innerX, Compare(integer, ComparisonKind.GreaterThan, 0)))))],
                    Property(holder, (held, Property(inner, (innerX, Equal(integer, 1)))))),
            "property-empty-after-type-test" => (point, [Test(point)], Property(point)),
            "property-refined-type-test" => (point, [Property(point, (x, Compare(integer, ComparisonKind.LessThanOrEqual, 0)))], Test(origin)),
            "struct-catch-all-after-every-member-value"
                => (pair, [Property(pair, (flag, Equal(h.Bool, true))), Property(pair, (flag, Equal(h.Bool, false)))], Any(pair)),
            "struct-negated-property" => (pair, [Not(pair, Property(pair, (flag, Equal(h.Bool, true))))], Property(pair, (flag, Equal(h.Bool, false)))),
            "optional-held-property" => (optional, [Property(point, (x, Compare(integer, ComparisonKind.GreaterThan, 0)))], Property(point, (x, Equal(integer, 1)))),
            "tuple-with-property-element"
                => (labelled, [tuple(labelled, Property(point, (x, Compare(integer, ComparisonKind.GreaterThan, 0))), Any(h.Bool))],
                    tuple(labelled, Property(point, (x, Equal(integer, 2))), Equal(h.Bool, true))),
            "list-same-pair" => (numbers, [list(numbers, integer, Equal(integer, 1), Equal(integer, 2))], list(numbers, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-open-prefix-before-pair" => (numbers, [list(numbers, integer, Any(integer), null)], list(numbers, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-empty-twice" => (numbers, [list(numbers, integer)], list(numbers, integer)),
            "list-suffix-covers-pair" => (numbers, [list(numbers, integer, null, Equal(integer, 9))], list(numbers, integer, Equal(integer, 1), Equal(integer, 9))),
            "list-open-slice-before-every-list" => (numbers, [list(numbers, integer, (Pattern?)null)], list(numbers, integer, Equal(integer, 1), null, Equal(integer, 2))),
            "list-covered-only-by-lengths-together"
                => (numbers, [list(numbers, integer, Equal(integer, 1)), list(numbers, integer, Equal(integer, 1), Any(integer), null)],
                    list(numbers, integer, Equal(integer, 1), null)),
            "list-bound-slice" => (numbers, [list(numbers, integer, Equal(integer, 1), Any(numbers))], list(numbers, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-property-element"
                => (points, [list(points, point, Property(point, (x, Compare(integer, ComparisonKind.GreaterThanOrEqual, 0))), null)],
                    list(points, point, Property(point, (x, Equal(integer, 1))), Any(point))),
            "list-of-lists"
                => (nested, [list(nested, numbers, list(numbers, integer, Any(integer), null), null)],
                    list(nested, numbers, list(numbers, integer, Equal(integer, 1), Equal(integer, 2)))),
            "list-tuple-element"
                => (couples, [list(couples, couple, tuple(couple, Compare(integer, ComparisonKind.GreaterThan, 0), Any(integer)), null)],
                    list(couples, couple, tuple(couple, Equal(integer, 1), Equal(integer, 2)))),
            "string-chars" => (text, [list(text, character, Equal(character, 'a'), null)], list(text, character, Equal(character, 'a'), Equal(character, 'b'))),
            "span-catch-all-after-every-length" => (span, [list(span, integer), list(span, integer, Any(integer), null)], Any(span)),
            "property-overlap-not-contained"
                => (point, [Property(point, (x, Compare(integer, ComparisonKind.GreaterThan, 0)))], Property(point, (x, Compare(integer, ComparisonKind.GreaterThanOrEqual, 0)))),
            "property-other-member" => (point, [Property(point, (x, Equal(integer, 1)))], Property(point, (y, Equal(integer, 1)))),
            "property-hidden-member-shares-the-name" => (point, [Property(point, (x, Equal(integer, 1)))], Property(point, (Member(point, "X", integer), Equal(integer, 1)))),
            "property-derived-type-test-unread" => (point, [Property(point, Test(dog), (x, Equal(integer, 1)))], Property(point, (x, Equal(integer, 1)))),
            "property-errored-member"
                => (point, [new PropertyPattern(HandBuiltModel.Node(), point,
                    [new PropertySubpattern(HandBuiltModel.Node(), "X", Any(integer), integer, new PropertyResult(x, point, [])) { IsMemberFailed = true }])],
                    Property(point, (x, Equal(integer, 1)))),
            "property-failed-pattern"
                => (point, [new PropertyPattern(HandBuiltModel.Node(), point, [new PropertySubpattern(HandBuiltModel.Node(), "X", Any(integer), integer, new PropertyResult(x, point, []))]) { IsFailed = true }],
                    Property(point, (x, Equal(integer, 1)))),
            "property-regex-member-earlier"
                => (point, [Property(point, (Member(point, "Name", text), new RegexPattern(HandBuiltModel.Node(), text, "a.*")))], Property(point, (x, Equal(integer, 1)))),
            "property-static-member"
                => (point, [Property(point, (new Property("Shared", integer, new MemberModifiers { IsStatic = true }) { DeclaringType = point }, Any(integer)))],
                    Property(point, (x, Equal(integer, 1)))),
            "class-catch-all-after-every-member-value"
                => (point, [Property(point, (x, Compare(integer, ComparisonKind.GreaterThan, 0))), Property(point, (x, Compare(integer, ComparisonKind.LessThanOrEqual, 0)))], Any(point)),
            "list-pair-against-other-pair" => (numbers, [list(numbers, integer, Equal(integer, 1), Equal(integer, 2))], list(numbers, integer, Equal(integer, 1), Equal(integer, 3))),
            "list-different-length" => (numbers, [list(numbers, integer, Equal(integer, 1), Equal(integer, 2))], list(numbers, integer, Equal(integer, 1), Equal(integer, 2), Equal(integer, 3))),
            "list-empty-against-open" => (numbers, [list(numbers, integer, Any(integer), null)], list(numbers, integer)),
            "list-prefix-against-suffix" => (numbers, [list(numbers, integer, Equal(integer, 1), null)], list(numbers, integer, null, Equal(integer, 9))),
            "list-gap-between-lengths"
                => (numbers, [list(numbers, integer, Equal(integer, 1)), list(numbers, integer, Equal(integer, 1), Any(integer), Any(integer), null)],
                    list(numbers, integer, Equal(integer, 1), null)),
            "list-custom-indexer-opaque"
                => (numbers, [new ListPattern(HandBuiltModel.Node(), [new ListPatternElement(Equal(integer, 1), false, integer)], numbers) { Reads = new CountedIndexer(indexer, integer, count) }],
                    list(numbers, integer, Equal(integer, 1))),
            "list-counted-slice-opaque"
                => (numbers, [new ListPattern(HandBuiltModel.Node(), [new ListPatternElement(Any(integer), false, integer), new ListPatternElement(null, true, null)], numbers) { Slice = new CountedSlice(count, indexer, null) }],
                    list(numbers, integer, Equal(integer, 1))),
            "list-custom-receiver-opaque" => (custom, [list(custom, integer, Equal(integer, 1), Equal(integer, 2))], list(custom, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-nested-slice-unread"
                => (numbers, [list(numbers, integer, Equal(integer, 1), list(numbers, integer, (Pattern?)null))], list(numbers, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-failed"
                => (numbers, [new ListPattern(HandBuiltModel.Node(), [new ListPatternElement(Any(integer), false, integer), new ListPatternElement(null, true, null)], numbers) { IsFailed = true }],
                    list(numbers, integer, Equal(integer, 1), Equal(integer, 2))),
            "list-enum-element-unread" => (colors, [list(colors, color, Equal(color, 0), null)], list(colors, color, Equal(color, 0), null)),
            "list-catch-all-after-every-length" => (numbers, [list(numbers, integer), list(numbers, integer, Any(integer), null)], Any(numbers)),
            "string-other-char" => (text, [list(text, character, Equal(character, 'a'), null)], list(text, character, Equal(character, 'b'), null)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private static ClassType Shape(string name)
    {
        return HandBuiltModel.Class(name);
    }

    private static Property Member(MemberType owner, string name, IMistType type)
    {
        return new Property(name, type, new MemberModifiers()) { DeclaringType = owner, Getter = new PropertyAccessor { IsAuto = true } };
    }

    private static PropertyPattern Property(IMistType type, params (object Member, Pattern Pattern)[] subpatterns)
    {
        return Property(type, null, subpatterns);
    }

    private static PropertyPattern Property(IMistType type, TypeTestPattern? typeTest, params (object Member, Pattern Pattern)[] subpatterns)
    {
        return new PropertyPattern(HandBuiltModel.Node(), typeTest?.NarrowedType ?? type, [.. subpatterns.Select(subpattern => subpattern.Member switch
        {
            Property property => new PropertySubpattern(HandBuiltModel.Node(), property.Name, subpattern.Pattern, property.Type, new PropertyResult(property, type, [])),
            Field field => new PropertySubpattern(HandBuiltModel.Node(), field.Name, subpattern.Pattern, field.Type, new FieldResult(field, type, [])),
            _ => throw new ArgumentOutOfRangeException(nameof(subpatterns)),
        })], typeTest: typeTest);
    }

    private static ListPattern List(IMistType sequence, IMistType element, IReadOnlyList<Pattern?> elements)
    {
        return new ListPattern(HandBuiltModel.Node(), [.. elements.Select(pattern => pattern switch
        {
            null => new ListPatternElement(null, true, null),
            CatchAllPattern slice when ReferenceEquals(slice.NarrowedType, sequence) => new ListPatternElement(slice, true, sequence),
            ListPattern slice when ReferenceEquals(slice.NarrowedType, sequence) => new ListPatternElement(slice, true, sequence),
            _ => new ListPatternElement(pattern, false, element),
        })], sequence);
    }

    private static TypeTestPattern Test(IMistType type)
    {
        return new TypeTestPattern(HandBuiltModel.Node(), type, null, refinement: type is RefinedType refined ? refined.Predicate : null);
    }

    private static CatchAllPattern Any(IMistType type)
    {
        return new CatchAllPattern(HandBuiltModel.Node(), type, null);
    }

    private static NegatedPattern Not(IMistType type, Pattern operand)
    {
        return new NegatedPattern(HandBuiltModel.Node(), type, operand);
    }

    private ComparisonPattern Compare(IMistType type, ComparisonKind kind, object bound)
    {
        return h.Compare(type, kind, bound);
    }

    private ComparisonPattern Equal(IMistType type, object bound)
    {
        return Compare(type, ComparisonKind.Equal, bound);
    }
}