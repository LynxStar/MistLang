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
/// Bodies built by hand over an owned Cell handed to a call -- the ordinary parameter of a static or
/// instance method, a constructor, a local function, a function value or a wrapper, against a frozen,
/// borrowed or readonly ref one -- followed by the owned claim the handoff forbids: a return, a binding,
/// a parameter, a receiver or a field, on one path, in a loop, or after a fresh value replaced what was
/// handed away.
/// </summary>
internal sealed class CallBoundaryFragments
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType cell = HandBuiltModel.Class("Cell");
    private readonly ClassType pair = HandBuiltModel.Class("Pair");
    private readonly ClassType sink = HandBuiltModel.Class("Sink");
    private readonly ClassType probe = HandBuiltModel.Class("Probe");
    private readonly ClassType integer;
    private readonly ClassType text;
    private readonly ClassType unit;
    private readonly Field n;
    private readonly Field left;
    private readonly Field right;
    private readonly Field kept;
    private readonly Constructor cellConstructor;
    private readonly Constructor pairConstructor;
    private readonly Constructor emptySink;
    private readonly Constructor holdingSink;
    private readonly Method keep;
    private readonly Method bump;
    private readonly Method count;
    private readonly Method print;
    private readonly Method identity;
    private readonly Method wrap;
    private readonly Method freeze;
    private readonly Method borrow;
    private readonly Method view;
    private readonly Method take;
    private readonly Method attach;
    private readonly Method close;

    public CallBoundaryFragments()
    {
        integer = model.Type("Int32");
        text = model.Type("String");
        unit = model.Type("Void");
        n = Field(cell, "N", integer);
        left = Field(pair, "Left", cell, OwnershipKind.Owned);
        right = Field(pair, "Right", cell, OwnershipKind.Owned);
        kept = Field(sink, "Kept", cell, OwnershipKind.Owned);
        cellConstructor = Constructor(cell);
        pairConstructor = Constructor(pair);
        emptySink = Constructor(sink);
        holdingSink = Constructor(sink, new Parameter("c", cell));
        close = new Method("Close", unit, [], default) { DeclaringType = cell, DeclaredThisTier = ThisTier.Owned };
        cell.MethodsList.Add(close);
        attach = new Method("Attach", unit, [new Parameter("c", cell)], default) { DeclaringType = sink, Body = HandBuiltModel.Block() };
        sink.MethodsList.Add(attach);
        keep = Static("Keep", unit, null, new Parameter("c", cell));
        bump = Static("Bump", unit, null, new Parameter("c", cell));
        count = Static("Count", unit, null, new Parameter("n", integer));
        print = Static("Print", unit, null, new Parameter("s", text));
        identity = Static("Id", cell, null, new Parameter("c", cell));
        freeze = Static("Freeze", unit, null, new Parameter("c", cell, new ParameterModifier(false, false, OwnershipKind.Frozen)));
        borrow = Static("Borrow", unit, null, new Parameter("c", cell, new ParameterModifier(false, false, OwnershipKind.Borrowed)));
        view = Static("View", unit, null, new Parameter("c", cell, new ParameterModifier(IsReadonly: true, IsRef: true, OwnershipKind.Unrestricted)));
        take = Static("Take", unit, null, new Parameter("c", cell, new ParameterModifier(false, false, OwnershipKind.Owned)));
        var x = new LocalVariable("x", cell) { Parameter = new Parameter("x", cell) };
        wrap = Static("Wrap", unit, HandBuiltModel.Block(Run(Call(keep, null, 0, Ref(x)))), x.Parameter!);
    }

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        var c = Owned("c", cell);
        var p = Owned("p", pair);
        var s = new LocalVariable("s", sink);
        var fresh = Declare(c, New(cell, cellConstructor));
        var handed = Run(Call(keep, null, 0, Ref(c)));
        var replaced = Run(Assign(Ref(c), New(cell, cellConstructor)));

        switch(name)
        {
            case "handed-to-a-static-method-then-returned-owned":
                return Returns(cell, fresh, handed, Return(Ref(c, 37)));
            case "handed-to-an-instance-method-then-returned-owned":
                return Returns(cell, fresh, Declare(s, New(sink, emptySink)), Run(Call(attach, Ref(s), 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-to-a-constructor-then-returned-owned":
                return Returns(cell, fresh, Declare(s, New(sink, holdingSink, Ref(c))), Return(Ref(c, 37)));
            case "handed-to-a-local-function-then-returned-owned":
                var held = new LocalVariable("held", cell) { Parameter = new Parameter("held", cell) };
                var hold = new Method("Hold", unit, [held.Parameter!], default) { Body = HandBuiltModel.Block() };
                return Returns(cell, fresh, new LocalFunctionStatement(hold, [held]), Run(Call(hold, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-to-a-function-value-then-returned-owned":
                var f = new LocalVariable("f", model.Interner.GetOrCreateFunctionType([cell], unit));
                var applied = new InvocationExpression(Node(), Ref(f), [Ref(c)], unit, null, null);
                return Returns(cell, fresh, Declare(f, HandBuiltModel.Literal(f.Type)), Run(applied), Return(Ref(c, 37)));
            case "handed-through-a-wrapper-then-returned-owned":
                return Returns(cell, fresh, Run(Call(wrap, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-then-result-aliased-then-returned-owned":
                return Returns(cell, fresh, Declare(new LocalVariable("alias", cell), Call(identity, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-on-one-branch-then-returned-owned":
                return Returns(cell, fresh, If(HandBuiltModel.Block(handed)), Return(Ref(c, 37)));
            case "handed-in-a-loop-then-returned-owned":
                return Returns(cell, fresh, While(HandBuiltModel.Block(handed)), Return(Ref(c, 37)));
            case "handed-then-mutated-then-returned-owned":
                return Returns(cell, fresh, handed, Run(Assign(Access(Ref(c), n), HandBuiltModel.Literal(integer, 1L))), Return(Ref(c, 37)));
            case "handed-owned-field-then-holder-returned-owned":
                return Returns(pair, Declare(p, New(pair, pairConstructor)), Run(Call(keep, null, 0, Access(Ref(p), left))), Return(Ref(p, 37)));
            case "handed-owned-field-then-field-returned-owned":
                return Returns(cell, Declare(p, New(pair, pairConstructor)), Run(Call(keep, null, 0, Access(Ref(p), left))), Return(Access(Ref(p), left, 37)));
            case "handed-whole-then-owned-field-returned-owned":
                return Returns(cell, Declare(p, New(pair, pairConstructor)), Run(Call(keep, null, 0, Ref(p))), Return(Access(Ref(p), left, 37)));
            case "handed-then-moved-into-an-owned-binding":
                return Body(fresh, handed, Declare(Owned("d", cell), Ref(c, 37)));
            case "handed-then-moved-into-an-owned-parameter":
                return Body(fresh, handed, Run(Call(take, null, 0, Ref(c, 37))));
            case "handed-then-consumed-as-an-owned-receiver":
                return Body(fresh, handed, Run(Call(close, Ref(c, 37), 0)));
            case "handed-then-moved-into-an-owned-field":
                return Body(fresh, Declare(s, New(sink, emptySink)), handed, Run(Assign(Access(Ref(s), kept), Ref(c, 37))));
            case "handed-owned-parameter-then-moved-into-an-owned-binding":
                var given = Owned("c", cell, parameter: true);
                var method = Static("M", unit, HandBuiltModel.Block(Run(Call(keep, null, 0, Ref(given))), Declare(Owned("d", cell), Ref(given, 37))), given.Parameter!);
                return model.Analyze(new OwnershipAnalyzer(), method.Body, owner: probe, member: method);
            case "handed-to-a-frozen-parameter-then-returned-owned":
                return Returns(cell, fresh, Run(Call(freeze, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-to-a-borrowed-parameter-then-returned-owned":
                return Returns(cell, fresh, Run(Call(borrow, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-to-a-readonly-ref-parameter-then-returned-owned":
                return Returns(cell, fresh, Run(Call(view, null, 0, Ref(c))), Return(Ref(c, 37)));
            case "handed-then-replaced-then-returned-owned":
                return Returns(cell, fresh, handed, replaced, Return(Ref(c, 37)));
            case "handed-on-one-branch-and-replaced-there-then-returned-owned":
                return Returns(cell, fresh, If(HandBuiltModel.Block(handed, replaced)), Return(Ref(c, 37)));
            case "handed-in-a-loop-and-replaced-then-returned-owned":
                return Returns(cell, fresh, While(HandBuiltModel.Block(handed, replaced)), Return(Ref(c, 37)));
            case "handed-then-used-plainly":
                return Body(fresh, handed, Run(Assign(Access(Ref(c), n), HandBuiltModel.Literal(integer, 1L))), Run(Call(bump, null, 0, Ref(c))));
            case "handed-value-field-then-returned-owned":
                return Returns(cell, fresh, Run(Call(count, null, 0, Access(Ref(c), n))), Return(Ref(c, 37)));
            case "handed-string-then-moved-into-an-owned-binding":
                var word = Owned("word", text);
                return Body(Declare(word, HandBuiltModel.Literal(text, "word")), Run(Call(print, null, 0, Ref(word))), Declare(Owned("copy", text), Ref(word, 37)));
            case "handed-owned-field-then-sibling-returned-owned":
                return Returns(cell, Declare(p, New(pair, pairConstructor)), Run(Call(keep, null, 0, Access(Ref(p), left))), Return(Access(Ref(p), right, 37)));
            case "handed-owned-field-then-restored-then-holder-returned-owned":
                return Returns(
                    pair,
                    Declare(p, New(pair, pairConstructor)),
                    Run(Call(keep, null, 0, Access(Ref(p), left))),
                    Run(Assign(Access(Ref(p), left), New(cell, cellConstructor))),
                    Return(Ref(p, 37)));
            default:
                throw new ArgumentException($"no call-boundary fragment named '{name}'", nameof(name));
        }
    }

    private IReadOnlyList<Diagnostic> Returns(IMistType returned, params Statement[] statements)
    {
        var make = new Method("Make", returned, [], new MemberModifiers { IsStatic = true })
        {
            DeclaringType = probe,
            ReturnOwnership = OwnershipKind.Owned,
            Body = HandBuiltModel.Block(statements),
        };
        probe.MethodsList.Add(make);
        return model.Analyze(new OwnershipAnalyzer(), make.Body, owner: probe, member: make);
    }

    private IReadOnlyList<Diagnostic> Body(params Statement[] statements)
    {
        return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block(statements));
    }

    private IfStatement If(Block then)
    {
        return new IfStatement(Condition(), then, null);
    }

    private WhileStatement While(Block body)
    {
        return new WhileStatement(Condition(), body);
    }

    private Expression Condition()
    {
        return HandBuiltModel.Literal(model.Type("Boolean"));
    }

    private Method Static(string name, IMistType returns, Block? body, params Parameter[] parameters)
    {
        var method = new Method(name, returns, [.. parameters], new MemberModifiers { IsStatic = true }) { DeclaringType = probe, Body = body };
        probe.MethodsList.Add(method);
        return method;
    }

    private static Constructor Constructor(ClassType owner, params Parameter[] parameters)
    {
        var constructor = new Constructor(owner.Name, owner, [.. parameters], default) { DeclaringType = owner, Body = HandBuiltModel.Block() };
        owner.ConstructorsList.Add(constructor);
        return constructor;
    }

    private static Field Field(ClassType owner, string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var field = new Field(name, type, ownership, default) { DeclaringType = owner };
        owner.FieldsList.Add(field);
        return field;
    }

    private static LocalVariable Owned(string name, IMistType type, bool parameter = false)
    {
        return new LocalVariable(name, type)
        {
            Ownership = OwnershipKind.Owned,
            Parameter = parameter ? new Parameter(name, type, new ParameterModifier(false, false, OwnershipKind.Owned)) : null,
        };
    }

    private static NewExpression New(ClassType type, Constructor constructor, params Expression[] arguments)
    {
        return new NewExpression(Node(), type, arguments, constructor, [], [.. constructor.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static InvocationExpression Call(Method method, Expression? receiver, int offset, params Expression[] arguments)
    {
        Expression callee = receiver is null
            ? new MethodReferenceExpression(Node(), method)
            : new MemberAccessExpression(Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(Node(offset), callee, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private static LocalDeclarationStatement Declare(LocalVariable local, Expression initializer)
    {
        return new LocalDeclarationStatement(local, initializer);
    }

    private static ReturnStatement Return(Expression value)
    {
        return new ReturnStatement(value, value.Type);
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

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(Node(offset), local);
    }

    private static Storm.Runtime.SyntaxNode Node(int offset = 0)
    {
        return HandBuiltModel.Node(offset);
    }
}