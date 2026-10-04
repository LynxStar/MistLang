using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
        Flag = new LocalVariable("flag", Boolean);
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

    public LocalVariable Flag { get; }

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

    private ClassType Disciplined(string name, SubstructuralDiscipline discipline)
    {
        var type = HandBuiltModel.Class(name);
        type.Modifiers = new MemberModifiers { Discipline = discipline };
        type.MethodsList.Add(new Method("Drop", Void, [], default) { DeclaringType = type });
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