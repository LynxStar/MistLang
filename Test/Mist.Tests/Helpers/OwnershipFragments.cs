using Mist.Common.Diagnostics;
using Mist.Generated;
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

internal sealed class OwnershipFragments
{
    private readonly HandBuiltModel model = new();
    private readonly ClassType engine = HandBuiltModel.Class("Engine");

    public IReadOnlyList<Diagnostic> Analyze(string name)
    {
        if(name.StartsWith("owned-field-", StringComparison.Ordinal) || name.StartsWith("owned-string-field-", StringComparison.Ordinal)
            || name.StartsWith("this-lend-", StringComparison.Ordinal) || name.StartsWith("shared-extract-", StringComparison.Ordinal))
        {
            return new OwnedFieldFragments().Analyze(name);
        }

        if((name.StartsWith("readonly-", StringComparison.Ordinal) && name != "readonly-ref-to-frozen-parameter")
            || name.StartsWith("field-assigned-", StringComparison.Ordinal))
        {
            return ReadonlyFragments.Analyze(name);
        }

        if(name.Contains("captures-", StringComparison.Ordinal) || name.StartsWith("stack-span-", StringComparison.Ordinal))
        {
            return Escape(name);
        }

        if(name.StartsWith("this-tier-", StringComparison.Ordinal)
            || name is "frozen-to-borrowed-parameter" or "readonly-ref-to-frozen-parameter"
                or "unrestricted-to-borrowed-parameter" or "unrestricted-to-frozen-parameter" or "shared-extract")
        {
            return Tiers(name);
        }

        if(name is "affine-declared-owned" or "relevant-declared-owned" or "owned-abstract" or "ref-on-class"
            or "ref-struct-as-type-argument" or "ref-struct-field-in-class" or "affine-field-in-relevant-container"
            or "linear-field-in-unrestricted-container" or "linear-field-in-affine-container"
            or "task-field-in-unrestricted-container" or "unconstrained-type-parameter-field-in-linear-container"
            or "owned-binding-of-linear-type"
            || name.Contains("-argument-closing-", StringComparison.Ordinal) || name.Contains("-argument-reaching-", StringComparison.Ordinal))
        {
            return OwnershipDeclarations.Analyze(name);
        }

        return Moves(name);
    }

    private IReadOnlyList<Diagnostic> Moves(string name)
    {
        engine.Modifiers = new MemberModifiers { IsOwned = true };
        if(name.StartsWith("linear-", StringComparison.Ordinal))
        {
            engine.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
        }
        else if(name.StartsWith("affine-", StringComparison.Ordinal))
        {
            engine.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Affine };
        }

