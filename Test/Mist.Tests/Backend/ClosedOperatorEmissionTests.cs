using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ClosedOperatorEmissionTests
{
    private readonly HandBuiltModel model = new();

    [Fact]
    public void TheSelectedResultTypesTheEmittedLocal()
    {
        var integer = model.Type("Int32");
        var money = HandBuiltModel.Struct("Money");
        var exact = Declare(money, integer, money, integer);
        var sum = new BinaryExpression(HandBuiltModel.Node(), Ref("m", money), BinaryOperatorKind.Add, HandBuiltModel.Literal(integer, 1), integer)
        {
            Operator = exact,
            ParameterTypes = [money, integer],
        };

        Assert.Equal("int result = m + 1;", Emitted(new LocalDeclarationStatement(new LocalVariable("result", integer), sum)));
    }

    [Fact]
    public void AConvertedOperandIsConvertedOnceInItsOwnPosition()
    {
        var integer = model.Type("Int32");
        var tag = HandBuiltModel.Class("Tag");
        var held = new TypeParameter("T", 0, tag.Scope, tag);
        tag.TypeParametersList.Add(held);
        var closed = model.Interner.GetOrCreateConstructedType(tag, [integer]);
        var open = model.Interner.GetOrCreateConstructedType(tag, [held]);
        var meters = HandBuiltModel.Class("Meters");
        var toTag = HandBuiltModel.Conversion(meters, meters, closed);
        var converted = new ImplicitConversionExpression(HandBuiltModel.Node(), Ref("m", meters), closed, [new ConversionFunction(toTag, meters, closed)]);
        var sum = new BinaryExpression(HandBuiltModel.Node(), Ref("t", closed), BinaryOperatorKind.Add, converted, integer)
        {
            Operator = Declare(tag, held, open, open),
            ParameterTypes = [closed, closed],
        };

        Assert.Equal("int result = t + ((Probe.Tag<int>)(m));", Emitted(new LocalDeclarationStatement(new LocalVariable("result", integer), sum)));
    }

    private static LocalReferenceExpression Ref(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static Operator Declare(MemberType owner, IMistType returns, params IMistType[] parameters)
    {
        var declared = new Operator(
            OperatorKind.Plus, returns, [.. parameters.Select((type, i) => new Parameter($"p{i}", type))], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = owner,
        };
        owner.OperatorsList.Add(declared);
        return declared;
    }

    private static string Emitted(Statement statement)
    {
        var writer = new CSharpWriter();
        BodyEmitter.EmitBlock(HandBuiltModel.Block(statement), writer, new DiagnosticBag());
        return writer.ToString().Trim();
    }
}