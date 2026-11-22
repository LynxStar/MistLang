using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class FactHolderFragments
{
    public FactHolderFragments()
    {
        Five = H.Refine(H.Int, ComparisonKind.Equal, 5L, "Five");
        Box = HandBuiltModel.Class("Box");
        V = AddField(Box, "V");
        W = AddField(Box, "W");
        Pair = HandBuiltModel.Class("Pair");
        A = AddField(Pair, "A");
        B = AddField(Pair, "B");
    }

    public HandBuiltRefinements H { get; } = new();

    public RefinedType Five { get; }

    public ClassType Box { get; }

    public Field V { get; }

    public Field W { get; }

    public ClassType Pair { get; }

    public Field A { get; }

    public Field B { get; }

    public ThisExpression This => new(HandBuiltModel.Node(), Pair);

    public LiteralExpression Number(long value)
    {
        return H.Literal(value);
    }

    public static LocalVariable Int(HandBuiltRefinements h, string name, bool isRef = false)
    {
        return new LocalVariable(name, h.Int) { IsRef = isRef };
    }

    public static LocalReferenceExpression Read(LocalVariable local, int offset = 0)
    {
        return HandBuiltRefinements.Read(local, offset);
    }

    public static LocalDeclarationStatement Declare(LocalVariable local, Expression? value)
    {
        return new(local, value);
    }

    public static ExpressionStatement Set(Expression target, Expression value)
    {
        return HandBuiltRefinements.Evaluate(HandBuiltRefinements.Assign(target, value));
    }

    public LocalDeclarationStatement Need(Expression value)
    {
        return HandBuiltRefinements.Need(value, Five);
    }

    public LocalVariable Object(string name, OwnershipKind ownership)
    {
        return new LocalVariable(name, Box) { Ownership = ownership };
    }

    public LocalDeclarationStatement Created(LocalVariable local)
    {
        return new(local, new NewExpression(HandBuiltModel.Node(), Box, [], null, [], []));
    }

    public static MemberAccessExpression Member(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, field.DeclaringType ?? receiver.Type, []));
    }

    public static FieldReferenceExpression Own(Field field, int offset = 0)
    {
        return new(HandBuiltModel.Node(offset), field);
    }

    public Method Method(string name, ClassType declaring, Block? body, IMistType? returns = null, params Parameter[] parameters)
    {
        return new Method(name, returns ?? H.String, [.. parameters], new MemberModifiers()) { Body = body, DeclaringType = declaring };
    }

    public Method Writing(string name, ClassType declaring, params Field[] written)
    {
        return Method(name, declaring, HandBuiltModel.Block([.. written.Select(field => Set(Own(field), Number(1L)))]));
    }

    public static InvocationExpression Call(Expression receiver, Method method, params Expression[] arguments)
    {
        var target = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType, member: new MethodGroupResult([method]));
        return new InvocationExpression(HandBuiltModel.Node(), target, arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }

    public InvocationExpression Free(Method method, params Expression[] arguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(), H.Literal(0L), arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }

    public static Parameter Parameter(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted, ByRefDirection direction = ByRefDirection.None)
    {
        return new Parameter(name, type, new ParameterModifier(direction == ByRefDirection.In, direction != ByRefDirection.None, ownership, direction));
    }

    public Method Bump()
    {
        var local = ByRef("x");
        return Method("Bump", Pair, HandBuiltModel.Block(Set(Read(local), Number(1L))), H.Int, local.Parameter!);
    }

    public LocalVariable ByRef(string name)
    {
        return new LocalVariable(name, H.Int) { IsRef = true, Parameter = Parameter(name, H.Int, direction: ByRefDirection.Ref) };
    }

    public Method Referencing(string name, ClassType declaring, IMistType? returns = null)
    {
        return new Method(name, returns ?? H.Int, [], new MemberModifiers { IsRef = true }) { Body = HandBuiltModel.Block(), DeclaringType = declaring };
    }

    public LambdaExpression Closure(params Statement[] statements)
    {
        return new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(statements), false, new FunctionType([], H.Int));
    }

    public InvocationExpression Delegate()
    {
        return new InvocationExpression(HandBuiltModel.Node(), Number(0L), [], H.Int, null, []);
    }

    private Field AddField(ClassType owner, string name)
    {
        var field = new Field(name, H.Int, default, new MemberModifiers()) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }
}