        IMistType localType = engine;
        if(name.StartsWith("linear-task-", StringComparison.Ordinal))
        {
            var task = model.Type("ValueTask<>");
            task.Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear };
            localType = model.Interner.GetOrCreateConstructedType(task, [model.Type("Int32")]);
        }

        var owned = name.StartsWith("linear-", StringComparison.Ordinal) || name.StartsWith("affine-", StringComparison.Ordinal)
            ? OwnershipKind.Unrestricted : OwnershipKind.Owned;
        var lender = new LocalVariable("e", localType)
        {
            Ownership = owned,
            Parameter = name == "owned-parameter-used-after-move" ? new Parameter("e", localType, new ParameterModifier(false, false, owned)) : null,
        };
        var statements = new List<Statement> { new LocalDeclarationStatement(lender, HandBuiltModel.Literal(localType)) };
        var recipient = new LocalVariable("f", localType) { Ownership = owned };
        var consume = new Method("Consume", model.Type("Void"), [new Parameter("e", localType, new ParameterModifier(false, false, OwnershipKind.Owned))], new MemberModifiers { IsStatic = true });
        var drop = new Method("Drop", model.Type("Int32"), [], default);
        Statement move = new LocalDeclarationStatement(recipient, Ref(lender));
        Statement read = new ExpressionStatement(Call(new Method("Get", model.Type("Int32"), [], default), Ref(lender, 37), 37));

        switch(name)
        {
            case "use-after-move-into-owned-parameter":
            case "owned-parameter-used-after-move":
            case "use-after-move-through-interpolation":
            case "use-after-move-in-one-branch":
            case "use-after-move-in-else-branch":
            case "moved-inside-loop":
                move = new ExpressionStatement(Call(consume, null, name == "moved-inside-loop" ? 37 : 0, Ref(lender, name == "moved-inside-loop" ? 37 : 0)));
                break;
            case "use-after-move-into-owned-constructor-parameter":
                var constructor = new Constructor("Holder", engine, [new Parameter("inner", localType, new ParameterModifier(false, false, OwnershipKind.Owned))], default);
                move = new ExpressionStatement(new NewExpression(HandBuiltModel.Node(), engine, [Ref(lender)], constructor, [], [localType]));
                break;
            case "use-after-move-into-array-literal":
                move = new ExpressionStatement(new ArrayLiteralExpression(HandBuiltModel.Node(), localType, [Ref(lender)], model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [localType])));
                break;
            case "use-after-move-by-assignment":
                statements.Add(new LocalDeclarationStatement(recipient, HandBuiltModel.Literal(localType)));
                move = new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), Ref(recipient), Ref(lender), localType));
                break;
            case "use-after-move-into-unrestricted-binding":
                move = new LocalDeclarationStatement(new LocalVariable("f", localType), Ref(lender));
                break;
            case "use-after-consuming-call":
                move = new ExpressionStatement(Call(new Method("Close", model.Type("String"), [], default) { DeclaredThisTier = ThisTier.Owned }, Ref(lender)));
                break;
            case "affine-used-after-drop":
            case "linear-used-after-drop":
            case "linear-dropped-twice":
            case "linear-task-dropped-twice":
                move = new ExpressionStatement(Call(drop, Ref(lender)));
                if(name.EndsWith("twice", StringComparison.Ordinal))
                {
                    read = new ExpressionStatement(Call(drop, Ref(lender, 37), 37));
                }

                break;
            case "linear-task-awaited-twice":
                move = new LocalDeclarationStatement(new LocalVariable("first", drop.ReturnType), Await(lender, drop));
                read = new LocalDeclarationStatement(new LocalVariable("second", drop.ReturnType), Await(lender, drop, 37));
                break;
            case "access-while-borrowed":
            case "borrow-while-frozen":
            case "borrower-mutation-while-frozen":
            case "field-write-while-frozen":
            case "move-while-frozen":
            case "move-while-readonly-ref-outstanding":
            case "mutation-while-frozen":
                return Leases(name, lender, consume, statements);
        }

        if(name == "use-after-move-in-one-branch")
        {
            statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(move), null));
        }
        else if(name == "use-after-move-in-else-branch")
        {
            statements.Add(new IfStatement(Condition(), HandBuiltModel.Block(), HandBuiltModel.Block(move)));
        }
        else if(name == "moved-inside-loop")
        {
            statements.Add(new WhileStatement(Condition(), HandBuiltModel.Block(move)));
            return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements]));
        }
        else
        {
            statements.Add(move);
        }

        if(name == "use-after-move-through-interpolation")
        {
            read = new ExpressionStatement(new InterpolatedStringExpression(HandBuiltModel.Node(), [new InterpolatedStringHole(((ExpressionStatement)read).Expression)], model.Type("String")));
        }

        statements.Add(read);
        return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements]));
    }

    private IReadOnlyList<Diagnostic> Leases(string name, LocalVariable lender, Method consume, List<Statement> statements)
    {
        var borrower = new LocalVariable("b", engine) { Ownership = OwnershipKind.Borrowed };
        if(name == "borrower-mutation-while-frozen")
        {
            statements.Add(new LocalDeclarationStatement(borrower, Ref(lender)));
            lender = borrower;
        }

        var lease = new LocalVariable("view", engine)
        {
            Ownership = name == "access-while-borrowed" ? OwnershipKind.Borrowed : OwnershipKind.Frozen,
            IsRef = name == "move-while-readonly-ref-outstanding",
            IsReadonly = name == "move-while-readonly-ref-outstanding",
        };
        if(lease.IsRef)
        {
            lease = new LocalVariable("view", engine) { IsRef = true, IsReadonly = true };
        }

        statements.Add(new LocalDeclarationStatement(lease, Ref(lender)));
        switch(name)
        {
            case "borrow-while-frozen":
                statements.Add(new LocalDeclarationStatement(borrower, Ref(lender, 37)));
                break;
            case "move-while-frozen":
                statements.Add(new LocalDeclarationStatement(new LocalVariable("g", engine) { Ownership = OwnershipKind.Owned }, Ref(lender, 37)));
                break;
            case "move-while-readonly-ref-outstanding":
                statements.Add(new ExpressionStatement(Call(consume, null, 37, Ref(lender, 37))));
                break;
            case "field-write-while-frozen":
            case "borrower-mutation-while-frozen":
                var field = new Field("Rpm", model.Type("Int32"), default, default) { DeclaringType = engine };
                var target = new MemberAccessExpression(HandBuiltModel.Node(37), Ref(lender, 37), "Rpm", field.Type,
                    member: new FieldResult(field, engine, []));
                statements.Add(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(37), target, HandBuiltModel.Literal(field.Type, 2L), field.Type)));
                break;
            case "mutation-while-frozen":
                var rev = new Method("Rev", model.Type("Void"), [], default) { DeclaredThisTier = ThisTier.Borrowed };
                statements.Add(new ExpressionStatement(Call(rev, Ref(lender, 37), 37)));
                break;
            default:
                statements.Add(new ExpressionStatement(Call(new Method("Get", model.Type("Int32"), [], default), Ref(lender, 37), 37)));
                break;
        }

        return model.Analyze(new OwnershipAnalyzer(), HandBuiltModel.Block([.. statements]));
    }

    private IReadOnlyList<Diagnostic> Escape(string name)
    {
        if(name.StartsWith("stack-span-", StringComparison.Ordinal))
        {
            var definition = new StructType("Span", "Standard", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } };
            var span = model.Interner.GetOrCreateConstructedType(definition, [model.Type("Int32")]);
            var stackLocal = new LocalVariable("s", span);
            var allocation = new StackallocExpression(HandBuiltModel.Node(), model.Type("Int32"), HandBuiltModel.Literal(model.Type("Int32"), 4L), span);
            var statements = new List<Statement> { new LocalDeclarationStatement(stackLocal, allocation) };
            if(name == "stack-span-returned-through-alias")
            {
                var alias = new LocalVariable("t", span);
                statements.Add(new LocalDeclarationStatement(alias, Ref(stackLocal)));
                stackLocal = alias;
            }

            statements.Add(new ReturnStatement(Ref(stackLocal, 37), span));
            return model.Analyze(new EscapeAnalyzer(), HandBuiltModel.Block([.. statements]));
        }

        IMistType type = name == "lambda-captures-ref-struct" ? new StructType("Cursor", "Probe", new Scope(null), null, null) { Modifiers = new MemberModifiers { IsRef = true } } : engine;
        var ownership = name.Contains("borrowed", StringComparison.Ordinal) ? OwnershipKind.Borrowed
            : name.Contains("frozen", StringComparison.Ordinal) ? OwnershipKind.Frozen
            : name.Contains("owned", StringComparison.Ordinal) ? OwnershipKind.Owned
            : OwnershipKind.Unrestricted;
        var local = new LocalVariable("e", type)
        {
            Ownership = ownership,
            IsRef = name.Contains("readonly-ref", StringComparison.Ordinal),
            IsReadonly = name.Contains("readonly-ref", StringComparison.Ordinal),
        };
        var captured = Ref(local, 37);
        object root = name.StartsWith("local-function-", StringComparison.Ordinal)
            ? new LocalFunctionStatement(new Method("Read", model.Type("Int32"), [], default) { Body = HandBuiltModel.Block(new ReturnStatement(captured, model.Type("Int32"))) }, [])
            : new LambdaExpression(HandBuiltModel.Node(), [], captured, null, false, model.Interner.GetOrCreateFunctionType([], model.Type("Int32")));
        return model.Analyze(new EscapeAnalyzer(), root);
    }

    private IReadOnlyList<Diagnostic> Tiers(string name)
    {
        var field = new Field("Balance", model.Type("Int32"), OwnershipKind.Owned, default) { DeclaringType = engine };
        var write = new AssignmentExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), field), HandBuiltModel.Literal(field.Type, 1L), field.Type);
        var rev = new Method("Rev", model.Type("Void"), [], default)
        {
            Body = HandBuiltModel.Block(new ExpressionStatement(write)),
            DeclaringType = engine,
        };
        if(name.StartsWith("this-tier-insufficient", StringComparison.Ordinal))
        {
            var declared = new Method("Touch", model.Type("Void"), [], default)
            {
                Body = HandBuiltModel.Block(new ExpressionStatement(write)),
                DeclaredThisTier = name.EndsWith("frozen", StringComparison.Ordinal) ? ThisTier.Frozen : ThisTier.ReadonlyRef,
                DeclaringType = engine,
                DeclarationNode = HandBuiltModel.Node(37),
            };
            return model.Analyze(new TierAnalyzer(), declared.Body, owner: engine, member: declared);
        }

        if(name == "this-tier-mismatch-through-explicit-this-call")
        {
            rev = new Method("DepositViaThis", model.Type("Void"), [], default)
            {
                Body = HandBuiltModel.Block(new ExpressionStatement(Call(rev, new ThisExpression(HandBuiltModel.Node(), engine)))),
            };
        }
        else if(name == "this-tier-mismatch-owned-required")
        {
            rev = new Method("Close", model.Type("Void"), [], default) { DeclaredThisTier = ThisTier.Owned };
        }

        var ownership = !name.StartsWith("readonly-ref-", StringComparison.Ordinal) && name.Contains("frozen", StringComparison.Ordinal)
            ? OwnershipKind.Frozen : OwnershipKind.Unrestricted;
        var local = new LocalVariable("e", engine)
        {
            Ownership = ownership,
            IsRef = name.Contains("readonly-ref", StringComparison.Ordinal),
            IsReadonly = name.Contains("readonly-ref", StringComparison.Ordinal),
            Parameter = new Parameter("e", engine, new ParameterModifier(name.Contains("readonly-ref", StringComparison.Ordinal), name.Contains("readonly-ref", StringComparison.Ordinal), ownership)),
        };
        Expression receiver = Ref(local);
        if(name == "this-tier-mismatch-bare-field")
        {
            receiver = new FieldReferenceExpression(HandBuiltModel.Node(), new Field("Main", engine, default, default));
        }

        if(name == "shared-extract")
        {
            var access = new MemberAccessExpression(HandBuiltModel.Node(37), new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("car", engine)), "Front", engine,
                member: new FieldResult(new Field("Front", engine, OwnershipKind.Owned, default), engine, []));
            return model.Analyze(new TierAnalyzer(), new LocalDeclarationStatement(new LocalVariable("f", engine) { Ownership = OwnershipKind.Owned }, access));
        }

        if(name.EndsWith("-parameter", StringComparison.Ordinal) && !name.StartsWith("this-tier-", StringComparison.Ordinal))
        {
            var required = name.Contains("to-borrowed", StringComparison.Ordinal) ? OwnershipKind.Borrowed : OwnershipKind.Frozen;
            var method = new Method("Lend", model.Type("Void"), [new Parameter("value", engine, new ParameterModifier(false, false, required))], new MemberModifiers { IsStatic = true });
            if(name.StartsWith("unrestricted-", StringComparison.Ordinal))
            {
                local = new LocalVariable("e", engine);
            }

            return model.Analyze(new TierAnalyzer(), Call(method, null, 37, Ref(local, 37)));
        }

        return model.Analyze(new TierAnalyzer(), Call(rev, receiver, 37));
    }

    private static InvocationExpression Await(LocalVariable local, Method drop, int offset = 0)
    {
        var location = HandBuiltModel.Node(offset);
        var syntax = new AwaitExpressionUnaryNode { Span = location.Span, Origin = location.Origin };
        var callee = new MemberAccessExpression(syntax, Ref(local, offset), "Drop", drop.ReturnType);
        return new InvocationExpression(syntax, callee, [], drop.ReturnType, drop, []);
    }

    private InvocationExpression Call(Method method, Expression? receiver = null, int offset = 0, params Expression[] arguments)
    {
        var group = new MemberAccessExpression(HandBuiltModel.Node(offset), receiver ?? HandBuiltModel.Literal(engine), method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(offset), group, arguments, method.ReturnType, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private Expression Condition()
    {
        return HandBuiltModel.Literal(model.Type("Boolean"));
    }

    private static LocalReferenceExpression Ref(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }
}