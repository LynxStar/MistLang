using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class RequirementReadingDecisionTests
{
    private readonly HandBuiltRefinements h = new();
    private readonly LocalVariable n;
    private readonly LocalVariable m;

    public RequirementReadingDecisionTests()
    {
        n = new LocalVariable("n", h.Int);
        m = new LocalVariable("m", h.Int);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATypeTestAgainstARefinedType_AdmitsExactlyThatTypesValues(bool throughAnAlias)
    {
        var digit = h.Between(h.Int, 0L, 9L, "Digit");
        IMistType tested = throughAnAlias ? new TypeAlias("Numeral", "Probe", null, null, null!) { AliasedType = digit } : digit;

        var required = Requirement.Of(new TypeTestPattern(HandBuiltModel.Node(), tested, null))!;

        Assert.Equal(Admitted.Between(0, 9), required.Values);
        Assert.Empty(required.Bounds);
    }

    [Fact]
    public void ACatchAllOperand_AdmitsEveryValueAndConstrainsNothingItIsJoinedWith()
    {
        var any = new CatchAllPattern(HandBuiltModel.Node(), h.Int, null);
        var negative = h.Compare(h.Int, ComparisonKind.LessThan, 0L);
        var natural = h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L);

        Assert.Same(Requirement.Anything, Requirement.Of(any));
        Assert.True(Requirement.Of(new DisjunctionPattern(HandBuiltModel.Node(), h.Int, [negative, any]))!.Values.IsEverything);
        Assert.Equal(Admitted.AtLeast(0), Requirement.Of(new ConjunctionPattern(HandBuiltModel.Node(), h.Int, [natural, any]))!.Values);
    }

    [Theory]
    [InlineData('A', 65L)]
    [InlineData(true, 1L)]
    [InlineData(false, 0L)]
    [InlineData(-7L, -7L)]
    public void ALiftedValue_IsExactlyItsNumericReading(object value, long expected)
    {
        Assert.Equal(Admitted.Exactly(expected), Requirement.ValuesOf(new LiftedType(h.Int, value)));
    }

    [Fact]
    public void ALiftedULongNoLongHolds_HasNoReadingRatherThanAWrappedOne()
    {
        Assert.Null(Requirement.ValuesOf(new LiftedType(h.Built.Type("UInt64"), ulong.MaxValue)));
    }

    [Theory]
    [InlineData("2*n", false, true)]
    [InlineData("n*2", false, true)]
    [InlineData("-n", false, true)]
    [InlineData("n+m", false, true)]
    [InlineData("-n", true, true)]
    [InlineData("n-1", true, true)]
    [InlineData("n", true, false)]
    [InlineData("n*m", false, false)]
    [InlineData("n-m*2*n", false, false)]
    [InlineData("n/2", false, false)]
    [InlineData("!n", false, false)]
    [InlineData("n+1 user", false, false)]
    [InlineData("-n user", false, false)]
    public void ASymbolicBound_IsReadOnlyWhenItIsLinearOverIntrinsicArithmetic(string form, bool equality, bool readable)
    {
        var operand = Operand(form);

        var required = Requirement.Of(h.Compare(h.Int, equality ? ComparisonKind.Equal : ComparisonKind.LessThan, operand));

        if(readable)
        {
            Assert.True(required!.Values.IsEverything);
            Assert.Same(operand, Assert.Single(required.Bounds).Operand);
        }
        else
        {
            Assert.Null(required);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFieldBound_NamesTheFieldAndTheLocalItIsReadThrough(bool throughALocal)
    {
        var owner = HandBuiltModel.Class("Tally");
        var limit = new Field("limit", h.Int, OwnershipKind.Unrestricted, new MemberModifiers()) { DeclaringType = owner };
        var receiver = new LocalVariable("self", owner);
        Expression through = throughALocal ? HandBuiltRefinements.Read(receiver) : new ThisExpression(HandBuiltModel.Node(), owner);
        var access = new MemberAccessExpression(HandBuiltModel.Node(), through, "limit", h.Int, member: new FieldResult(limit, owner, []));

        var bound = Assert.Single(Requirement.Of(h.Compare(h.Int, ComparisonKind.LessThan, access))!.Bounds);

        Assert.Equal("limit", bound.Name);
        Assert.Same(limit, bound.Member);
        Assert.Same(throughALocal ? receiver : null, bound.Receiver);
    }

    [Fact]
    public void AMemberReadThroughALocalThatIsNeitherPropertyNorField_IsNoBound()
    {
        var owner = HandBuiltModel.Class("Tally");
        var method = new Method("Count", h.Int, [], new MemberModifiers());
        var access = new MemberAccessExpression(
            HandBuiltModel.Node(), HandBuiltRefinements.Read(new LocalVariable("self", owner)), "Count", h.Int, member: new MethodGroupResult([method]));

        Assert.Null(Requirement.Of(h.Compare(h.Int, ComparisonKind.LessThan, access)));
    }

    [Theory]
    [InlineData("a-range")]
    [InlineData("another-parameter")]
    [InlineData("an-ordinary-parameter")]
    public void ADependentBoundClosedWithoutAnExactValueOrADependentForward_KeepsItsOwnParameter(string closing)
    {
        var size = new TypeParameter("N", 0, new Scope(null), null) { IsDependent = true };
        var other = new TypeParameter("K", 0, new Scope(null), null) { IsDependent = true };
        var declared = HandBuiltRefinements.RefinePattern(h.Int, new ConjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.GreaterThanOrEqual, 0L), h.Compare(h.Int, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), size))]),
            name: null);
        var map = closing switch
        {
            "a-range" => new Dictionary<TypeParameter, IMistType> { [size] = h.Between(h.Int, 1L, 5L) },
            "another-parameter" => new Dictionary<TypeParameter, IMistType> { [other] = new LiftedType(h.Int, 3L) },
            _ => new Dictionary<TypeParameter, IMistType> { [size] = new TypeParameter("T", 0, new Scope(null), null) },
        };

        var required = Requirement.OfType(new RefinedType(declared, declared.BaseType, map))!;

        Assert.Same(size, Assert.Single(required.Bounds).Named);
        Assert.Equal(Admitted.AtLeast(0), required.Values);
    }

    private Expression Operand(string form)
    {
        var user = new MemberModifiers { IsIntrinsic = false };
        return form switch
        {
            "2*n" => Binary(h.Literal(2L), BinaryOperatorKind.Multiply, Read(n)),
            "n*2" => Binary(Read(n), BinaryOperatorKind.Multiply, h.Literal(2L)),
            "-n" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Read(n), h.Int),
            "n+m" => Binary(Read(n), BinaryOperatorKind.Add, Read(m)),
            "n-1" => Binary(Read(n), BinaryOperatorKind.Subtract, h.Literal(1L)),
            "n" => Read(n),
            "n*m" => Binary(Read(n), BinaryOperatorKind.Multiply, Read(m)),
            "n-m*2*n" => Binary(Read(n), BinaryOperatorKind.Subtract, Binary(Binary(Read(m), BinaryOperatorKind.Multiply, h.Literal(2L)), BinaryOperatorKind.Multiply, Read(n))),
            "n/2" => Binary(Read(n), BinaryOperatorKind.Divide, h.Literal(2L)),
            "!n" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.LogicalNot, Read(n), h.Int),
            "n+1 user" => new BinaryExpression(HandBuiltModel.Node(), Read(n), BinaryOperatorKind.Add, h.Literal(1L), h.Int)
            {
                Operator = new Operator(OperatorKind.Plus, h.Int, [new Parameter("left", h.Int), new Parameter("right", h.Int)], user),
            },
            "-n user" => new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Read(n), h.Int)
            {
                Operator = new Operator(OperatorKind.Minus, h.Int, [new Parameter("value", h.Int)], user),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };
    }

    private Expression Binary(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return h.Binary(left, kind, right, h.Int);
    }

    private static LocalReferenceExpression Read(LocalVariable local)
    {
        return HandBuiltRefinements.Read(local);
    }
}