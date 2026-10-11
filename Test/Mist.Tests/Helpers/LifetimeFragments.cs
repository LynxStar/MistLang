using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

/// <summary>
/// Bodies over a linear Token built by hand, for the lifetime plan, the cleanup it lowers to and the
/// exit drops analysis judges -- the three readers that must agree on one body.
/// </summary>
internal sealed class LifetimeFragments
{
    public LifetimeFragments()
    {
        Int32 = Model.Type("Int32");
        Boolean = Model.Type("Boolean");
        Void = Model.Type("Void");
        Token = Disciplined("Token", SubstructuralDiscipline.Linear);
        Shared = Disciplined("Shared", SubstructuralDiscipline.Relevant);
        Once = Disciplined("Once", SubstructuralDiscipline.Affine);
        Plain = Disciplined("Plain", SubstructuralDiscipline.None);
        Sink = HandBuiltModel.Class("Sink");
        Take = Static("Take", Void, new Parameter("t", Token));
        Lend = Static("Lend", Void, new Parameter("t", Token, new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted)));
        Pair = Static("Pair", Void, new Parameter("t", Token), new Parameter("n", Int32));
        Risky = Static("Risky", Int32);
        Make = Static("Make", Token);
        Peek = Static("Peek", Void, new Parameter("t", Token, new ParameterModifier(IsReadonly: false, IsRef: false, OwnershipKind.Frozen)));
        Flag = new LocalVariable("flag", Boolean);
        Box = HandBuiltModel.Class("Box");
        Use = new Method("Use", Void, [new Parameter("t", Token)], default) { DeclaringType = Box };
        Box.MethodsList.Add(Use);
        Holder = Disciplined("Holder", SubstructuralDiscipline.Linear, withDrop: false);
        First = new Field("First", Token, OwnershipKind.Unrestricted, default) { DeclaringType = Holder };
        Second = new Field("Second", Token, OwnershipKind.Unrestricted, default) { DeclaringType = Holder };
        Holder.FieldsList.AddRange([First, Second]);
        Lease = HandBuiltModel.Struct("Lease");
        Lease.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear, IsRef = true };
        Lease.MethodsList.Add(new Method("Drop", Void, [], default) { DeclaringType = Lease });
    }

    public HandBuiltModel Model { get; } = new();

    public ClassType Int32 { get; }

    public ClassType Boolean { get; }

    public ClassType Void { get; }

    public ClassType Token { get; }

    public ClassType Shared { get; }

    public ClassType Once { get; }

    public ClassType Plain { get; }

    public ClassType Sink { get; }

    public Method Take { get; }

    public Method Lend { get; }

    public Method Pair { get; }

    public Method Risky { get; }

    public Method Make { get; }

    public Method Peek { get; }

    public LocalVariable Flag { get; }

    public ClassType Box { get; }

    public Method Use { get; }

    public ClassType Holder { get; }

    public Field First { get; }

    public Field Second { get; }

    public StructType Lease { get; }

    public LocalVariable Local(string name, IMistType? type = null, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        return new LocalVariable(name, type ?? Token) { Ownership = ownership };
    }

    public static LocalDeclarationStatement Declare(LocalVariable local, Expression? initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    public NewExpression New(int id, ClassType? type = null)
    {
        var made = type ?? Token;
        return new NewExpression(HandBuiltModel.Node(), made, [HandBuiltModel.Literal(Int32, id)], made.ConstructorsList[0], [], [Int32]);
    }

    public static LocalReferenceExpression Ref(LocalVariable local)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), local);
    }

    public LocalReferenceExpression Condition()
    {
        return Ref(Flag);
    }

    public InvocationExpression Call(Method method, params Expression[] arguments)
    {
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), Sink), method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    public static InvocationExpression Drop(LocalVariable local)
    {
        var definition = (MemberType)(local.Type is ConstructedType constructed ? constructed.Definition : local.Type);
        var drop = definition.MethodsList.Single(method => method.Name == "Drop");
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), Ref(local), "Drop", drop.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), callee, [], drop.ReturnType, drop, []);
    }

    public static AssignmentExpression Assign(LocalVariable local, Expression value)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), Ref(local), value, local.Type);
    }

    public static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    public static ReturnStatement Return(Expression value)
    {
        return new ReturnStatement(value, value.Type);
    }

    public IfStatement If(Block then, Block? otherwise = null)
    {
        return new IfStatement(Condition(), then, otherwise);
    }

    public WhileStatement While(Block body)
    {
        return new WhileStatement(Condition(), body);
    }

    public Method Function(string name, Block body, params Parameter[] parameters)
    {
        return new Method(name, Void, [.. parameters], new MemberModifiers { IsStatic = true }) { DeclaringType = Sink, Body = body, DeclarationNode = HandBuiltModel.Node() };
    }

    public Method HolderDrop(Block body)
    {
        var drop = new Method("Drop", Void, [], default) { DeclaringType = Holder, Body = body, DeclarationNode = HandBuiltModel.Node() };
        Holder.MethodsList.Add(drop);
        return drop;
    }

    public static Parameter Owned(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Owned)
    {
        return new Parameter(name, type, new ParameterModifier(IsReadonly: false, IsRef: false, ownership));
    }

    public static LocalReferenceExpression Ref(Parameter parameter)
    {
        return Ref(new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter, Ownership = parameter.Modifier.Ownership, IsRef = parameter.Modifier.IsRef });
    }

    public static FieldReferenceExpression Ref(Field field)
    {
        return new FieldReferenceExpression(HandBuiltModel.Node(), field);
    }

    public static InvocationExpression DropOf(Expression held)
    {
        var definition = (MemberType)(held.Type is ConstructedType constructed ? constructed.Definition : held.Type);
        var drop = definition.MethodsList.Single(method => method.Name == "Drop");
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), held, "Drop", drop.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(), callee, [], drop.ReturnType, drop, []);
    }

    public InvocationExpression CallOn(Expression receiver, bool conditional, params Expression[] arguments)
    {
        var callee = new MemberAccessExpression(HandBuiltModel.Node(), receiver, Use.Name, Void, isConditional: conditional);
        return new InvocationExpression(HandBuiltModel.Node(), callee, arguments, Void, Use, [Token]);
    }

    public static AssignmentExpression Store(Field field, Expression value)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), Ref(field), value, field.Type);
    }

    public ForStatement For(LocalDeclarationStatement initializer, Block body)
    {
        return new ForStatement(initializer, Condition(), null, body);
    }

    public static WithBlockStatement With(Expression value, Block body, LocalVariable? binding = null)
    {
        return new WithBlockStatement(value, body, binding);
    }

    public IsPatternExpression Is(Expression scrutinee, LocalVariable binding)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), scrutinee, new TypeTestPattern(HandBuiltModel.Node(), binding.Type, binding), Boolean);
    }

    public TupleLiteralExpression Tuple(params Expression[] elements)
    {
        return new TupleLiteralExpression(
            HandBuiltModel.Node(), elements, [.. elements.Select(_ => (string?)null)], Model.Interner.GetOrCreateTupleType([.. elements.Select(element => element.Type)]));
    }

    public static string EmittedMethod(Method method, MemberType? owner = null)
    {
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitMethodBody(method, method.Body!, writer, new DiagnosticBag(), owner: owner);
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    public LambdaExpression Lambda(Block body)
    {
        return new LambdaExpression(HandBuiltModel.Node(), [], null, body, false, Model.Interner.GetOrCreateFunctionType([], Void));
    }

    public static string Emitted(Block body)
    {
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitBlock(body, writer, new DiagnosticBag());
        return writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private ClassType Disciplined(string name, SubstructuralDiscipline discipline, bool withDrop = true)
    {
        var type = HandBuiltModel.Class(name);
        type.Modifiers = new MemberModifiers { Discipline = discipline };

        if(withDrop)
        {
            type.MethodsList.Add(new Method("Drop", Void, [], default) { DeclaringType = type });
        }

        type.ConstructorsList.Add(new Constructor(name, type, [new Parameter("id", Int32)], default) { DeclaringType = type });
        return type;
    }

    private Method Static(string name, IMistType returns, params Parameter[] parameters)
    {
        var method = new Method(name, returns, [.. parameters], new MemberModifiers { IsStatic = true }) { DeclaringType = Sink };
        Sink.MethodsList.Add(method);
        return method;
    }
}