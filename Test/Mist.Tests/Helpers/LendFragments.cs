using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class LendFragments
{
    public LendFragments()
    {
        V = Member(Box, "V", Int32);
        S = Member(Hook, "S", Box, isStatic: true);
        Name = Member(Hook, "Name", Model.Type("String"), isStatic: true);
        In = Member(Holder, "In", Box);
        Kept = Member(Holder, "Kept", Box, OwnershipKind.Owned);
        Shared = Member(Hook, "Shared", Holder, isStatic: true);
        Add = new Method("Add", Model.Type("Void"), [new Parameter("item", Box)], default) { DeclaringType = Boxes };
    }

    public HandBuiltModel Model { get; } = new();

    public ClassType Box { get; } = HandBuiltModel.Class("Box");

    public ClassType Hook { get; } = HandBuiltModel.Class("Hook");

    public ClassType Holder { get; } = HandBuiltModel.Class("P");

    public ClassType Boxes { get; } = HandBuiltModel.Class("Boxes");

    public ClassType Int32 => Model.Type("Int32");

    public Field V { get; }

    public Field S { get; }

    public Field Name { get; }

    public Field In { get; }

    public Field Kept { get; }

    public Field Shared { get; }

    public Method Add { get; }

    public static LocalVariable Parameter(string name, IMistType type, ParameterModifier modifier)
    {
        return new LocalVariable(name, type)
        {
            Parameter = new Parameter(name, type, modifier),
            Ownership = modifier.Ownership,
            IsRef = modifier.IsRef,
            IsReadonly = modifier.IsReadonly,
        };
    }

    public static ParameterModifier Tier(string tier)
    {
        return tier switch
        {
            "borrowed" => new ParameterModifier(false, false, OwnershipKind.Borrowed),
            "frozen" => new ParameterModifier(false, false, OwnershipKind.Frozen),
            "owned" => new ParameterModifier(false, false, OwnershipKind.Owned),
            "readonly ref" => new ParameterModifier(true, true, OwnershipKind.Unrestricted),
            "ref" => new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref),
            "out" => new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Out),
            _ => default,
        };
    }

    public static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    public static MemberAccessExpression Static(Field field, int offset = 0)
    {
        return Access(new TypeReferenceExpression(HandBuiltModel.Node(), field.DeclaringType!), field, offset);
    }

    public static MemberAccessExpression Access(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, field.DeclaringType!, []));
    }

    public IndexExpression Element(Expression array, int offset = 0)
    {
        return new IndexExpression(HandBuiltModel.Node(offset), array, HandBuiltModel.Literal(Int32, 0L), Box);
    }

    public ClassType BoxArray => Model.Type("Box[]");

    public static InvocationExpression Call(Method method, Expression? receiver = null, int offset = 0, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new FunctionReferenceExpression(HandBuiltModel.Node(offset), null, method, method.ReturnType)
            : new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(offset), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    public static LocalFunctionStatement Local(string name, IMistType returns, Block body, params LocalVariable[] parameters)
    {
        return Local(name, returns, body, isStatic: true, parameters);
    }

    public static LocalFunctionStatement Local(string name, IMistType returns, Block body, bool isStatic, params LocalVariable[] parameters)
    {
        var function = new Method(name, returns, [.. parameters.Select(parameter => parameter.Parameter!)], new MemberModifiers { IsStatic = isStatic }) { Body = body };
        return new LocalFunctionStatement(function, parameters);
    }

    public LambdaExpression Lambda(Expression result, bool isStatic = false)
    {
        return new LambdaExpression(HandBuiltModel.Node(), [], result, null, isStatic, Model.Interner.GetOrCreateFunctionType([], result.Type));
    }

    public static Constructor Chained(ClassType type, Constructor target, LocalVariable[] parameters, params Expression[] arguments)
    {
        var constructor = new Constructor(type.Name, type, [.. parameters.Select(parameter => parameter.Parameter!)], default) { DeclaringType = type, ChainedConstructor = target };
        constructor.ChainArgumentsList.AddRange(arguments);
        return constructor;
    }

    public static NewExpression Fresh(ClassType type, Constructor? constructor = null, int offset = 0, params Expression[] arguments)
    {
        return new NewExpression(HandBuiltModel.Node(offset), type, arguments, constructor, [], [.. (constructor?.ParametersList ?? []).Select(parameter => parameter.Type)]);
    }

    public static NewExpression Leaky(int offset = 0)
    {
        var leaky = HandBuiltModel.Class("Leaky");
        var last = Member(leaky, "Last", leaky, isStatic: true);
        var keeps = new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), last), new ThisExpression(HandBuiltModel.Node(), leaky), leaky);
        var constructor = new Constructor("Leaky", leaky, [], default) { Body = HandBuiltModel.Block(new ExpressionStatement(keeps)), DeclaringType = leaky };
        return Fresh(leaky, constructor, offset);
    }

    public static LocalDeclarationStatement Declare(LocalVariable local, Expression value)
    {
        return new LocalDeclarationStatement(local, value);
    }

    public static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    public static AssignmentExpression Store(Expression target, Expression value)
    {
        return new AssignmentExpression(HandBuiltModel.Node(), target, value, target.Type);
    }

    public IReadOnlyList<Diagnostic> Analyze(IProgramAnalyzer analyzer, object root, MistType? owner = null, object? member = null)
    {
        return Model.Analyze(analyzer, root, owner: owner, member: member);
    }

    public static Field Member(ClassType owner, string name, IMistType type, OwnershipKind ownership = default, bool isStatic = false)
    {
        var field = new Field(name, type, ownership, new MemberModifiers { IsStatic = isStatic }) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }
}