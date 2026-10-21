using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class InheritedStructOperatorEmissionTests
{
    private readonly StructType point = HandBuiltModel.Struct("P");

    private readonly StructType derived;

    private readonly StructType grandchild;

    public InheritedStructOperatorEmissionTests()
    {
        derived = HandBuiltModel.Struct("Q");
        derived.BaseType = point;
        grandchild = HandBuiltModel.Struct("R");
        grandchild.BaseType = derived;
    }

    [Fact]
    public void DerivedOperands_AreConvertedToTheBaseOperatorsParameters()
    {
        var plus = Operator(OperatorKind.Plus, point, point);

        Assert.Equal("((P)a) + ((P)b)", Emit(Binary(plus, Read("a", derived), Read("b", grandchild))));
    }

    [Fact]
    public void AnOperandAlreadyOfTheParameterType_StaysBare()
    {
        var plus = Operator(OperatorKind.Plus, point, point);

        Assert.Equal("a + ((P)b)", Emit(Binary(plus, Read("a", point), Read("b", derived))));
        Assert.Equal("a + b", Emit(Binary(plus, Read("a", point), Read("b", point))));
    }

    [Fact]
    public void ADerivedOperandOfAUnaryBaseOperator_IsConverted()
    {
        var minus = Operator(OperatorKind.Minus, point);
        var negation = new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, Read("a", derived), point)
        {
            Operator = minus,
            ParameterTypes = [point],
        };

        Assert.Equal("-((P)a)", Emit(negation));
    }

    private Operator Operator(OperatorKind kind, params StructType[] parameters)
    {
        var declared = new Operator(kind, point, [.. parameters.Select((parameter, i) => new Parameter($"p{i}", parameter))],
            new MemberModifiers { IsStatic = true })
        { DeclaringType = point };
        point.MethodsList.Add(declared);
        return declared;
    }

    private BinaryExpression Binary(Operator declared, Expression left, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add, right, point)
        {
            Operator = declared,
            ParameterTypes = [point, point],
        };
    }

    private static LocalReferenceExpression Read(string name, StructType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        return BodyEmitter.Render(expression, new DiagnosticBag());
    }
}