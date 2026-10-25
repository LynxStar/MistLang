using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ClrConstantReadingTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly ClassType owner = HandBuiltModel.Class("TimeSpan");

    public static IEnumerable<object?[]> Kinds()
    {
        yield return ["Int64", 10_000_000L, 10_000_000L, (Int128)10_000_000L];
        yield return ["Int32", -42, -42L, (Int128)(-42)];
        yield return ["SByte", sbyte.MinValue, (long)sbyte.MinValue, (Int128)sbyte.MinValue];
        yield return ["UInt64", ulong.MaxValue, null, (Int128)ulong.MaxValue];
        yield return ["Char", 'M', 77L, null];
        yield return ["Boolean", true, 1L, null];
        yield return ["Double", 3.141592653589793, null, null];
        yield return ["String", "mist", null, null];
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void BothReadersReadAClrLiteralFieldThroughItsAccess(string type, object stored, long? constant, Int128? folded)
    {
        var access = Access(Clr("Value", h.Built.Type(type), stored));

        Assert.Equal(constant, Requirement.Constant(access));
        Assert.Equal(folded, IntegralConstant.Fold(access));
    }

    [Fact]
    public void ABareReferenceToAnInheritedClrLiteralIsReadToo()
    {
        var reference = new FieldReferenceExpression(HandBuiltModel.Node(), Clr("Limit", Long, 64L));

        Assert.Equal(64L, Requirement.Constant(reference));
        Assert.Equal((Int128)64, IntegralConstant.Fold(reference));
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("source")]
    [InlineData("conditional")]
    [InlineData("failed")]
    public void FieldsWithoutAProjectedLiteralSupplyNoConstant(string scenario)
    {
        var field = scenario switch
        {
            "readonly" => Clr("Readonly", Long, null),
            "source" => new Field("Declared", Long, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true, IsReadonly = true })
            {
                Initializer = h.Literal(7L, Long),
            },
            _ => Clr("Value", Long, 7L),
        };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner),
            field.Name, Long, isConditional: scenario == "conditional", member: new FieldResult(field, owner, []),
            isFailed: scenario == "failed");

        Assert.Null(Requirement.Constant(access));
        Assert.Null(IntegralConstant.Fold(access));
    }

    [Theory]
    [InlineData(true, 10_000_000L, null)]
    [InlineData(false, 10_000_000L, "MIST_PA_CONSTRAINT_VIOLATED")]
    [InlineData(true, null, "MIST_PA_CONSTRAINT_UNPROVEN")]
    public void AParameterConstraintAdmitsOrRefusesAClrLiteralByItsValue(bool positive, long? stored, string? expected)
    {
        var argument = Access(Clr("TicksPerSecond", Long, stored));
        var constraint = h.Refine(Long, positive ? ComparisonKind.GreaterThan : ComparisonKind.LessThan, h.Literal(0L, Long), name: null);
        var known = Requirement.Constant(argument) is { } value ? Admitted.Exactly(value) : Admitted.Everything;

        Assert.Equal(expected, RefinementDecision.Judge(argument, constraint, known, onConstraint: true)?.Number.Value);
    }

    private IMistType Long => h.Built.Type("Int64");

    private Field Clr(string name, IMistType type, object? stored)
    {
        return new Field(name, type, OwnershipKind.Unrestricted,
            new MemberModifiers { IsStatic = true, IsReadonly = true, IsClrProjected = true })
        {
            DeclaringType = owner,
            Initializer = stored is null ? null : h.Literal(stored, type),
        };
    }

    private MemberAccessExpression Access(Field field)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), owner),
            field.Name, field.Type, member: new FieldResult(field, owner, []));
    }
}