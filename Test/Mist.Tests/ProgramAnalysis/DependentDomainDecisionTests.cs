using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What fills a dependent slot, decided against the slot's domain: a value of its kind and in its range,
/// or a parameter whose own domain widens to it. The rule is <see cref="RefinementAnalyzer.DomainFinding"/>,
/// called with hand-built parameters; where it is judged in a program is <see cref="DependentClosingSiteTests"/>.
/// </summary>
[Trait("Tier", "1")]
public sealed class DependentDomainDecisionTests
{
    private const string NotAssignable = "MIST_DT_0003";
    private const string NotConvertible = "MIST_DT_0004";

    private readonly HandBuiltModel built = new();

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
    [InlineData("UInt64", 0L)]
    [InlineData("UInt64", long.MaxValue)]
    [InlineData("UInt64", ulong.MaxValue)]
    public void AValueAtItsDomainsBoundary_FillsTheSlot(string domain, object value)
    {
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent(domain), Lifted(value)));
    }

    [Theory]
    [InlineData("SByte", -129L)]
    [InlineData("SByte", 128L)]
    [InlineData("Byte", -1L)]
    [InlineData("Byte", 256L)]
    [InlineData("Int16", -32769L)]
    [InlineData("Int16", 32768L)]
    [InlineData("UInt16", -1L)]
    [InlineData("UInt16", 65536L)]
    [InlineData("Int32", -2147483649L)]
    [InlineData("Int32", 2147483648L)]
    [InlineData("UInt32", -1L)]
    [InlineData("UInt32", 4294967296L)]
    [InlineData("Int64", 9223372036854775808UL)]
    [InlineData("UInt64", -1L)]
    [InlineData("UInt64", long.MinValue)]
    public void AValueOneBeyondItsDomainsBoundary_IsRefused(string domain, object value)
    {
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(Dependent(domain), Lifted(value))?.Id.Value);
    }

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("Int32", 'A')]
    [InlineData("Byte", false)]
    [InlineData("Boolean", 1L)]
    [InlineData("Boolean", 'T')]
    [InlineData("Char", 65L)]
    [InlineData("Char", true)]
    public void AValueOfAnotherKind_IsRefusedWhateverItsNumber(string domain, object value)
    {
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(Dependent(domain), Lifted(value))?.Id.Value);
    }

    [Theory]
    [InlineData("Boolean", true)]
    [InlineData("Boolean", false)]
    [InlineData("Char", 'A')]
    [InlineData("Char", '￿')]
    public void ABoolOrACharFillsASlotOfItsOwnKind(string domain, object value)
    {
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent(domain), Lifted(value)));
    }

    [Fact]
    public void ATypeInAValueSlot_IsRefused()
    {
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(Dependent("Int32"), built.Type("String"))?.Id.Value);
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(Dependent("Int32"), new TypeParameter("T", 0, new Scope(null), null))?.Id.Value);
    }

    [Fact]
    public void ANamedRefinementJudgesKindAndRangeByThePrimitiveBeneathIt()
    {
        var positive = HandBuiltModel.Refined(built.Type("Byte"), "SmallPositive");
        var slot = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true, Domain = positive };

        Assert.Null(RefinementAnalyzer.DomainFinding(slot, Lifted(200L)));
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(slot, Lifted(256L))?.Id.Value);
        Assert.Equal(NotAssignable, RefinementAnalyzer.DomainFinding(slot, Lifted(true))?.Id.Value);
    }

    [Fact]
    public void AnErasedLengthOrAnUnresolvedArgument_IsNotJudged()
    {
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent("Int32"), UnknownType.Instance));
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent("Int32"), ErrorType.Instance));
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent("Int32"), new UnknownValueType(built.Type("Int32"))));
    }

    [Fact]
    public void AnOrdinaryOrPackSlot_IsNotADomainToJudge()
    {
        var ordinary = new TypeParameter("T", 0, new Scope(null), null) { Domain = built.Type("Type") };
        var pack = Dependent("Int32");
        pack.PackKind = TypePackKind.Bracketed;

        Assert.Null(RefinementAnalyzer.DomainFinding(ordinary, Lifted(5L)));
        Assert.Null(RefinementAnalyzer.DomainFinding(pack, Lifted(5L)));
    }

    [Theory]
    [InlineData("Int32", "Int64")]
    [InlineData("Int32", "Int32")]
    [InlineData("Byte", "UInt16")]
    [InlineData("Byte", "UInt64")]
    [InlineData("SByte", "Int16")]
    [InlineData("UInt32", "Int64")]
    [InlineData("UInt32", "UInt64")]
    [InlineData("Int16", "Int64")]
    [InlineData("Boolean", "Boolean")]
    [InlineData("Char", "Char")]
    public void AParameterForwardedIntoADomainItsOwnWidensTo_IsAdmitted(string from, string to)
    {
        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent(to), Dependent(from, "M")));
    }

    [Theory]
    [InlineData("Int64", "Int32")]
    [InlineData("UInt64", "Int64")]
    [InlineData("Int32", "UInt32")]
    [InlineData("SByte", "Byte")]
    [InlineData("Int32", "UInt64")]
    [InlineData("Boolean", "Int32")]
    [InlineData("Int32", "Boolean")]
    [InlineData("Char", "Int32")]
    [InlineData("Char", "UInt16")]
    [InlineData("UInt16", "Char")]
    public void AParameterForwardedIntoANarrowerOrForeignDomain_IsRefused(string from, string to)
    {
        Assert.Equal(NotConvertible, RefinementAnalyzer.DomainFinding(Dependent(to), Dependent(from, "M"))?.Id.Value);
    }

    [Fact]
    public void ForwardingReadsBothDomainsThroughTheirRefinements()
    {
        var narrow = Dependent("Int64", "M");
        narrow.Domain = HandBuiltModel.Refined(built.Type("Int64"), "Big");
        var wide = Dependent("Int32", "M");
        wide.Domain = HandBuiltModel.Refined(built.Type("Int32"), "Small");

        Assert.Null(RefinementAnalyzer.DomainFinding(Dependent("Int64"), wide));
        Assert.Equal(NotConvertible, RefinementAnalyzer.DomainFinding(Dependent("Int32"), narrow)?.Id.Value);
    }

    [Fact]
    public void ADomainMismatchIsTheFindingEvenWhereARefinementWouldAlsoRefuse()
    {
        var h = new HandBuiltRefinements();
        var slot = new TypeParameter("N", 0, new Scope(null), null)
        {
            IsDependent = true,
            Domain = h.Built.Type("Byte"),
            Refinement = h.Compare(h.Int, ComparisonKind.LessThan, 10L),
        };

        Assert.Equal(NotAssignable, RefinementAnalyzer.DependentFinding(slot, new LiftedType(h.Int, 300L))?.Id.Value);
        Assert.Equal("MIST_PA_DEPENDENT_ARGUMENT_VIOLATED", RefinementAnalyzer.DependentFinding(slot, new LiftedType(h.Int, 20L))?.Id.Value);
        Assert.Null(RefinementAnalyzer.DependentFinding(slot, new LiftedType(h.Int, 9L)));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, "MIST_PA_DEPENDENT_ARGUMENT_VIOLATED")]
    public void AULongAboveLongMeetsAPredicateAsTheTopItSaturatesAt(bool boundedAbove, string? expected)
    {
        var kind = boundedAbove ? ComparisonKind.LessThan : ComparisonKind.GreaterThanOrEqual;
        const ulong bound = 10;
        var h = new HandBuiltRefinements();
        var ulongType = h.Built.Type("UInt64");
        var slot = new TypeParameter("N", 0, new Scope(null), null)
        {
            IsDependent = true,
            Domain = ulongType,
            Refinement = h.Compare(ulongType, kind, HandBuiltModel.Literal(ulongType, bound)),
        };

        Assert.Equal(expected, RefinementAnalyzer.DependentFinding(slot, new LiftedType(h.Int, ulong.MaxValue))?.Id.Value);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "MIST_PA_DEPENDENT_FORWARD_UNPROVEN")]
    public void AForwardedParameterMustImplyTheRefinementItFills(bool constrained, string? expected)
    {
        var h = new HandBuiltRefinements();
        var slot = new TypeParameter("Size", 0, new Scope(null), null)
        {
            IsDependent = true,
            Domain = h.Built.Type("Int32"),
            Refinement = h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L),
        };
        var forwarded = new TypeParameter("N", 0, new Scope(null), null)
        {
            IsDependent = true,
            Domain = h.Built.Type("Int32"),
            Refinement = constrained ? h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 1L) : null,
        };

        Assert.Equal(expected, RefinementAnalyzer.DependentFinding(slot, forwarded)?.Id.Value);
    }

    [Theory]
    [InlineData(5L, "int")]
    [InlineData(2147483648L, "uint")]
    [InlineData(4294967296L, "long")]
    [InlineData(-2147483649L, "long")]
    [InlineData(9223372036854775808UL, "ulong")]
    [InlineData(true, "bool")]
    [InlineData('c', "char")]
    public void AValueIsNamedByTheTypeItsLiteralHas(object value, string expected)
    {
        Assert.Equal(expected, DependentDomains.TypeOfValue(value));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData('A', "A")]
    [InlineData('\n', "\\u000a")]
    [InlineData(ulong.MaxValue, "18446744073709551615")]
    public void AValueIsSpelledAsSourceWritesIt(object value, string expected)
    {
        Assert.Equal(expected, DependentDomains.Spell(value));
    }

    private TypeParameter Dependent(string domain, string name = "N")
    {
        return new TypeParameter(name, 0, new Scope(null), null) { IsDependent = true, Domain = built.Type(domain) };
    }

    private LiftedType Lifted(object value)
    {
        IMistType type = value switch
        {
            bool => built.Type("Boolean"),
            char => built.Type("Char"),
            _ => built.Type("Int32"),
        };

        return new LiftedType(type, value);
    }
}