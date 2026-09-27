// Tier 3 (Component): the emitter's oracle-less readonly inference, a collaborator from the analysis phase it
// builds for itself, agrees with the analysis on a struct method that sets an auto property on a fresh local,
// which is what keeps C#'s readonly on the emitted member.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class ReadonlyEmissionDecisionTests
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };

    [Fact]
    public void AStructMethodSettingAnAutoPropertyOnAFreshLocal_KeepsCSharpReadonly()
    {
        var model = new HandBuiltModel();
        var integer = model.Type("Int32");
        integer.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        var cell = HandBuiltModel.Class("Cell");
        var level = new Property("Level", integer, Public) { DeclaringType = cell, Getter = new PropertyAccessor { IsAuto = true }, Setter = new PropertyAccessor { IsAuto = true } };
        cell.PropertiesList.Add(level);
        var vec = HandBuiltModel.Struct("Vec");
        var x = new Field("X", integer, OwnershipKind.Unrestricted, Public) { DeclaringType = vec };
        vec.FieldsList.Add(x);
        var setting = Peek(vec, "PeekProperty", level, x, sets: true);
        var reading = Peek(vec, "PeekField", level, x, sets: false);
        var emission = ReadonlyEmission.For(vec);

        Assert.True(emission.OnMethod(reading));
        Assert.True(emission.OnMethod(setting));
    }

    private static Method Peek(StructType owner, string name, Property level, Field x, bool sets)
    {
        var cell = level.DeclaringType!;
        var c = new LocalVariable("c", cell);
        var statements = new List<Statement> { new LocalDeclarationStatement(c, new NewExpression(HandBuiltModel.Node(), cell, [], null, [], null)) };

        if(sets)
        {
            statements.Add(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Access(c, level), HandBuiltModel.Literal(x.Type, 3L), x.Type)));
        }

        var sum = new BinaryExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), x), BinaryOperatorKind.Add, Access(c, level), x.Type);
        statements.Add(new ReturnStatement(sum, x.Type));
        var method = new Method(name, x.Type, [], Public) { DeclaringType = owner, Body = HandBuiltModel.Block([.. statements]) };
        owner.MethodsList.Add(method);
        return method;
    }

    private static MemberAccessExpression Access(LocalVariable receiver, Property property)
    {
        return new MemberAccessExpression(
            HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), receiver), property.Name, property.Type,
            member: new PropertyResult(property, receiver.Type, []));
    }
}