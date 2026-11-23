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
        Pair = HandBuiltModel.Class("Pair");
        A = AddField(Pair, "A");
        B = AddField(Pair, "B");
        S = AddField(Pair, "S", new MemberModifiers { IsStatic = true });
        Span = HandBuiltModel.Struct("Span");
        Span.Modifiers = new MemberModifiers { IsRef = true };
    }

    public HandBuiltRefinements H { get; } = new();

    public RefinedType Five { get; }

    public ClassType Box { get; }

    public Field V { get; }

    public ClassType Pair { get; }

    public Field A { get; }

    public Field B { get; }

    public Field S { get; }

    public StructType Span { get; }

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

    public Method Viewing(IMistType returns, ByRefDirection direction = ByRefDirection.Ref, bool isReadonly = false)
    {
        var local = new LocalVariable("x", H.Int) { IsRef = true, Parameter = Parameter("x", H.Int, direction: direction) };
        return new Method("View", returns, [local.Parameter!], new MemberModifiers { IsReadonly = isReadonly }) { DeclaringType = Pair };
    }

    public Method Identity()
    {
        var local = ByRef("x");
        return new Method("Id", H.Int, [local.Parameter!], new MemberModifiers { IsRef = true })
        {
            Body = HandBuiltModel.Block(new ReturnStatement(Read(local), H.Int, isRef: true)),
            DeclaringType = Pair,
        };
    }

    public Method Returning(string name, Expression? returned, params Parameter[] parameters)
    {
        return new Method(name, H.Int, [.. parameters], new MemberModifiers { IsRef = true })
        {
            Body = returned is null ? null : HandBuiltModel.Block(new ReturnStatement(returned, H.Int, isRef: true)),
            DeclaringType = Pair,
        };
    }

    public Method Returning(string name, IReadOnlyList<Statement> before, Expression returned, params Parameter[] parameters)
    {
        return new Method(name, H.Int, [.. parameters], new MemberModifiers { IsRef = true })
        {
            Body = HandBuiltModel.Block([.. before, new ReturnStatement(returned, H.Int, isRef: true)]),
            DeclaringType = Pair,
        };
    }

    public static ExtensionBlock Extension(ClassType receiver)
    {
        return new ExtensionBlock("p", "", new Scope(null), null) { ResolvedReceiverType = receiver, Receiver = new LocalVariable("p", receiver) };
    }

    public static Method Extending(ExtensionBlock block, Method method)
    {
        return new Method(method.Name, method.ReturnType, [.. method.ParametersList], method.Modifiers) { Body = method.Body, DeclaringBlock = block };
    }

    public static InvocationExpression Implicit(Method method, params Expression[] arguments)
    {
        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), method), arguments, method.ReturnType, method, method.ParametersList.ConvertAll(parameter => parameter.Type));
    }

    public NewExpression Create()
    {
        return new NewExpression(HandBuiltModel.Node(), Pair, [], null, [], null);
    }

    public LocalVariable ByRef(string name, IMistType? type = null, ByRefDirection direction = ByRefDirection.Ref)
    {
        return new LocalVariable(name, type ?? H.Int) { IsRef = true, Parameter = Parameter(name, type ?? H.Int, direction: direction) };
    }

    public Field AddField(ClassType owner, string name, MemberModifiers? modifiers = null, IMistType? type = null)
    {
        var field = new Field(name, type ?? H.Int, default, modifiers ?? new MemberModifiers()) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }
}