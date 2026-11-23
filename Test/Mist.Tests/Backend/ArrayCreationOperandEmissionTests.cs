using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ArrayCreationOperandEmissionTests
{
    private readonly HandBuiltModel model = new();

    private readonly StructType integer = new("Int32", "Standard", new Scope(null), null, null)
    {
        Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true },
    };

    private IMistType Int => integer;

    private IMistType Holder => model.Type("W");

    [Fact]
    public void AnArrayCreationMultipliedByAValue_IsParenthesizedSoCSharpDoesNotReadAPointerType()
    {
        Assert.Equal("(new int[3]) * w", Emit(Binary(Creation(), BinaryOperatorKind.Multiply, Local("w"))));
    }

    [Fact]
    public void AnArrayCreationIndexed_IsParenthesizedSoCSharpDoesNotReadAJaggedRank()
    {
        Assert.Equal("(new int[3])[0]", Emit(new IndexExpression(HandBuiltModel.Node(), Creation(), Literal(0), Int)));
    }

    [Fact]
    public void AnArrayCreationFollowedByAnotherMultiplication_IsParenthesizedAsTheRightOperand()
    {
        var inner = Binary(Local("w"), BinaryOperatorKind.Multiply, Creation());

        Assert.Equal("w * (new int[3]) * v", Emit(Binary(inner, BinaryOperatorKind.Multiply, Local("v"))));
    }

    [Fact]
    public void AnArrayCreationIndexedAsAnOperand_IsParenthesizedOnce()
    {
        var element = new IndexExpression(HandBuiltModel.Node(), Creation(), Literal(1), Int);

        Assert.Equal("(new int[3])[1] * 2", Emit(Binary(element, BinaryOperatorKind.Multiply, Literal(2))));
    }

    [Fact]
    public void AnArrayCreationWhoseMemberIsRead_IsParenthesized()
    {
        Assert.Equal("(new int[3]).Length", Emit(new MemberAccessExpression(HandBuiltModel.Node(), Creation(), "Length", Int)));
    }

    [Fact]
    public void AnArrayCreationWhoseMemberIsReadConditionally_IsParenthesized()
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), Creation(), "Length", Int, isConditional: true);

        Assert.Equal("(new int[3])?.Length", Emit(access));
    }

    [Fact]
    public void AnArrayCreationThroughAResultIdentity_IsParenthesizedAsTheCreationItIs()
    {
        var identity = new ResultIdentityExpression(HandBuiltModel.Node(), Creation());

        Assert.Equal("(new int[3])[0]", Emit(new IndexExpression(HandBuiltModel.Node(), identity, Literal(0), Int)));
    }

    [Fact]
    public void AnArrayCreationThroughAnIdentityAs_IsParenthesizedAsTheCreationItIs()
    {
        var identity = new AsExpression(HandBuiltModel.Node(), Creation(), Int, AsConversionKind.Identity, null, Int);

        Assert.Equal("(new int[3]) * w", Emit(Binary(identity, BinaryOperatorKind.Multiply, Local("w"))));
    }

    [Fact]
    public void AStackallocMultipliedByAValue_IsParenthesized()
    {
        Assert.Equal("(stackalloc int[3]) * w", Emit(Binary(Stackalloc(), BinaryOperatorKind.Multiply, Local("w"))));
    }

    [Fact]
    public void AStackallocIndexed_IsParenthesized()
    {
        Assert.Equal("(stackalloc int[3])[0]", Emit(new IndexExpression(HandBuiltModel.Node(), Stackalloc(), Literal(0), Int)));
    }

    [Fact]
    public void AnArrayCreationAddedToAValue_IsOneOperand()
    {
        Assert.Equal("(new int[3]) + w", Emit(Binary(Creation(), BinaryOperatorKind.Add, Local("w"))));
    }

    [Fact]
    public void AValueMultipliedByAnArrayCreation_IsOneOperand()
    {
        Assert.Equal("w * (new int[3])", Emit(Binary(Local("w"), BinaryOperatorKind.Multiply, Creation())));
    }

    [Fact]
    public void AnArrayCreationThatIsNotAnOperand_IsWrittenBare()
    {
        Assert.Equal("new int[3]", Emit(Creation()));
    }

    [Fact]
    public void AnOperandThatIsNotAnArrayCreation_IsWrittenBare()
    {
        Assert.Equal("v * w", Emit(Binary(Local("v"), BinaryOperatorKind.Multiply, Local("w"))));
        Assert.Equal("v[0]", Emit(new IndexExpression(HandBuiltModel.Node(), Local("v"), Literal(0), Int)));
    }

    private ArrayCreationExpression Creation()
    {
        return new ArrayCreationExpression(HandBuiltModel.Node(), Int, Literal(3), Int);
    }

    private StackallocExpression Stackalloc()
    {
        return new StackallocExpression(HandBuiltModel.Node(), Int, Literal(3), Int);
    }

    private LiteralExpression Literal(int value)
    {
        return HandBuiltModel.Literal(Int, (long)value);
    }

    private LocalReferenceExpression Local(string name)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, Holder));
    }

    private BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, kind, right, Holder);
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}