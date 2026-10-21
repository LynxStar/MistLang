using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class FieldEffectFragments
{
    public FieldEffectFragments()
    {
        Owner = HandBuiltModel.Class("Ledger");
        Items = new Field("items", H.Sequence("Array"), default, new MemberModifiers()) { Initializer = H.Array(3L), DeclaringType = Owner };
        Count = new Field("count", H.Int, default, new MemberModifiers()) { DeclaringType = Owner };
    }

    public HandBuiltRefinements H { get; } = new();

    public ClassType Owner { get; }

    public Field Items { get; }

    public Field Count { get; }

    public FieldReferenceExpression ItemsRead => new(HandBuiltModel.Node(), Items);

    public ThisExpression This => new(HandBuiltModel.Node(), Owner);

    public Block Reads()
    {
        return HandBuiltModel.Block(new ReturnStatement(H.Literal(1L), H.Int));
    }

    public Block Writes()
    {
        return HandBuiltModel.Block(
            HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(new FieldReferenceExpression(HandBuiltModel.Node(), Count), H.Literal(2L))),
            new ReturnStatement(H.Literal(1L), H.Int));
    }

    public Method Method(string name, Block? body, MemberModifiers modifiers = default, MemberType? declaring = null, params Parameter[] parameters)
    {
        return new Method(name, H.Int, [.. parameters], modifiers) { Body = body, DeclaringType = declaring ?? Owner };
    }

    public InvocationExpression Call(Method method, params Expression[] arguments)
    {
        Expression receiver = method.Modifiers.IsStatic
            ? new MethodReferenceExpression(HandBuiltModel.Node(), method)
            : new MemberAccessExpression(HandBuiltModel.Node(), This, method.Name, method.ReturnType, member: new MethodGroupResult([method]));
        return new InvocationExpression(HandBuiltModel.Node(), receiver, arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }

    public Property Property(PropertyAccessor? getter, PropertyAccessor? setter, MemberModifiers modifiers = default, MemberType? declaring = null)
    {
        return new Property("Peek", H.Int, modifiers) { Getter = getter, Setter = setter, DeclaringType = declaring ?? Owner };
    }

    public MemberAccessExpression Read(Property property)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(), This, property.Name, property.Type, member: new PropertyResult(property, Owner, []));
    }

    public AssignmentExpression Write(Property property)
    {
        return HandBuiltRefinements.Assign(Read(property), H.Literal(5L));
    }

    public IndexExpression Read(Indexer indexer)
    {
        return new IndexExpression(HandBuiltModel.Node(), This, H.Literal(0L), indexer.Type) { Indexer = indexer };
    }

    public Indexer Indexer(PropertyAccessor getter)
    {
        return new Indexer(H.Int, [new Parameter("slot", H.Int)], new MemberModifiers()) { Getter = getter, DeclaringType = Owner };
    }

    public static Parameter ByRef(IMistType type)
    {
        return new Parameter("slot", type, new ParameterModifier(false, true, default, ByRefDirection.Ref));
    }

    public static PropertyAccessor Accessor(Block? body = null, bool isAuto = false)
    {
        return new PropertyAccessor { Body = body, IsAuto = isAuto };
    }
}