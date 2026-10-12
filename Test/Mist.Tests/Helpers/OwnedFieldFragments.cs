using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

/// <summary>
/// Bodies over an owned Pair of two owned Engine fields, built by hand: per-field moves, leases and
/// restorations with the composite restrictions they put on the whole, `this` lent to a parameter, and
/// an owned field read through a shared holder.
/// </summary>
internal sealed class OwnedFieldFragments
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType engine = HandBuiltModel.Class("Engine");
    private readonly ClassType pair = HandBuiltModel.Class("Pair");
    private readonly ClassType integer;
    private readonly ClassType unit;
    private readonly Field n;
    private readonly Field left;
    private readonly Field right;
    private readonly Field name;
    private readonly Constructor engineConstructor;
    private readonly Method readLeft;
    private readonly Method readRight;
    private readonly Method writeLeft;
    private readonly Method consume;
    private readonly Method show;
    private readonly Method takeEngine;
    private readonly LocalVariable p;

    public OwnedFieldFragments()
    {
        integer = model.Type("Int32");
        unit = model.Type("Void");
        n = Field(engine, "N", integer);
        engineConstructor = new Constructor("Engine", engine, [], default) { DeclaringType = engine, Body = HandBuiltModel.Block() };
        engine.ConstructorsList.Add(engineConstructor);
        engine.MethodsList.Add(new Method("Clone", engine, [], new MemberModifiers { IsOwned = true }) { DeclaringType = engine });
        left = Field(pair, "Left", engine, OwnershipKind.Owned);
        right = Field(pair, "Right", engine, OwnershipKind.Owned);
        name = Field(pair, "Name", model.Type("String"), OwnershipKind.Owned);
        _ = Field(pair, "Count", integer);
        pair.ConstructorsList.Add(new Constructor("Pair", pair, [], default) { DeclaringType = pair, Body = HandBuiltModel.Block() });
        readLeft = Method(pair, "ReadLeft", integer, DeclarationFragments.Returning(Access(Access(This(pair), left), n)));
        readRight = Method(pair, "ReadRight", integer, DeclarationFragments.Returning(Access(new FieldReferenceExpression(Node(), right), n)));
        writeLeft = Method(pair, "WriteLeft", unit, HandBuiltModel.Block(new ExpressionStatement(Assign(Access(This(pair), left), Fresh()))));
        consume = Static("Consume", new Parameter("p", pair, new ParameterModifier(false, false, OwnershipKind.Owned)));
        show = Static("Show", new Parameter("p", pair));
        takeEngine = Static("TakeEngine", new Parameter("e", engine, new ParameterModifier(false, false, OwnershipKind.Owned)));
        p = new LocalVariable("p", pair) { Ownership = OwnershipKind.Owned };
    }

    public IReadOnlyList<Diagnostic> Analyze(string fragment)
    {
        if(fragment.StartsWith("this-lend-", StringComparison.Ordinal))
        {
            return ThisLend(fragment);
        }

        if(fragment.StartsWith("shared-extract-", StringComparison.Ordinal))
        {
            return SharedExtract(fragment);
        }

        var statements = new List<Statement> { Declare(p, new NewExpression(Node(), pair, [], pair.ConstructorsList[0], [], [])) };

        switch(fragment)
        {
            case "owned-field-move-then-read":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-move-then-sibling-read":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Declare(new LocalVariable("k", integer), Access(Access(Ref(p), right), n)));
                break;
            case "owned-field-move-then-whole-move":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(Call(consume, Ref(p, 37))));
                break;
            case "owned-field-move-then-whole-read":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(Call(show, Ref(p, 37))));
                break;
            case "owned-field-move-then-restore-then-whole-move":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(Assign(Access(Ref(p), left), Fresh())));
                statements.Add(Run(Call(consume, Ref(p))));
                break;
            case "owned-field-move-then-method-reading-it":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(CallOn(readLeft, Ref(p), 37)));
                break;
            case "owned-field-move-then-method-reading-sibling":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(CallOn(readRight, Ref(p))));
                break;
            case "owned-field-move-then-method-writing-it":
                statements.Add(Declare(Owned("taken"), Access(Ref(p), left)));
                statements.Add(Run(CallOn(writeLeft, Ref(p), 37)));
                statements.Add(ReadLeftN(0));
                break;
            case "owned-field-move-into-owned-parameter-then-read":
                statements.Add(Run(Call(takeEngine, Access(Ref(p), left))));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-frozen-then-write":
                statements.Add(Declare(Frozen("f", engine), Access(Ref(p), left)));
                statements.Add(Run(Assign(Access(Ref(p), left, 37), Fresh())));
                break;
            case "owned-field-frozen-then-sibling-write":
                statements.Add(Declare(Frozen("f", engine), Access(Ref(p), left)));
                statements.Add(Run(Assign(Access(Ref(p), right), Fresh())));
                break;
            case "owned-field-frozen-then-whole-move":
                statements.Add(Declare(Frozen("f", engine), Access(Ref(p), left)));
                statements.Add(Declare(Owned("q", pair), Ref(p, 37)));
                break;
            case "owned-field-frozen-then-whole-freeze":
                statements.Add(Declare(Frozen("f", engine), Access(Ref(p), left)));
                statements.Add(Declare(Frozen("g", pair), Ref(p)));
                break;
            case "owned-field-frozen-lease-ends-then-write":
                statements.Add(HandBuiltModel.Block(Declare(Frozen("f", engine), Access(Ref(p), left))));
                statements.Add(Run(Assign(Access(Ref(p), left), Fresh())));
                break;
            case "owned-field-borrowed-then-read":
                statements.Add(Declare(new LocalVariable("b", engine) { Ownership = OwnershipKind.Borrowed }, Access(Ref(p), left)));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-borrowed-then-sibling-read":
                statements.Add(Declare(new LocalVariable("b", engine) { Ownership = OwnershipKind.Borrowed }, Access(Ref(p), left)));
                statements.Add(Declare(new LocalVariable("k", integer), Access(Access(Ref(p), right), n)));
                break;
            case "owned-field-moved-in-one-branch-then-read":
                statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(Run(Call(takeEngine, Access(Ref(p), left)))), null));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-moved-inside-loop":
                statements.Add(new WhileStatement(Condition(), HandBuiltModel.Block(Run(Call(takeEngine, Access(Ref(p), left, 37))))));
                break;
            case "owned-field-moved-in-loop-and-restored":
                statements.Add(new WhileStatement(Condition(), HandBuiltModel.Block(
                    Run(Call(takeEngine, Access(Ref(p), left))),
                    Run(Assign(Access(Ref(p), left), Fresh())))));
                break;
            case "owned-field-moved-while-holder-frozen":
                statements.Add(Declare(Frozen("f", pair), Ref(p)));
                statements.Add(Declare(Owned("t"), Access(Ref(p), left, 37)));
                break;
            case "owned-field-read-into-unrestricted-then-read":
                statements.Add(Declare(new LocalVariable("alias", engine), Access(Ref(p), left)));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-cloned-into-owned-then-read":
                statements.Add(Declare(Owned("c"), new CloneExpression(Node(), Access(Ref(p), left))));
                statements.Add(ReadLeftN(37));
                break;
            case "owned-field-readonly-ref-then-move":
                statements.Add(Declare(new LocalVariable("r", engine) { IsRef = true, IsReadonly = true }, Access(Ref(p), left)));
                statements.Add(Declare(Owned("m"), Access(Ref(p), left, 37)));
                break;
            case "owned-string-field-read-then-read":
                statements.Add(Declare(new LocalVariable("s", name.Type), Access(Ref(p), name)));
                statements.Add(Declare(new LocalVariable("t", name.Type), Access(Ref(p), name, 37)));
                break;
            default:
                throw new ArgumentException($"no owned-field fragment named '{fragment}'", nameof(fragment));
        }

        return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements]));
    }

    private IReadOnlyList<Diagnostic> ThisLend(string fragment)
    {
        ConcreteType cell = fragment == "this-lend-struct" ? HandBuiltModel.Struct("Cell") : HandBuiltModel.Class("Cell");
        var borrow = Static("Borrow", new Parameter("x", cell, new ParameterModifier(false, false, OwnershipKind.Borrowed)));
        var freeze = Static("Freeze", new Parameter("x", cell, new ParameterModifier(false, false, OwnershipKind.Frozen)));
        var callee = fragment == "this-lend-readonly-ref-to-frozen" ? freeze : borrow;
        var body = HandBuiltModel.Block(Run(Call(callee, new ThisExpression(Node(37), cell))));
        object member = fragment == "this-lend-in-constructor"
            ? new Constructor("Cell", cell, [], default) { DeclaringType = cell, Body = body }
            : new Method("Lend", unit, [], default)
            {
                DeclaringType = cell,
                Body = body,
                DeclarationNode = Node(),
                DeclaredThisTier = fragment switch
                {
                    "this-lend-frozen-to-borrowed" or "this-lend-struct" => ThisTier.Frozen,
                    "this-lend-readonly-ref-to-frozen" => ThisTier.ReadonlyRef,
                    "this-lend-with-borrowed" => ThisTier.Borrowed,
                    _ => null,
                },
            };

        return model.Analyze(new TierAnalyzer(), body, owner: cell, member: member);
    }

    private IReadOnlyList<Diagnostic> SharedExtract(string fragment)
    {
        var car = new LocalVariable("car", pair);
        var statements = new List<Statement> { Declare(car, new NewExpression(Node(), pair, [], pair.ConstructorsList[0], [], [])) };

        switch(fragment)
        {
            case "shared-extract-frozen":
                statements.Add(Declare(Frozen("f", engine), Access(Ref(car), left, 37)));
                break;
            case "shared-extract-unrestricted-nonclonable":
                statements.Add(Declare(new LocalVariable("f", engine), Access(Ref(car), left, 37)));
                break;
            case "shared-extract-unrestricted-string":
                statements.Add(Declare(new LocalVariable("s", name.Type), Access(Ref(car), name, 37)));
                break;
            case "shared-extract-into-owned-parameter":
                statements.Add(Run(Call(takeEngine, Access(Ref(car), left, 37))));
                break;
            case "shared-extract-cloned":
                statements.Add(Declare(new LocalVariable("f", engine), new CloneExpression(Node(), Access(Ref(car), left, 37))));
                break;
            case "shared-extract-by-assignment":
                var f = new LocalVariable("f", engine);
                statements.Add(Declare(f, Fresh()));
                statements.Add(Run(Assign(Ref(f), Access(Ref(car), left, 37))));
                break;
            default:
                throw new ArgumentException($"no shared-extract fragment named '{fragment}'", nameof(fragment));
        }

        return model.Analyze(new TierAnalyzer(), HandBuiltModel.Block([.. statements]));
    }

    private LocalDeclarationStatement ReadLeftN(int offset)
    {
        return Declare(new LocalVariable("k", integer), Access(Access(Ref(p), left, offset), n));
    }

    private LocalVariable Owned(string local, IMistType? type = null)
    {
        return new LocalVariable(local, type ?? engine) { Ownership = OwnershipKind.Owned };
    }

    private static LocalVariable Frozen(string local, IMistType type)
    {
        return new LocalVariable(local, type) { Ownership = OwnershipKind.Frozen };
    }

    private NewExpression Fresh()
    {
        return new NewExpression(Node(), engine, [], engineConstructor, [], []);
    }

    private Expression Condition()
    {
        return HandBuiltModel.Literal(model.Type("Boolean"));
    }

    private Method Static(string method, params Parameter[] parameters)
    {
        return new Method(method, unit, [.. parameters], new MemberModifiers { IsStatic = true });
    }

    private static Method Method(ConcreteType owner, string method, IMistType returns, Block body)
    {
        var declared = new Method(method, returns, [], default) { DeclaringType = owner, Body = body };
        owner.MethodsList.Add(declared);
        return declared;
    }

    private static Field Field(ConcreteType owner, string field, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var declared = new Field(field, type, ownership, default) { DeclaringType = owner };
        owner.FieldsList.Add(declared);
        return declared;
    }

    private static LocalDeclarationStatement Declare(LocalVariable local, Expression initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private static MemberAccessExpression Access(Expression receiver, Field field, int offset = 0)
    {
        return new MemberAccessExpression(Node(offset), receiver, field.Name, field.Type, member: new FieldResult(field, receiver.Type, []));
    }

    private static ThisExpression This(ConcreteType type)
    {
        return new ThisExpression(Node(), type);
    }

    private static InvocationExpression Call(Method method, params Expression[] arguments)
    {
        var callee = new MethodReferenceExpression(Node(), method);
        return new InvocationExpression(Node(), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression CallOn(Method method, Expression receiver, int offset = 0)
    {
        var callee = new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(offset), callee, [], method.ReturnType, method, []);
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(Node(offset), local);
    }

    private static Storm.Runtime.SyntaxNode Node(int offset = 0)
    {
        return HandBuiltModel.Node(offset);
    }
}