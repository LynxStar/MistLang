using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class FrameFragments
{
    public FrameFragments()
    {
        Span = Model.Interner.GetOrCreateConstructedType(SpanDefinition, [Integer]);
        Data = new Field("Data", Span, OwnershipKind.Unrestricted, default) { DeclaringType = Holder };
        Holder.FieldsList.Add(Data);
        Construct = new Constructor("Holder", Holder, [new Parameter("data", Span)], default) { DeclaringType = Holder };
        Put = new Method("Put", Model.Type("Void"), [new Parameter("s", Span)], default) { DeclaringType = Holder };
        Look = new Method("Look", Model.Type("Void"), [new Parameter("s", Span)], new MemberModifiers { IsReadonly = true }) { DeclaringType = Holder };
        Value = new Field("Value", Integer, OwnershipKind.Unrestricted, default) { DeclaringType = Box };
        Shared = new Field("Shared", Integer, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true }) { DeclaringType = Box };
        Point.FieldsList.Add(new Field("X", Integer, OwnershipKind.Unrestricted, default) { DeclaringType = Point });
    }

    public HandBuiltModel Model { get; } = new();

    public IMistType Integer => Model.Type("Int32");

    public StructType SpanDefinition { get; } = new("Span<,>", "Standard", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };

    public IMistType Span { get; }

    public StructType Holder { get; } = new("Holder", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };

    public Field Data { get; }

    public Constructor Construct { get; }

    public Method Put { get; }

    public Method Look { get; }

    public ClassType Box { get; } = HandBuiltModel.Class("Box");

    public Field Value { get; }

    public Field Shared { get; }

    public StructType Point { get; } = HandBuiltModel.Struct("Point");

    public static LocalVariable Local(string name, IMistType type, bool isRef = false)
    {
        return new LocalVariable(name, type) { IsRef = isRef };
    }

    public static LocalVariable Parameter(string name, IMistType type, bool byRef = false)
    {
        var parameter = new Parameter(name, type, new ParameterModifier(false, byRef, OwnershipKind.Unrestricted, byRef ? ByRefDirection.Ref : ByRefDirection.None));
        return new LocalVariable(name, type) { IsRef = byRef, Parameter = parameter };
    }

    public static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    public static LocalDeclarationStatement Declare(LocalVariable local, Expression? initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    public static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    public StackallocExpression Stackalloc(int offset = 0)
    {
        return new StackallocExpression(HandBuiltModel.Node(offset), Integer, HandBuiltModel.Literal(Integer, 3L), Span);
    }

    public IMistType Array => Model.Interner.GetOrCreateConstructedType(Model.Type("Array<,>"), [Integer]);

    public NewExpression NewHolder(Expression data, int offset = 0)
    {
        return new NewExpression(HandBuiltModel.Node(offset), Holder, [data], Construct, [], [Span]);
    }

    public static MemberAccessExpression Member(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    public IndexExpression Element(Expression receiver, int offset = 0)
    {
        return new IndexExpression(HandBuiltModel.Node(offset), receiver, HandBuiltModel.Literal(Integer, 0L), Integer);
    }

    public static InvocationExpression Call(Method method, Expression? receiver, int offset, params Expression[] arguments)
    {
        var callee = new MemberAccessExpression(HandBuiltModel.Node(offset), receiver ?? HandBuiltModel.Literal(method.ReturnType), method.Name, method.ReturnType);
        return new InvocationExpression(
            HandBuiltModel.Node(offset), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    public static AssignmentExpression Assign(Expression target, Expression value, int offset = 0)
    {
        return new AssignmentExpression(HandBuiltModel.Node(offset), target, value, target.Type);
    }

    public static ReturnStatement Return(Expression value, bool isRef = false)
    {
        return new ReturnStatement(value, value.Type, isRef);
    }

    public Method RefReturning(string name, params Parameter[] parameters)
    {
        return new Method(name, Integer, [.. parameters], new MemberModifiers { IsRef = true, IsStatic = true });
    }
}