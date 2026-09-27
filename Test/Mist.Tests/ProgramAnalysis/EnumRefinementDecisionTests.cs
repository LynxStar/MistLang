using Mist.Generated;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class EnumRefinementDecisionTests
{
    [Theory]
    [InlineData("Red", 0L, "MIST_PA_REFINEMENT_VIOLATED")]
    [InlineData("Green", 1L, null)]
    [InlineData("Blue", 2L, null)]
    public void StoredConstantsProveOrViolateTheBound(string name, long number, string? expected)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var bound = EnumRefinementModel.Member(color, "Green", model.Number(color, 1L));
        var value = EnumRefinementModel.Member(color, name, model.Number(color, number));

        Assert.Equal(expected, EnumRefinementModel.Judge(value, model.AtLeast(color, bound)));
    }

    [Fact]
    public void DeclarationOrderAndNamesDoNotSupplyTheConstant()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var high = EnumRefinementModel.Member(color, "First", model.Number(color, 100L));
        var low = EnumRefinementModel.Member(color, "Last", model.Number(color, -20L));

        Assert.Equal(100L, Requirement.Constant(high));
        Assert.Equal(-20L, Requirement.Constant(low));
        Assert.Equal("MIST_PA_REFINEMENT_VIOLATED", EnumRefinementModel.Judge(low, model.AtLeast(color, high)));
        Assert.Null(EnumRefinementModel.Judge(high, model.AtLeast(color, low)));
    }

    [Theory]
    [InlineData("SByte", -128L)]
    [InlineData("SByte", 127L)]
    [InlineData("Byte", 0L)]
    [InlineData("Byte", 255L)]
    [InlineData("Int16", -32768L)]
    [InlineData("Int16", 32767L)]
    [InlineData("UInt16", 65535L)]
    [InlineData("Int32", -2147483648L)]
    [InlineData("Int32", 2147483647L)]
    [InlineData("UInt32", 4294967295L)]
    [InlineData("Int64", long.MinValue)]
    [InlineData("Int64", long.MaxValue)]
    [InlineData("UInt64", long.MaxValue)]
    [InlineData("Char", 65535L)]
    public void SupportedUnderlyingValuesStayExact(string underlying, long number)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum(underlying: model.Facts.Built.Type(underlying));
        var member = EnumRefinementModel.Member(color, "Value", model.Number(color, number));

        Assert.Equal(number, Requirement.Constant(member));
        Assert.Null(EnumRefinementModel.Judge(member, model.Facts.Refine(color, ComparisonKind.Equal, member)));
    }

    public static IEnumerable<object[]> BoxedValues()
    {
        yield return ["SByte", (sbyte)-3, -3L];
        yield return ["Byte", (byte)200, 200L];
        yield return ["Int16", (short)-300, -300L];
        yield return ["UInt16", (ushort)60000, 60000L];
        yield return ["Int32", -30000, -30000L];
        yield return ["UInt32", uint.MaxValue, (long)uint.MaxValue];
        yield return ["Int64", long.MinValue, long.MinValue];
        yield return ["UInt64", (ulong)long.MaxValue, long.MaxValue];
        yield return ["Char", 'A', 65L];
    }

    [Theory]
    [MemberData(nameof(BoxedValues))]
    public void StoredIntegralRepresentationsAreReadWithoutReinterpretation(string underlying, object stored, long expected)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum(underlying: model.Facts.Built.Type(underlying));
        var member = EnumRefinementModel.Member(color, "Value", model.Number(color, stored));

        Assert.Equal(expected, Requirement.Constant(member));
    }

    [Fact]
    public void SynthesizedSuccessorsAndNegativeInitializersUseTheirStoredValues()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var start = EnumRefinementModel.Member(color, "Start", model.Number(color, 0L));
        var negative = EnumRefinementModel.Member(color, "Negative", new UnaryExpression(HandBuiltModel.Node(),
            UnaryOperatorKind.Negate, model.Number(color, 20L), model.Facts.Int));
        var successor = EnumRefinementModel.Member(color, "Successor", model.Number(color, -19L));
        var explicitValue = EnumRefinementModel.Member(color, "Sparse", model.Number(color, 100L));
        var next = EnumRefinementModel.Member(color, "Next", model.Number(color, 101L));
        var last = EnumRefinementModel.Member(color, "Last", model.Number(color, 102L));

        Assert.Equal(new long?[] { 0L, -20L, -19L, 100L, 101L, 102L },
            new[] { start, negative, successor, explicitValue, next, last }.Select(Requirement.Constant));
        Assert.Null(EnumRefinementModel.Judge(last, model.AtLeast(color, next)));
    }

    [Fact]
    public void ReferencesClonesAndRepeatedValuesUseTheStoredConstant()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var first = EnumRefinementModel.Member(color, "First", model.Number(color, 42L));
        var second = EnumRefinementModel.Member(color, "Second", first);
        var third = EnumRefinementModel.Member(color, "Third", new CloneExpression(HandBuiltModel.Node(), second));
        var target = model.Facts.Refine(color, ComparisonKind.Equal, first);

        Assert.Equal(42L, Requirement.Constant(third));
        Assert.Null(EnumRefinementModel.Judge(second, target));
        Assert.Null(EnumRefinementModel.Judge(new CloneExpression(HandBuiltModel.Node(), third), target));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecursiveMemberReferencesRemainUnproven(bool indirect)
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var first = EnumRefinementModel.Member(color, "First", null);
        var other = indirect ? EnumRefinementModel.Member(color, "Second", first) : first;
        ((EnumMemberResult)first.Member!).Member.Value = new CloneExpression(HandBuiltModel.Node(), other);
        var bound = EnumRefinementModel.Member(color, "Bound", model.Number(color, 1L));

        Assert.Null(Requirement.Constant(first));
        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", EnumRefinementModel.Judge(first, model.AtLeast(color, bound)));
        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", EnumRefinementModel.Judge(bound, model.AtLeast(color, first)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unresolved")]
    [InlineData("local")]
    [InlineData("field")]
    [InlineData("arithmetic")]
    [InlineData("operator")]
    [InlineData("conversion")]
    [InlineData("cast")]
    [InlineData("real")]
    [InlineData("bool")]
    [InlineData("string")]
    [InlineData("union")]
    [InlineData("custom")]
    [InlineData("unsigned-overflow")]
    [InlineData("negation-overflow")]
    [InlineData("byte-overflow")]
    [InlineData("negative-unsigned")]
    [InlineData("numeric-payload-on-custom-type")]
    public void UnsupportedInitializersSupplyNeitherValuesNorBounds(string scenario)
    {
        var model = new EnumRefinementModel();
        var h = model.Facts;
        var underlying = scenario switch
        {
            "real" => h.Double,
            "bool" => h.Bool,
            "string" => h.String,
            "union" => new UnionType([h.Int, h.Double]),
            "custom" => HandBuiltModel.Class("Custom"),
            "unsigned-overflow" or "negative-unsigned" => h.Built.Type("UInt64"),
            "negation-overflow" => h.Built.Type("Int64"),
            "byte-overflow" => h.Built.Type("Byte"),
            _ => h.Int,
        };
        var color = model.SourceEnum(underlying: underlying);
        Expression? stored = model.Number(color, 1L);
        switch(scenario)
        {
            case "missing": stored = null; break;
            case "unresolved": stored = new UnresolvedExpression(HandBuiltModel.Node()); break;
            case "local": stored = HandBuiltRefinements.Local(h.Int); break;
            case "field":
                stored = new FieldReferenceExpression(HandBuiltModel.Node(), new Field("value", h.Int, default, new MemberModifiers())
                {
                    Initializer = h.Literal(1L),
                });
                break;
            case "arithmetic": stored = h.Binary(h.Literal(1L), BinaryOperatorKind.Add, h.Literal(2L), h.Int); break;
            case "operator":
                stored = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, h.Literal(1L), h.Int)
                {
                    Operator = new Operator(OperatorKind.Minus, h.Int, [new Parameter("value", h.Int)], new MemberModifiers()),
                };
                break;
            case "conversion":
                var custom = HandBuiltModel.Class("Convertible");
                var conversion = HandBuiltModel.Conversion(custom, custom, h.Int);
                stored = new ImplicitConversionExpression(HandBuiltModel.Node(), h.Literal(1L, custom), h.Int,
                    [new ConversionFunction(conversion, custom, h.Int)]);
                break;
            case "cast": stored = new CastExpression(HandBuiltModel.Node(), h.Literal(1L), h.Int, [], h.Int); break;
            case "real": stored = h.Literal(1.0); break;
            case "bool": stored = h.Literal(true); break;
            case "string": stored = h.Literal("one"); break;
            case "unsigned-overflow": stored = model.Number(color, ulong.MaxValue); break;
            case "negation-overflow":
                stored = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate,
                    model.Number(color, long.MinValue), underlying);
                break;
            case "byte-overflow": stored = h.Literal(256L); break;
            case "negative-unsigned": stored = h.Literal(-1L); break;
            case "numeric-payload-on-custom-type": stored = h.Literal(1L, HandBuiltModel.Class("NumericPayload")); break;
        }

        var member = EnumRefinementModel.Member(color, "Unknown", stored);
        var bound = EnumRefinementModel.Member(color, "Known", model.Number(color, 0L));
        var target = h.Refine(color, ComparisonKind.Equal, bound);
        var unreadable = h.Refine(color, ComparisonKind.Equal, member);

        Assert.Null(Requirement.Constant(member));
        Assert.Null(Requirement.OfType(unreadable));
        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", EnumRefinementModel.Judge(member, target));
        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", EnumRefinementModel.Judge(bound, unreadable));
    }

    [Fact]
    public void SourceOnlyReadingLeavesMissingResolvedAndConditionalAccessesUnknown()
    {
        var model = new EnumRefinementModel();
        var projected = HandBuiltModel.Enum("Projected", model.Facts.Int);
        var projectedValue = EnumRefinementModel.Member(projected, "Value", model.Number(projected, 1L));
        var color = model.SourceEnum();
        var member = EnumRefinementModel.Member(color, "Value", model.Number(color, 1L));
        var unresolved = new MemberAccessExpression(HandBuiltModel.Node(), member.Receiver, "Value", color);
        var conditional = new MemberAccessExpression(HandBuiltModel.Node(), member.Receiver, "Value", color,
            isConditional: true, member: member.Member);
        var failed = new MemberAccessExpression(HandBuiltModel.Node(), member.Receiver, "Value", color,
            member: member.Member, isFailed: true);

        Assert.All(new[] { projectedValue, unresolved, conditional, failed }, value => Assert.Null(Requirement.Constant(value)));
    }

    [Fact]
    public void EqualNumbersDoNotConvertBetweenDistinctEnums()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var priority = model.SourceEnum("Priority");
        var red = EnumRefinementModel.Member(color, "Red", model.Number(color, 1L));
        var high = EnumRefinementModel.Member(priority, "High", model.Number(priority, 1L));

        Assert.Equal(Requirement.Constant(red), Requirement.Constant(high));
        Assert.Equal(ConversionKind.Explicit, ConversionClassifier.Classify(priority, color));
        Assert.Null(EnumRefinementModel.Judge(high, model.AtLeast(color, red)));
        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", Assignability.Judge(high, model.AtLeast(color, red), model.Facts.Built.Interner)?.Id.Value);
    }

    [Fact]
    public void AliasImplicationReadsEnumBoundsThroughTheExistingFramework()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum();
        var green = EnumRefinementModel.Member(color, "Green", model.Number(color, 10L));
        var blue = EnumRefinementModel.Member(color, "Blue", model.Number(color, 100L));
        var warm = model.AtLeast(color, green);
        var hot = model.AtLeast(color, blue);
        var alias = new TypeAlias("HotAlias", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = hot };
        var read = HandBuiltRefinements.Local(alias);

        Assert.True(Requirement.OfType(alias)!.Values.IsSubsetOf(Requirement.OfType(warm)!.Values));
        Assert.Null(RefinementDecision.Judge(read, warm, Requirement.ValuesOf(alias)!));
    }

    [Fact]
    public void UnknownUnsignedConstantsDoNotUseTheSignedDomainCeilingAsAProof()
    {
        var model = new EnumRefinementModel();
        var color = model.SourceEnum(underlying: model.Facts.Built.Type("UInt64"));
        var high = EnumRefinementModel.Member(color, "High", model.Number(color, long.MaxValue));
        var outside = EnumRefinementModel.Member(color, "Outside", model.Number(color, ulong.MaxValue));
        var target = model.Facts.Refine(color, ComparisonKind.LessThanOrEqual, high);

        Assert.Null(Requirement.Constant(outside));
        Assert.Equal("MIST_PA_REFINEMENT_UNPROVEN", EnumRefinementModel.Judge(outside, target));
        Assert.Null(EnumRefinementModel.Judge(high, target));
    }
}