using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits C# source for method bodies by walking the Statement/Expression model.
/// <para>
/// Emission is where an unresolved node becomes a user-facing problem, which is why a
/// <see cref="DiagnosticBag"/> travels with the writer: the binder parks sentinels deliberately, and
/// writing one out is the moment that turns into C# that will not compile. See
/// <see cref="BackendDiagnostics.UnsupportedExpression"/>.
/// </para>
/// </summary>
internal static class BodyEmitter
{
    private const int AssignmentPrecedence = 0;

    private const int CoalescePrecedence = 1;

    private const int RelationalPrecedence = 5;

    private const int SwitchPrecedence = 8;

    private const int PrefixPrecedence = 9;

    private const int PostfixPrecedence = 10;

    private const int DisjunctionPrecedence = 1;

    private const int ConjunctionPrecedence = 2;

    private const int NegationPrecedence = 3;

    public static void EmitBlock(Block block, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        EmitBlock(block, [], writer, diagnostics);
    }

    public static void EmitBlock(
        Block block, IEnumerable<string> parameterNames, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var names = LocalNames.Enter(block, parameterNames);
        EmitStatements(block.Statements, 0, writer, diagnostics);
    }

    /// <summary>
    /// A method body, with the drops its OWNED parameters oblige. The caller handed those values over,
    /// so the body owes the release; the nesting gives the same reverse order a block's own bindings
    /// get, for the same reason.
    /// </summary>
    public static void EmitMethodBody(
        Method method, Block body, CSharpWriter writer, DiagnosticBag diagnostics, Func<Parameter, string>? spell = null)
    {
        using var names = LocalNames.Enter(body, method.ParametersList.Select(parameter => parameter.Name));
        EmitContextBindings(method, writer);
        EmitParameterDrops(DropInsertion.DroppedParameters(method, body), 0, body, spell, writer, diagnostics);
    }

    /// <summary>
    /// A local function's body, emitted where the function is declared. An await inside it is a real
    /// one whatever encloses it -- a sync lambda included -- because the function can be made async on
    /// its own, which <see cref="AsyncPromotion"/> does; so the blocking mode is off for its duration
    /// and restored after.
    /// </summary>
    public static void EmitLocalFunctionBody(
        LocalFunctionStatement function, Func<Parameter, string> spell, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var enclosing = blockingAwaits;
        blockingAwaits = false;

        try
        {
            EmitMethodBody(function.Function, function.Body, writer, diagnostics, spell);
        }
        finally
        {
            blockingAwaits = enclosing;
        }
    }

    /// <summary>
    /// A `with Type name` clause read at body entry. Emitted whether the body uses the name or not:
    /// the clause is what makes the method a context CONSUMER, and a transparent intermediate declares
    /// one precisely so the value stays reachable through it.
    /// </summary>
    private static void EmitContextBindings(Method method, CSharpWriter writer)
    {
        if(method.ContextBindingsList.Count > 0)
        {
            ContextLowering.Require();
        }

        foreach(var binding in method.ContextBindingsList)
        {
            var type = TypeNameEmitter.Emit(binding.ContextType);

            writer.WriteLine($"var {binding.Identifier} = ({type})MistContext.Peek<{type}>();");
        }
    }

    private static void EmitParameterDrops(
        IReadOnlyList<Parameter> dropped,
        int index,
        Block body,
        Func<Parameter, string>? spell,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(index >= dropped.Count)
        {
            EmitBlock(body, writer, diagnostics);
            return;
        }

        var parameter = dropped[index];

        writer.WriteLine("try");

        using(writer.Block())
        {
            EmitParameterDrops(dropped, index + 1, body, spell, writer, diagnostics);
        }

        writer.WriteLine("finally");

        using(writer.Block())
        {
            EmitRelease(spell?.Invoke(parameter) ?? parameter.Name, parameter.Type, writer);
        }
    }

    /// <summary>
    /// The statements from <paramref name="index"/> on. A declaration that still owns a droppable value
    /// at the end of the scope takes the REST OF THE BLOCK as the body of a try, with the drop in its
    /// finally, so the emission recurses rather than looping.
    /// <para>
    /// The nesting is what gives reverse declaration order, each drop sitting inside the try of every
    /// binding declared before it. try/finally rather than a trailing statement because the drop has to
    /// survive a return or a throw from inside, which no trailing statement does.
    /// </para>
    /// </summary>
    private static void EmitStatements(
        IReadOnlyList<Statement> statements, int index, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        for(var i = index; i < statements.Count; i++)
        {
            if(statements[i] is LocalDeclarationStatement declaration
                && DropInsertion.NeedsDrop(declaration, Remainder(statements, i + 1)))
            {
                EmitStatement(declaration, writer, diagnostics);
                writer.WriteLine("try");

                using(writer.Block())
                {
                    EmitStatements(statements, i + 1, writer, diagnostics);
                }

                writer.WriteLine("finally");

                using(writer.Block())
                {
                    EmitRelease(LocalNames.Of(declaration.Local), declaration.Local.Type, writer);
                }

                return;
            }

            if(statements[i] is ExpressionStatement discarded
                && DropInsertion.IsDiscardedTask(discarded.Expression))
            {
                EmitDiscardedTask(discarded, statements, i, writer, diagnostics);
                return;
            }

            EmitStatement(statements[i], writer, diagnostics);
        }
    }

    /// <summary>
    /// A task nobody kept still has to complete, so it is bound to a temporary and awaited at scope
    /// exit rather than left running -- the same obligation a named binding carries, and the reason it
    /// takes the same shape. Only reachable in an ASYNC context: a sync one awaits the call where it
    /// stands, so the statement is an await by the time it arrives here and never a bare task.
    /// </summary>
    private static void EmitDiscardedTask(
        ExpressionStatement discarded,
        IReadOnlyList<Statement> statements,
        int index,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var name = TemporaryName();

        writer.Write($"var {name} = ");
        EmitExpression(discarded.Expression, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
        writer.WriteLine("try");

        using(writer.Block())
        {
            EmitStatements(statements, index + 1, writer, diagnostics);
        }

        writer.WriteLine("finally");

        using(writer.Block())
        {
            EmitRelease(name, discarded.Expression.Type, writer);
        }
    }

    /// <summary>
    /// What a scope owes a binding on the way out. A task is completed rather than dropped -- that is
    /// what dropping a task means -- and the wait blocks in a sync lambda for the same reason any other
    /// does there.
    /// </summary>
    private static void EmitRelease(string name, IMistType type, CSharpWriter writer)
    {
        if(!DropInsertion.IsAwaitable(type))
        {
            writer.WriteLine($"{DropInsertion.DropChain(name, type)};");
            return;
        }

        writer.WriteLine(blockingAwaits ? $"{name}.GetAwaiter().GetResult();" : $"await {name};");
    }

    private static IReadOnlyList<Statement> Remainder(IReadOnlyList<Statement> statements, int from)
    {
        var remainder = new List<Statement>(statements.Count - from);

        for(var i = from; i < statements.Count; i++)
        {
            remainder.Add(statements[i]);
        }

        return remainder;
    }

    private static void EmitStatement(Statement statement, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        switch(statement)
        {
            case ExpressionStatement { Expression: InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } } drop }
                when TaskResult.Dropped(drop) is { } task:
                {
                    var present = PresentName(drop);
                    writer.WriteLine($"if ({RenderAt(task, PrefixPrecedence, diagnostics)} is {{ }} {present})");
                    using(writer.Block())
                    {
                        writer.WriteLine($"{Awaited(present)};");
                    }

                    break;
                }

            case ExpressionStatement exprStmt:
                {
                    using var hoisted = EmitHoisted(exprStmt.Expression, writer, diagnostics);
                    writer.Write("");

                    if(DiscardedCombinator(exprStmt.Expression, diagnostics) is { } direct)
                    {
                        writer.Append(blockingAwaits ? $"{direct}.GetAwaiter().GetResult()" : $"await {direct}");
                    }
                    else
                    {
                        EmitExpression(exprStmt.Expression, writer, diagnostics);
                    }

                    writer.Append(";");
                    writer.AppendLine();
                    break;
                }

            case ReturnStatement returnStmt:
                using(EmitHoisted(returnStmt.Value, writer, diagnostics))
                {
                    EmitReturn(returnStmt, writer, diagnostics);
                }

                break;

            case LocalDeclarationStatement localDecl:
                EmitLocalDeclaration(localDecl, writer, diagnostics);
                break;

            case TupleDeconstructionStatement deconstruction:
                EmitDeconstruction(deconstruction, writer, diagnostics);
                break;

            case IfStatement ifStmt:
                EmitContained(ifStmt.ConditionBinds, writer, () => EmitIf(ifStmt, writer, diagnostics));
                break;

            case WhileStatement whileStmt:
                EmitContained(whileStmt.ConditionBinds, writer, () => EmitWhile(whileStmt, writer, diagnostics));
                break;

            case ForStatement forStmt:
                EmitFor(forStmt, writer, diagnostics);
                break;

            case ForEachStatement foreachStmt:
                EmitForEach(foreachStmt, writer, diagnostics);
                break;

            case BreakStatement:
                writer.WriteLine("break;");
                break;

            case WithBlockStatement withBlock:
                EmitWithBlock(withBlock, writer, diagnostics);
                break;

            case LocalFunctionStatement localFunction:
                LocalFunctionEmitter.Emit(localFunction, writer, diagnostics);
                break;

            case Block nestedBlock:
                using(writer.Block())
                {
                    EmitBlock(nestedBlock, writer, diagnostics);
                }

                break;

            case UnsupportedStatement unsupported:
                diagnostics.ReportAtNode(BackendDiagnostics.UnsupportedStatement, unsupported.Node, null);
                writer.WriteLine($"/* unsupported statement: {unsupported.Node.GetType().Name} */");
                break;

            default:
                // Unreported because it is not a language gap: reaching here means the model gained a
                // statement kind the emitter was never taught, which no user can act on.
                writer.WriteLine($"/* unsupported statement: {statement.GetType().Name} */");
                break;
        }
    }

    /// <summary>
    /// A call's arguments, each marked <c>ref</c> where the parameter it fills is.
    /// <para>
    /// Read from the RESOLVED CALLEE rather than from the argument, because binding drops the
    /// <c>ref</c> a call site wrote -- so the parameter is the only surviving record, and it is the one
    /// C# insists the two agree on anyway. A callee that resolved to nothing marks nothing, which is
    /// right: invoking a function value has no parameter to consult and Mist's function types carry no
    /// ref.
    /// </para>
    /// </summary>
    /// <summary>
    /// The factory arguments a call to a default-constrained generic supplies, ahead of the declared
    /// ones. The type arguments come off the CALLEE rather than being re-derived, so an inferred one
    /// reaches here already substituted -- the emitter runs no inference of its own.
    /// </summary>
    private static void EmitWitnesses(
        InvocationExpression invocation, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var typeArguments = invocation.Receiver switch
        {
            MemberAccessExpression access => access.TypeArguments,
            MethodReferenceExpression reference => reference.TypeArguments,
            _ => [],
        };

        if(invocation.Method is not { } method || typeArguments.Count == 0)
        {
            return;
        }

        var witnesses = DefaultConstraintWitness.Arguments(method.TypeParametersList, typeArguments);

        if(witnesses.Count == 0)
        {
            return;
        }

        foreach(var missing in witnesses.Where(witness => witness is null))
        {
            diagnostics.ReportAtNode(BackendDiagnostics.UnsupportedExpression, invocation.Node, null);
        }

        writer.Append(string.Join(", ", witnesses.Select(
            witness => witness ?? "/* unsupported: default constraint */")));

        if(invocation.Arguments.Count > 0)
        {
            writer.Append(", ");
        }
    }

    private static void EmitArguments(
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter>? parameters,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        // The call site is FLAT and the parameter is one tuple, which is the whole of what `params`
        // means on the value side. C# has no such convention, so the arguments the pack absorbs are
        // packed here -- the one place that knows both how many there are and where they end.
        if(parameters is not null && FlattenedTupleSlot(parameters) is { } flattened)
        {
            EmitFlattened(arguments, parameters, flattened, writer, diagnostics);
            return;
        }

        for(var i = 0; i < arguments.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            if(parameters is not null && i < parameters.Count)
            {
                writer.Append(ArgumentKeywords.For(parameters[i]));
            }

            // A spread that survived binding was PROVABLE rather than expandable: its pack was
            // still symbolic then, and only the specialization fixes the width it stands for.
            if(arguments[i] is SpreadExpression spread && TupleWidth(spread.Operand.Type) is { } width)
            {
                EmitTupleElements(spread.Operand, width, writer, diagnostics);
                continue;
            }

            UnitHoisting.Refuse(arguments[i], diagnostics);
            EmitCoerced(
                arguments[i],
                parameters is not null && i < parameters.Count ? parameters[i].Type : null,
                writer,
                diagnostics);
        }
    }

    private static void EmitReturn(ReturnStatement returnStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(returnStmt.Value is { } value && OptionalUnion.IsVoid(value.Type))
        {
            if(value is InvocationExpression)
            {
                writer.Write("");
                EmitExpression(value, writer, diagnostics);
                writer.Append(";");
                writer.AppendLine();
            }

            writer.WriteLine("return;");
        }
        else if(returnStmt.Value is not null)
        {
            writer.Write(returnStmt.IsRef ? "return ref " : "return ");
            EmitExpression(returnStmt.Value, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
        }
        else
        {
            writer.WriteLine("return;");
        }
    }

    private static UnitHoisting.Hoisting EmitHoisted(Expression? root, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var hoisting = UnitHoisting.Plan(root);

        foreach(var step in hoisting.Steps)
        {
            var text = Render(step.Expression, diagnostics);

            if(step.IsHoist)
            {
                writer.WriteLine($"{text};");
                hoisting.Bind(step, "Standard.Void.Instance");
                continue;
            }

            var temporary = TemporaryName();
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(step.Expression.Type)} {temporary} = {text};");
            hoisting.Bind(step, temporary);
        }

        return hoisting;
    }

    private static void EmitLocalDeclaration(LocalDeclarationStatement localDecl, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var hoisted = EmitHoisted(localDecl.Initializer, writer, diagnostics);
        var local = localDecl.Local;
        var refKeyword = RefKeywords.For(local.IsRef, local.IsReadonly);

        var typeName = $"{refKeyword}{TypeNameEmitter.EmitInferable(local.Type)}";

        if(localDecl.Initializer is InvocationExpression call && OptionalUnion.IsVoid(call.Type))
        {
            writer.Write("");
            EmitExpression(call, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
            writer.WriteLine($"{typeName} {LocalNames.Of(localDecl.Local)} = Standard.Void.Instance;");
            return;
        }

        writer.Write($"{typeName} {LocalNames.Of(local)}");
        if(localDecl.Initializer is not null)
        {
            // C# marks the alias TWICE, once on the type and once on what is aliased, and Mist writes
            // only the first. The second is reinstated here rather than being bound as part of the
            // initializer, since Mist has no syntax for it to have come from.
            writer.Append(local.IsRef ? " = ref " : " = ");
            EmitCoerced(localDecl.Initializer, local.Type, writer, diagnostics);
        }

        writer.Append(";");
        writer.AppendLine();
    }

    /// <summary>
    /// A statement wrapped in a braced block when its condition introduced a pattern binding. C# scopes
    /// such a binding to the block CONTAINING the statement, where Mist scopes it to the branch the
    /// condition guards, so without the wrapper a name reused later in the same Mist scope collides
    /// (CS0136) against a binding Mist considers out of scope by then.
    /// </summary>
    private static void EmitContained(bool contain, CSharpWriter writer, Action emit)
    {
        if(!contain)
        {
            emit();
            return;
        }

        using(writer.Block())
        {
            emit();
        }
    }

    /// <summary>
    /// <c>with(x) { ... }</c>: the value is made ambient for the block and dropped on the way out,
    /// whichever way the block leaves -- hence try/finally, which is the only C# that survives a
    /// return or a throw from inside.
    /// <para>
    /// Evaluated into a temporary first, because the same instance is pushed, popped and dropped and
    /// the expression must run once.
    /// </para>
    /// <para>
    /// BRACED, so that the binding is scoped to the block Mist scoped it to. Without this the local
    /// lands in the enclosing method and two sibling `with`s binding the same name collide, as does a
    /// `with` beside an ordinary block reusing it.
    /// </para>
    /// </summary>
    private static void EmitWithBlock(
        WithBlockStatement withBlock, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using(writer.Block())
        {
            EmitWithScope(withBlock, writer, diagnostics);
        }
    }

    private static void EmitWithScope(
        WithBlockStatement withBlock, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        ContextLowering.Require();

        var bound = withBlock.Binding is { } bindingLocal ? LocalNames.Of(bindingLocal) : TemporaryName();

        // The KEY the value is filed under, which a form may have written and which is not the
        // value's own type -- see WithBlockStatement.KeyType. The drop below still asks the VALUE
        // what it owes, those being different questions about the same instance.
        var type = TypeNameEmitter.Emit(withBlock.KeyType);

        writer.Write($"var {bound} = ");
        EmitExpression(withBlock.Value, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
        writer.WriteLine($"MistContext.Push<{type}>({bound});");
        writer.WriteLine("try");

        using(writer.Block())
        {
            EmitBlock(withBlock.Body, writer, diagnostics);
        }

        writer.WriteLine("finally");

        using(writer.Block())
        {
            writer.WriteLine($"MistContext.Pop<{type}>();");

            if(DropInsertion.ExitAction(withBlock.Value.Type) is not null)
            {
                writer.WriteLine($"{DropInsertion.DropChain(bound, withBlock.Value.Type)};");
            }
        }
    }

    private static void EmitWhile(WhileStatement whileStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(UnitHoisting.Plan(whileStmt.Condition).Steps.Count == 0)
        {
            writer.Write("while (");
            EmitExpression(whileStmt.Condition, writer, diagnostics);
            writer.Append(")");
            writer.AppendLine();

            using(writer.Block())
            {
                EmitBlock(whileStmt.Body, writer, diagnostics);
            }

            return;
        }

        writer.WriteLine("while (true)");
        using(writer.Block())
        {
            using(EmitHoisted(whileStmt.Condition, writer, diagnostics))
            {
                writer.Write("if (!(");
                EmitExpression(whileStmt.Condition, writer, diagnostics);
                writer.Append("))");
                writer.AppendLine();
            }

            using(writer.Block())
            {
                writer.WriteLine("break;");
            }

            writer.WriteBlankLine();
            EmitBlock(whileStmt.Body, writer, diagnostics);
        }
    }

    /// <summary>
    /// Each target spelled with its own type rather than through <c>var</c>, so both Mist forms lower to
    /// one shape and the types are the ones the MODEL resolved -- a <c>var</c> form would hand the choice
    /// back to C#'s inference, which types a target by the element as C# sees it and not as Mist does.
    /// </summary>
    private static void EmitDeconstruction(
        TupleDeconstructionStatement deconstruction, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var hoisted = EmitHoisted(deconstruction.Initializer, writer, diagnostics);

        if(deconstruction.ElementConversions.All(conversion => conversion is null))
        {
            var targets = deconstruction.Targets.Select(
                target => $"{TypeNameEmitter.EmitInferable(target.Type)} {LocalNames.Of(target)}");

            writer.Write($"({string.Join(", ", targets)}) = ");
            EmitExpression(deconstruction.Initializer, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
            return;
        }

        var tuple = $"__tuple{deconstruction.Initializer.Node.Span.Start}";

        writer.Write($"{TypeNameEmitter.EmitInferable(deconstruction.Initializer.Type)} {tuple} = ");
        EmitExpression(deconstruction.Initializer, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();

        for(var i = 0; i < deconstruction.Targets.Count; i++)
        {
            var read = $"{tuple}.Item{i + 1}";
            var value = deconstruction.ElementConversions[i] is { } conversion ? CastLowering.Implicit(conversion, read) : read;

            writer.WriteLine($"{TypeNameEmitter.EmitInferable(deconstruction.Targets[i].Type)} {LocalNames.Of(deconstruction.Targets[i])} = {value};");
        }
    }

    private static void EmitForEach(ForEachStatement loop, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var conversion = loop.ElementConversion;
        var element = conversion is null ? LocalNames.Of(loop.Variable) : $"__element{conversion.Node.Span.Start}";

        using(EmitHoisted(loop.Collection, writer, diagnostics))
        {
            writer.Write($"foreach ({TypeNameEmitter.EmitInferable(conversion?.Operand.Type ?? loop.Variable.Type)} {element} in ");
            EmitExpression(loop.Collection, writer, diagnostics);
            writer.Append(")");
            writer.AppendLine();
        }

        using(writer.Block())
        {
            if(conversion is not null)
            {
                writer.WriteLine($"{TypeNameEmitter.EmitInferable(loop.Variable.Type)} {LocalNames.Of(loop.Variable)} = {CastLowering.Implicit(conversion, element)};");
            }

            EmitBlock(loop.Body, writer, diagnostics);
        }
    }

    private static void EmitIf(IfStatement ifStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var hoisted = EmitHoisted(ifStmt.Condition, writer, diagnostics);
        writer.Write("if (");
        EmitExpression(ifStmt.Condition, writer, diagnostics);
        writer.Append(")");
        writer.AppendLine();
        using(writer.Block())
        {
            EmitBlock(ifStmt.ThenBranch, writer, diagnostics);
        }

        if(ifStmt.ElseBranch is IfStatement bindingElseIf
            && (bindingElseIf.ConditionBinds || UnitHoisting.Plan(bindingElseIf.Condition).Steps.Count > 0))
        {
            writer.WriteLine("else");
            using(writer.Block())
            {
                EmitIf(bindingElseIf, writer, diagnostics);
            }
        }
        else if(ifStmt.ElseBranch is IfStatement elseIf)
        {
            writer.Write("else ");
            EmitIf(elseIf, writer, diagnostics);
        }
        else if(ifStmt.ElseBranch is Block elseBlock)
        {
            writer.WriteLine("else");
            using(writer.Block())
            {
                EmitBlock(elseBlock, writer, diagnostics);
            }
        }
    }

    private static void EmitFor(ForStatement forStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Write("for (");

        // Initializer
        if(forStmt.Initializer is LocalDeclarationStatement initDecl)
        {
            var typeName = TypeNameEmitter.Emit(initDecl.Local.Type);
            writer.Append($"{typeName} {LocalNames.Of(initDecl.Local)}");
            if(initDecl.Initializer is not null)
            {
                writer.Append(" = ");
                EmitExpression(initDecl.Initializer, writer, diagnostics);
            }
        }
        else if(forStmt.Initializer is ExpressionStatement initExpr)
        {
            EmitExpression(initExpr.Expression, writer, diagnostics);
        }

        writer.Append("; ");

        // Condition
        if(forStmt.Condition is not null)
        {
            EmitExpression(forStmt.Condition, writer, diagnostics);
        }

        writer.Append("; ");

        // Iterator
        if(forStmt.Iterator is not null)
        {
            EmitExpression(forStmt.Iterator, writer, diagnostics);
        }

        writer.Append(")");
        writer.AppendLine();
        using(writer.Block())
        {
            EmitBlock(forStmt.Body, writer, diagnostics);
        }
    }

    internal static void EmitExpression(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(UnitHoisting.Substitution(expression) is { } substituted)
        {
            writer.Append(substituted);
            return;
        }

        switch(expression)
        {
            // A lift needs a task built around a value C# has no other way to wrap; a suppression needs
            // nothing at all, the operand already being the task and this having only kept the sync
            // context from awaiting it.
            case AsyncExpression { IsLift: false } suppression:
                EmitExpression(suppression.Operand, writer, diagnostics);
                break;

            case AsyncExpression lift:
                writer.Append("System.Threading.Tasks.Task.Run(() => ");
                EmitExpression(lift.Operand, writer, diagnostics);
                writer.Append(")");
                break;

            // A SYNC LAMBDA CANNOT BE AWAITED IN, and cannot be promoted either: its delegate type is
            // fixed by the position it fills, so making it async would change that type. The wait
            // therefore blocks. It is the one place this backend cannot honour a sync context with an
            // await, and it is safe here only because nothing installs a synchronization context.
            // The block becomes a lambda C# can run, since it is a body wanted as a value and C# has no
            // other way to spell one. A value task is CONSTRUCTED FROM that task rather than run
            // separately: ValueTask has no Run of its own, and wrapping is what its constructor is for.
            case AsyncBlockExpression asyncBlock:
                writer.Append(asyncBlock.IsValueTask
                    ? $"new {TypeNameEmitter.Emit(asyncBlock.Type)}(System.Threading.Tasks.Task.Run(() =>"
                    : "System.Threading.Tasks.Task.Run(() =>");
                writer.AppendLine();

                using(writer.Block())
                {
                    EmitBlock(asyncBlock.Body, writer, diagnostics);
                }

                writer.Append(asyncBlock.IsValueTask ? "))" : ")");
                break;

            case CloneExpression clone when IntrinsicCalls.ClonesAsItself(clone.Source.Type):
                EmitExpression(clone.Source, writer, diagnostics);
                break;

            case CloneExpression clone:
                EmitSubExpression(clone.Source, PrefixPrecedence, writer, diagnostics);
                writer.Append(".Clone()");
                break;

            case InvocationExpression drop when TaskResult.Dropped(drop) is { } task:
                EmitTaskDrop(drop, task, writer, diagnostics);
                break;

            case InvocationExpression respelled when IntrinsicCalls.Respells(respelled.Method):
                {
                    var access = respelled.Receiver as MemberAccessExpression;

                    writer.Append(IntrinsicCalls.Rewrite(
                        respelled.Method!,
                        access is null ? null : RenderAt(access.Receiver, PostfixPrecedence, diagnostics),
                        access?.Receiver.Type,
                        [.. respelled.Arguments.Select(argument => RenderAt(argument, PostfixPrecedence, diagnostics))]));
                    break;
                }

            case InvocationExpression { Method: { Modifiers.IsIntrinsic: true } unsupplied } missing
                when !IntrinsicSupply.Supplies(unsupplied):
                ReportMissingIntrinsic(IntrinsicSupply.Describe(unsupplied), missing.Node, writer, diagnostics);
                break;

            case InvocationExpression invocation:
                // A PROMOTED callee's task exists only in the emitted C#, so the model carries no
                // await for it and one is written here. See AsyncPromotion.
                if(AsyncPromotion.IsPromoted(invocation.Method))
                {
                    writer.Append("await ");
                }

                if(UnsafeReach.Call(invocation, receiver => RenderAt(receiver, PostfixPrecedence, diagnostics)) is { } reached)
                {
                    writer.Append(reached.Open);
                    EmitArguments(invocation.Arguments, invocation.Method?.ParametersList, writer, diagnostics);
                    writer.Append(reached.Close);
                    break;
                }

                // The member access is emitted from HERE when it carries type arguments, because
                // only the invocation knows which overload they fill -- and a dependent slot is
                // spelled by its carrier rather than by its domain.
                if(invocation.Receiver is MemberAccessExpression callee
                    && callee.TypeArguments is { Count: > 0 })
                {
                    EmitMemberAccess(callee, invocation.Method?.TypeParametersList, writer, diagnostics);
                }
                else
                {
                    EmitSubExpression(invocation.Receiver, PostfixPrecedence, writer, diagnostics);
                }

                writer.Append("(");
                EmitWitnesses(invocation, writer, diagnostics);
                EmitArguments(invocation.Arguments, invocation.Method?.ParametersList, writer, diagnostics);
                writer.Append(")");
                break;

            case MemberAccessExpression memberAccess:
                EmitMemberAccess(memberAccess, parameters: null, writer, diagnostics);
                break;

            // Named on the carrier rather than written as dot-notation, which is what the extension
            // METHOD case relies on. That is not available here: `get_IsEmpty` is a name the user
            // never wrote, so C#'s own extension lookup has nothing to find.
            case ExtensionPropertyAccessExpression extensionProperty:
                writer.Append($"{extensionProperty.CarrierName}.get_{extensionProperty.Property.Name}(");
                EmitExpression(extensionProperty.Receiver, writer, diagnostics);
                writer.Append(")");
                break;

            case CoalesceExpression coalesce:
                EmitSubExpression(coalesce.Left, CoalescePrecedence + 1, writer, diagnostics);
                writer.Append(" ?? ");
                EmitSubExpression(coalesce.Right, CoalescePrecedence, writer, diagnostics);
                break;

            case BinaryExpression binary
                when AnonymousEquality.Applies(binary.OperatorKind, binary.Left.Type, binary.Right.Type):
                writer.Append(AnonymousEquality.Call(
                    binary.OperatorKind,
                    Render(binary.Left, diagnostics),
                    Render(binary.Right, diagnostics)));
                break;

            // Every binary operator Mist has is left-associative, so a right operand of the same precedence is
            // one the source parenthesized: `a - (b - c)` written bare is `(a - b) - c`.
            case BinaryExpression binary when NarrowArithmetic.Promotes(binary.Type):
                writer.Append(NarrowArithmetic.Spell(
                    binary.Type,
                    $"{RenderAt(binary.Left, GetPrecedence(binary.OperatorKind), diagnostics)} {MapBinaryOperator(binary.OperatorKind)} {RenderAt(binary.Right, GetPrecedence(binary.OperatorKind) + 1, diagnostics)}"));
                break;

            case BinaryExpression binary:
                EmitSubExpression(binary.Left, GetPrecedence(binary.OperatorKind), writer, diagnostics);
                writer.Append($" {MapBinaryOperator(binary.OperatorKind)} ");
                EmitSubExpression(binary.Right, GetPrecedence(binary.OperatorKind) + 1, writer, diagnostics);
                break;

            case UnaryExpression unary when NarrowArithmetic.Promotes(unary.Type):
                writer.Append(NarrowArithmetic.Spell(
                    unary.Type, $"{MapUnaryOperator(unary.OperatorKind)}{RenderAt(unary.Operand, PrefixPrecedence, diagnostics)}"));
                break;

            case UnaryExpression unary:
                writer.Append(MapUnaryOperator(unary.OperatorKind));
                EmitSubExpression(unary.Operand, PrefixPrecedence, writer, diagnostics);
                break;

            case IsPatternExpression isPattern:
                EmitIsPattern(isPattern, writer, diagnostics);
                break;

            case SwitchExpression switchExpression:
                EmitSwitch(switchExpression, writer, diagnostics);
                break;

            // A cast TO regex constructs rather than converts: the CLR type has no operator from
            // string, the conversion being Mist's own.
            case CastExpression toRegex when RegexLowering.Is(toRegex.Type):
                writer.Append(RegexLowering.Construct(Render(toRegex.Operand, diagnostics)));
                break;

            case CastExpression { Operator: { } called } toString when CastLowering.ReadsPattern(called, toString.ParameterType):
                writer.Append(CastLowering.Pattern(Render(toString.Operand, diagnostics)));
                break;

            case CastExpression cast:
                // The operand is parenthesized whatever it is. Parentheses written in source bind the
                // expression inside them and reach the model as nothing, so `(double)(a / b)` arrives as
                // a cast of a division, which C# would read back bare as a division of a cast; and C#
                // reads `(Kelvin)-a` as a subtraction unless the type is a keyword.
                writer.Append($"(({TypeNameEmitter.Emit(cast.Type)})({CastLowering.Argument(cast, Render(cast.Operand, diagnostics))}))");
                break;

            case ImplicitConversionExpression conversion:
                writer.Append(CastLowering.Implicit(conversion, Render(conversion.Operand, diagnostics)));
                break;

            case AsExpression asExpression:
                EmitAs(asExpression, writer, diagnostics);
                break;

            case AssignmentExpression assignment
                when UnsafeReach.Assignment(assignment, operand => RenderAt(operand, PostfixPrecedence, diagnostics)) is { } written:
                writer.Append(written);
                break;

            case AssignmentExpression assignment:
                if(assignment.Target is LocalReferenceExpression assigned)
                {
                    writer.Append(LocalNames.Of(assigned.Local));
                }
                else
                {
                    EmitExpression(assignment.Target, writer, diagnostics);
                }

                writer.Append(assignment.IsCoalescing ? " ??= " : " = ");
                UnitHoisting.Refuse(assignment.Value, diagnostics);
                EmitExpression(assignment.Value, writer, diagnostics);
                break;

            case IndexExpression elementAccess
                when UnsafeReach.Index(elementAccess, operand => RenderAt(operand, PostfixPrecedence, diagnostics)) is { } reachedIndex:
                writer.Append(reachedIndex);
                break;

            case IndexExpression elementAccess:
                EmitSubExpression(elementAccess.Receiver, PostfixPrecedence, writer, diagnostics);
                writer.Append("[");
                EmitExpression(elementAccess.Index, writer, diagnostics);
                writer.Append("]");
                break;

            case RangeExpression range:
                if(range.Start is { } rangeStart)
                {
                    EmitSubExpression(rangeStart, PrefixPrecedence, writer, diagnostics);
                }

                writer.Append("..");

                if(range.End is { } rangeEnd)
                {
                    EmitSubExpression(rangeEnd, PrefixPrecedence, writer, diagnostics);
                }

                break;

            case ArrayCreationExpression arrayCreation:
                writer.Append($"new {TypeNameEmitter.Emit(arrayCreation.ElementType)}[");
                EmitExpression(arrayCreation.Size, writer, diagnostics);
                writer.Append("]");
                break;

            case StackallocExpression allocation:
                writer.Append($"stackalloc {TypeNameEmitter.Emit(allocation.ElementType)}[");
                EmitExpression(allocation.Size, writer, diagnostics);
                writer.Append("]");
                break;

            case ArrayLiteralExpression arrayLiteral:
                // Emitted with an explicit element type rather than as a C# collection
                // expression so the target type is never required at the use site. An ANONYMOUS element
                // is the exception it cannot be: C# has no name for that type, so the only spelling is
                // the implicit one, which reads the element type back off the elements.
                writer.Append(arrayLiteral.ElementType is AnonymousType
                    ? "new[]"
                    : $"new {TypeNameEmitter.Emit(arrayLiteral.ElementType)}[]");
                writer.Append(arrayLiteral.Elements.Count == 0 ? " { }" : " { ");
                EmitList.Separated(arrayLiteral.Elements, ", ", writer, element => EmitExpression(element, writer, diagnostics));

                if(arrayLiteral.Elements.Count > 0)
                {
                    writer.Append(" }");
                }

                break;

            case TupleLiteralExpression tupleLiteral:
                EmitTupleLiteral(tupleLiteral, writer, diagnostics);
                break;

            case AnonymousObjectExpression anonymous:
                EmitAnonymousObject(anonymous, writer, diagnostics);
                break;

            case LambdaExpression lambda:
                EmitLambda(lambda, writer, diagnostics);
                break;

            case InterpolatedStringExpression interpolated:
                writer.Append("$\"");
                foreach(var part in interpolated.Parts)
                {
                    switch(part)
                    {
                        case InterpolatedStringText text:
                            writer.Append(text.Text);
                            break;
                        case InterpolatedStringHole hole:
                            UnitHoisting.Refuse(hole.Expression, diagnostics);
                            writer.Append("{");

                            // An optional's absence lowers to null, which interpolation prints
                            // as the empty string; Mist prints it as the unit value's name.
                            if(OptionalUnion.IsOptional(hole.Expression.Type))
                            {
                                writer.Append("(object?)");
                                EmitExpression(hole.Expression, writer, diagnostics);
                                writer.Append(" ?? \"Void\"");
                            }
                            else
                            {
                                EmitExpression(hole.Expression, writer, diagnostics);
                            }

                            writer.Append("}");
                            break;
                    }
                }

                writer.Append("\"");
                break;

            // Outside an argument list a spread stands for the whole value, which is what a
            // whole-pack assignment means: `Item... = values...` moves one tuple to another of the
            // same shape, and writing the elements out would say the same thing at length.
            case SpreadExpression spread:
                EmitExpression(spread.Operand, writer, diagnostics);
                break;

            case TypeReferenceExpression typeRef:
                writer.Append(
                    DependentCarrierNames.ValueRead(typeRef.ReferencedType)
                        ?? TypeNameEmitter.Emit(typeRef.ReferencedType));
                break;

            case MethodReferenceExpression methodRef:
                writer.Append(
                    ReceiverSpelling.Qualifier(methodRef.Method.DeclaringType, methodRef.Method.Modifiers.IsStatic)
                    + CalleeName(LocalNames.Of(methodRef.Method), methodRef.TypeArguments, methodRef.Method.TypeParametersList)
                    + EmitTypeArguments(methodRef.TypeArguments, methodRef.Method.TypeParametersList));
                break;

            case FunctionReferenceExpression functionRef
                when UnsafeReach.Function(functionRef, operand => RenderAt(operand, PostfixPrecedence, diagnostics)) is { } reachedFunction:
                writer.Append(reachedFunction);
                break;

            case FunctionReferenceExpression functionRef:
                if(functionRef.Receiver is { } functionTarget)
                {
                    EmitExpression(functionTarget, writer, diagnostics);
                    writer.Append(".");
                }
                else
                {
                    writer.Append(ReceiverSpelling.Qualifier(functionRef.Method.DeclaringType, functionRef.Method.Modifiers.IsStatic));
                }

                writer.Append(LocalNames.Of(functionRef.Method));
                break;

            case MethodGroupExpression methodGroup:
                diagnostics.ReportAtNode(
                    BackendDiagnostics.MethodGroupReachedEmission, methodGroup.Node, null, methodGroup.MemberName);
                writer.Append($"/* unresolved method group: {methodGroup.MemberName} */");
                break;

            case LocalReferenceExpression localRef:
                writer.Append(LocalNames.Of(localRef.Local));

                if(localRef.IsNarrowed && OptionalUnion.IsOptional(localRef.Local.Type) && TypeNameEmitter.IsValueKind(localRef.Type))
                {
                    writer.Append(".Value");
                }

                break;

            case ThisExpression:
                writer.Append(ReceiverSpelling.This);
                break;

            case FieldReferenceExpression fieldRef:
                writer.Append(
                    UnsafeReach.Implicit(fieldRef.Field)
                    ?? ReceiverSpelling.Qualifier(fieldRef.Field.DeclaringType, fieldRef.Field.Modifiers.IsStatic) + fieldRef.Field.Name);
                break;
            case PropertyReferenceExpression propertyRef:
                writer.Append(
                    UnsafeReach.Implicit(propertyRef.Property)
                    ?? ReceiverSpelling.Qualifier(propertyRef.Property.DeclaringType, propertyRef.Property.Modifiers.IsStatic) + propertyRef.Property.Name);
                break;
            case BackingFieldReferenceExpression:
                writer.Append("field");
                break;

            case NewExpression newExpression:
                EmitNew(newExpression, writer, diagnostics);
                break;

            case LiteralExpression literal:
                writer.Append(EmitLiteral(literal));
                break;

            case VoidLiteralExpression voidLiteral:
                writer.Append(
                    voidLiteral.Reading == VoidReading.Absence ? "null" : "Standard.Void.Instance");
                break;

            case RegexLiteralExpression regexLiteral:
                writer.Append(RegexLowering.Construct(RegexLowering.Quote(regexLiteral.Pattern)));
                break;


            case TypeLiteralExpression typeLiteral:
                writer.Append($"typeof({TypeNameEmitter.Emit(typeLiteral.ReferencedType)})");
                break;

            case DefaultExpression @default when DefaultSynthesis.Expression(@default.Type) is { } value:
                writer.Append(value);
                break;

            default:
                // Still written, invalid though it is, so the emitted file stays readable for whoever
                // is diagnosing the gap. The build stops before Roslyn ever parses it.
                //
                // The AST node's kind rather than the bound expression's: a sentinel's own type says
                // only that it is a sentinel, whereas the node says which construct went unbound,
                // which is the thing worth knowing. Falls back to the bound kind for anything that
                // reached here without being a sentinel, where the bound type IS the answer.
                var unsupportedKind = expression is UnresolvedExpression
                    ? expression.Node.GetType().Name
                    : expression.GetType().Name
                    ;

                diagnostics.ReportAtNode(BackendDiagnostics.UnsupportedExpression, expression.Node, null);
                writer.Append($"/* unsupported: {unsupportedKind} */");
                break;
        }
    }

    /// <summary>
    /// The default of a type as C# text, or null where that default is a CONSTRUCTED value rather than a
    /// literal one. A class or struct defaults to an instance built by a synthesized factory, which is
    /// not emitted yet; returning null leaves the expression to the sentinel below, so the gap is
    /// reported against Mist source instead of emitted as C# that would not compile.
    /// <para>
    /// An optional defaults to ABSENCE rather than to a defaulted inner value, since absence is the
    /// reading that exists for every inner type and needs nothing constructed. Wider unions keep their
    /// carrier and are left to the sentinel: which constituent a carrier defaults to is unanswered.
    /// </para>
    /// </summary>
    internal static string? EmitTypeDefault(IMistType type)
    {
        if(OptionalUnion.IsOptional(type))
        {
            return "null";
        }

        if(type is not ConcreteType { IsIntrinsic: true })
        {
            return null;
        }

        // Mist's string default is the empty string where C#'s is null, so this is the one intrinsic
        // whose default cannot be spelled by deferring to C#. The rest agree, and naming the C# form
        // keeps their widths and signedness in one place rather than restating a zero per type.
        return type.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName
            ? "\"\""
            : $"default({TypeNameEmitter.Emit(type)})";
    }

    private static void EmitAnonymousObject(
        AnonymousObjectExpression anonymous, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var names = anonymous.AnonymousType.MemberNames;

        writer.Append("new { ");

        for(var i = 0; i < anonymous.Values.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            writer.Append($"{names[i]} = ");
            EmitExpression(anonymous.Values[i], writer, diagnostics);
        }

        writer.Append(" }");
    }

    private static void EmitTupleLiteral(
        TupleLiteralExpression tupleLiteral, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Append("(");

        for(var i = 0; i < tupleLiteral.Elements.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            if(tupleLiteral.ElementNames.Count > i && tupleLiteral.ElementNames[i] is { } name)
            {
                writer.Append($"{name}: ");
            }

            EmitExpression(tupleLiteral.Elements[i], writer, diagnostics);
        }

        writer.Append(")");
    }

    /// <summary>
    /// Parameters are always spelled with their types, even where source left them implicit. The types
    /// are the ones the MODEL resolved, so this stops C# inferring its own from the target and reaching a
    /// different answer -- and it costs nothing, C# accepting an explicit list wherever it accepts an
    /// implicit one.
    /// </summary>
    /// <summary>
    /// Whether an await inside the construct being emitted has to block. False in a method body, which
    /// <see cref="AsyncPromotion"/> makes async so the await can be real; true inside a lambda whose
    /// delegate type is synchronous.
    /// </summary>
    [ThreadStatic]
    private static bool blockingAwaits;

    private static void EmitLambda(
        LambdaExpression lambda, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var enclosing = blockingAwaits;
        blockingAwaits = TaskResult.Of(ResultTypeOf(lambda)) is null;

        try
        {
            EmitLambdaBody(lambda, writer, diagnostics);
        }
        finally
        {
            blockingAwaits = enclosing;
        }
    }

    /// <summary>
    /// What the lambda's delegate says it returns, which is what decides whether an await inside it can
    /// be a real one. Falls back to the body's own type where the position fixed no delegate.
    /// </summary>
    private static IMistType ResultTypeOf(LambdaExpression lambda)
    {
        return lambda.Type is IFunctionType function
            ? function.ReturnType
            : lambda.Result?.Type ?? lambda.Type
            ;
    }

    private static void EmitLambdaBody(
        LambdaExpression lambda, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var parameters = lambda.Parameters.Select(
            parameter => $"{TypeNameEmitter.Emit(parameter.Type)} {LocalNames.Of(parameter)}");

        writer.Append(lambda.IsAsync ? "async " : "");
        writer.Append(lambda.IsStatic ? "static (" : "(");
        writer.Append($"{string.Join(", ", parameters)}) => ");

        if(lambda.Result is { } result)
        {
            EmitExpression(result, writer, diagnostics);
            return;
        }

        writer.AppendLine();

        using(writer.Block())
        {
            if(lambda.Body is { } body)
            {
                EmitBlock(body, writer, diagnostics);
            }
        }
    }

    private static void EmitIsPattern(
        IsPatternExpression isPattern, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(isPattern.Scrutinee.Type is UnionType union && !OptionalUnion.IsOptional(union))
        {
            EmitUnionNarrowing(isPattern, union, writer, diagnostics);
            return;
        }

        if(EnumVariantTested(isPattern.Pattern) is { } variant)
        {
            EmitVariantComparison(isPattern.Scrutinee, variant, writer, diagnostics);
            return;
        }

        var (patternText, hoisted) = RenderPattern(isPattern.Pattern, diagnostics);

        // Parenthesized only when there are hoisted tests to need it, so an `is` without them keeps the
        // shape and the precedence the surrounding expression was emitted against. With them the whole
        // thing binds as a conjunction, which C# ranks BELOW the equality an `is` may sit under.
        if(hoisted.Count > 0)
        {
            writer.Append("(");
        }

        EmitSubExpression(isPattern.Scrutinee, RelationalPrecedence, writer, diagnostics);
        writer.Append($" is {patternText}");

        if(hoisted.Count > 0)
        {
            EmitHoistedTests(hoisted, " && ", writer, diagnostics);
            writer.Append(")");
        }
    }

    /// <summary>
    /// A pattern written to a writer of its own, so a caller can learn what the walk hoisted out of it
    /// before committing anything ahead of the pattern. Safe because pattern emission never indents or
    /// breaks a line -- it only appends.
    /// </summary>
    private static (string Text, List<HoistedTest> Hoisted) RenderPattern(
        Pattern pattern, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        var hoisted = new List<HoistedTest>();

        EmitPattern(pattern, 0, hoisted, scratch, diagnostics);

        return (scratch.ToString(), hoisted);
    }

    private static void EmitHoistedTests(
        List<HoistedTest> hoisted, string lead, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var test in hoisted)
        {
            writer.Append($"{lead}{test.Text}");
            lead = " && ";
        }

        _ = diagnostics;
    }

    private static void EmitVariantComparison(
        Expression scrutinee, Expression variant, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Append("(");
        EmitSubExpression(scrutinee, GetPrecedence(BinaryOperatorKind.Equal), writer, diagnostics);
        writer.Append(" == ");
        EmitExpression(variant, writer, diagnostics);
        writer.Append(")");
    }

    /// <summary>
    /// An enum variant lifted out of a tuple element into a condition beside the pattern, because C#
    /// admits only a compile-time constant in a pattern and a Mist variant lowers to a static readonly
    /// field. <see cref="Bound"/> names what the element position binds instead, so the comparison has
    /// something to read.
    /// </summary>
    /// <param name="Text">
    /// The test as C#, rendered where it was hoisted rather than at the guard, because the two
    /// kinds that need hoisting are not the same shape: an enum variant is an equality against an
    /// expression, a regex is a call.
    /// </param>
    private readonly record struct HoistedTest(string Text);

    /// <summary>
    /// A union carrier stands in no relation to its own constituents, so a C# type test against one
    /// answers false for every one of them. The tag check goes into the SCRUTINEE instead, through the
    /// carrier accessor for the constituent the test picked out, which leaves an ordinary C# pattern to
    /// test and BIND the accessor's result -- so a narrowed binding is usable in the condition that
    /// introduced it, where hoisting it into the branch would put it out of reach.
    /// </summary>
    private static void EmitUnionNarrowing(
        IsPatternExpression isPattern, UnionType union, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        // A catch-all discriminates nothing, so it matches the carrier as it stands.
        if(isPattern.Pattern is CatchAllPattern)
        {
            EmitSubExpression(isPattern.Scrutinee, RelationalPrecedence, writer, diagnostics);
            writer.Append(" is ");
            EmitPattern(isPattern.Pattern, 0, hoisted: null, writer, diagnostics);
            return;
        }

        if(isPattern.Pattern is not TypeTestPattern { Constituent: { } constituent })
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedUnionPattern, isPattern.Node, null, union.Name);
            writer.Append($"/* unsupported: pattern against union {union.Name} */");
            return;
        }

        EmitSubExpression(isPattern.Scrutinee, PrefixPrecedence, writer, diagnostics);
        writer.Append($".{UnionCarrierNames.NarrowingAccessor(constituent)} is ");
        EmitPattern(isPattern.Pattern, 0, hoisted: null, writer, diagnostics);
    }

    /// <summary>
    /// Emits a pattern, parenthesizing it when it binds looser than the combinator applying to it.
    /// Needed for the same reason the expression side needs it: the model carries no grouping, so
    /// <c>(a or b) and c</c> arrives as a conjunction OF a disjunction and written bare would be C# for
    /// <c>a or (b and c)</c>. Mist and C# rank these identically -- or, then and, then not.
    /// </summary>
    private static void EmitSwitch(
        SwitchExpression switchExpression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var narrowed = switchExpression.Scrutinee.Type is UnionType union && !OptionalUnion.IsOptional(union)
            ? union
            : null
            ;

        EmitSubExpression(switchExpression.Scrutinee, SwitchPrecedence + 1, writer, diagnostics);
        writer.Append(" switch { ");

        EmitList.Separated(switchExpression.Arms, ", ", writer, arm =>
            EmitArm(arm, narrowed, writer, diagnostics));

        writer.Append(" }");
    }

    private static void EmitArm(
        SwitchArm arm, UnionType? narrowed, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var hoisted = new List<HoistedTest>();

        if(narrowed is { } union)
        {
            EmitUnionArmPattern(arm.Pattern, union, writer, diagnostics);
        }
        else if(EnumVariantTested(arm.Pattern) is { } variant)
        {
            var bound = TemporaryName();
            writer.Append($"var {bound}");
            hoisted.Add(new HoistedTest($"{bound} == {Render(variant, diagnostics)}"));
        }
        else
        {
            var (patternText, nested) = RenderPattern(arm.Pattern, diagnostics);
            writer.Append(patternText);
            hoisted.AddRange(nested);
        }

        if(hoisted.Count > 0)
        {
            writer.Append(" when");
            EmitHoistedTests(hoisted, " ", writer, diagnostics);
        }

        if(arm.Guard is { } guard)
        {
            writer.Append(hoisted.Count > 0 ? " && " : " when ");
            EmitSubExpression(guard, GetPrecedence(BinaryOperatorKind.LogicalAnd), writer, diagnostics);
        }

        writer.Append(" => ");
        EmitExpression(arm.Result, writer, diagnostics);
    }

    /// <summary>
    /// The arm of a switch over a union, which stays an ARM: the tag check becomes a property pattern on
    /// the carrier, so one C# scrutinee serves every arm while each still reads its own constituent
    /// accessor -- <c>int n</c> emits as <c>{ AsInt32OrNull: int n }</c>. The accessor is
    /// <see cref="EmitUnionNarrowing"/>'s, and discriminates for the reason given there.
    /// <para>
    /// A CHAIN OF CONDITIONALS is the other shape and both of its defects were measured rather than
    /// reasoned. C# scopes a pattern variable to the whole enclosing statement instead of to the
    /// conditional that introduced it, so two arms binding one name -- legal Mist, since every arm binds
    /// in a scope of its own -- collide on CS0128. And a chain has nowhere to bind the scrutinee, so it
    /// re-emits it per arm and runs a side-effecting one once per test.
    /// </para>
    /// <para>
    /// Staying an arm also leaves first-match ordering, the <c>when</c> guard and the no-match throw to
    /// C#, each of which a chain would have to rebuild.
    /// </para>
    /// </summary>
    private static void EmitUnionArmPattern(
        Pattern pattern, UnionType union, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(pattern is CatchAllPattern)
        {
            EmitPattern(pattern, 0, hoisted: null, writer, diagnostics);
            return;
        }

        if(pattern is not TypeTestPattern { Constituent: { } constituent })
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedUnionPattern, pattern.Node, null, union.Name);
            writer.Append($"var _ /* unsupported: pattern against union {union.Name} */");
            return;
        }

        writer.Append($"{{ {UnionCarrierNames.NarrowingAccessor(constituent)}: ");
        EmitPattern(pattern, 0, hoisted: null, writer, diagnostics);
        writer.Append(" }");
    }

    /// <summary>
    /// The operand is emitted ONCE in every form, which is what makes a side-effecting one safe. C#'s two
    /// runtime-checked spellings each bind what they tested -- <c>as</c> answers the value or null, an
    /// <c>is</c> pattern binds it -- so neither wants the operand again; and a union needs no test at all,
    /// because the carrier's narrowing accessor already answers the constituent or absent, which is
    /// exactly what this expression's type says it produces.
    /// <para>
    /// Two spellings rather than one because C#'s <c>as</c> demands a reference or nullable target, so a
    /// value target goes through a pattern instead. <see cref="TypeNameEmitter.IsValueKind"/> decides,
    /// being the same authority that spells the optional this produces.
    /// </para>
    /// </summary>
    private static void EmitAs(
        AsExpression asExpression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(asExpression.Kind is AsConversionKind.Identity)
        {
            EmitExpression(asExpression.Operand, writer, diagnostics);
            return;
        }

        var target = TypeNameEmitter.Emit(asExpression.TargetType);

        if(asExpression.Kind is AsConversionKind.Widening)
        {
            writer.Append($"(({target})");
            EmitSubExpression(asExpression.Operand, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        var accessor = asExpression.Constituent is { } constituent
            ? $".{UnionCarrierNames.NarrowingAccessor(constituent)}"
            : null
            ;

        var checkedAtRuntime = accessor is null
            || asExpression.Constituent?.FullyQualifiedName != asExpression.TargetType.FullyQualifiedName
            ;

        if(checkedAtRuntime)
        {
            writer.Append("(");
        }

        EmitSubExpression(
            asExpression.Operand,
            accessor is null ? RelationalPrecedence : PrefixPrecedence,
            writer,
            diagnostics);

        if(accessor is not null)
        {
            writer.Append(accessor);
        }

        if(!checkedAtRuntime)
        {
            return;
        }

        if(!TypeNameEmitter.IsValueKind(asExpression.TargetType))
        {
            writer.Append($" as {target})");
            return;
        }

        var bound = $"__as{asExpression.Node.Span.Start}";
        writer.Append($" is {target} {bound} ? {bound} : default({target}?))");
    }

    private static void EmitCoerced(
        Expression expression, IMistType? target, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(DelegateLowering.NeedsConstruction(expression.Type, target))
        {
            writer.Append($"new {TypeNameEmitter.Emit(target!)}({Render(expression, diagnostics)})");
            return;
        }

        EmitExpression(expression, writer, diagnostics);
    }

    private static string Render(Expression expression, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        EmitExpression(expression, scratch, diagnostics);
        return scratch.ToString();
    }

    /// <summary>
    /// A task's Drop is the one call this backend does not write as a call: the CLR completes a task
    /// by awaiting it, and no CLR task has a Drop. A conditional access narrows first, C# having no
    /// await of a null -- absent answers the type's default, present is awaited -- and inside a sync
    /// lambda the wait blocks, as every await there does.
    /// </summary>
    private static void EmitTaskDrop(InvocationExpression drop, Expression task, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(drop.Receiver is MemberAccessExpression { IsConditional: true })
        {
            var present = PresentName(drop);
            writer.Append("(");
            EmitSubExpression(task, PrefixPrecedence, writer, diagnostics);
            writer.Append($" is {{ }} {present} ? {Awaited(present)} : default({TypeNameEmitter.Emit(drop.Type)}))");
            return;
        }

        if(blockingAwaits)
        {
            EmitSubExpression(task, PostfixPrecedence, writer, diagnostics);
            writer.Append(".GetAwaiter().GetResult()");
            return;
        }

        writer.Append("await ");
        EmitSubExpression(task, PrefixPrecedence, writer, diagnostics);
    }

    private static string PresentName(InvocationExpression drop)
    {
        return $"__present{drop.Node.Span.Start}";
    }

    private static string Awaited(string name)
    {
        return blockingAwaits ? $"{name}.GetAwaiter().GetResult()" : $"await {name}";
    }

    private static string? DiscardedCombinator(Expression statement, DiagnosticBag diagnostics)
    {
        return TaskResult.Dropped(statement) is InvocationExpression { Method: { } callee, Arguments: [{ } tasks] }
            && TaskCombinators.Combines(callee)
            ? TaskCombinators.Discarded(callee, Render(tasks, diagnostics))
            : null;
    }

    private static string RenderAt(Expression expression, int operatorPrecedence, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        EmitSubExpression(expression, operatorPrecedence, scratch, diagnostics);
        return scratch.ToString();
    }

    private static void ReportMissingIntrinsic(
        string member, Storm.Runtime.SyntaxNode node, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        diagnostics.ReportAtNode(BackendDiagnostics.MissingIntrinsicEmissionTemplate, node, null, member);
        writer.Append($"/* missing intrinsic: {member} */");
    }

    [ThreadStatic]
    private static int temporaries;

    /// <summary>
    /// A name for a value a pattern has to bind in order to be TESTED -- an enum variant compared in
    /// a guard, a regex matched against.
    /// <para>
    /// COUNTED RATHER THAN DERIVED FROM THE NODE, because one node can be emitted at many sites. A
    /// refinement's predicate is written once in its alias and reached by every test of that
    /// refinement, so a name taken from its span repeats -- two `is Email` tests in one scope
    /// declared the same local. Deterministic because emission walks in a fixed order and the count
    /// starts over for each program, so what a process emitted before it leaves no mark; thread-static
    /// so parallel emissions cannot hand out the same number.
    /// </para>
    /// </summary>
    private static string TemporaryName()
    {
        return $"__e{++temporaries}";
    }

    public static void ResetTemporaries()
    {
        temporaries = 0;
    }

    /// <summary>
    /// The enum variant a pattern tests BARE, which the two positions a pattern can occupy then lower
    /// differently -- an <c>is</c> becomes the comparison outright, an arm binds the scrutinee and
    /// compares in a <c>when</c> guard, because an arm admits a pattern and only a pattern. The arm's
    /// binding names what the switch already evaluated, so a side-effecting scrutinee is not run twice,
    /// and its name carries the pattern's SOURCE OFFSET: an arm ordinal would be unique within one
    /// switch and collide with the enclosing one's for a switch nested inside an arm's result, which C#
    /// rejects outright rather than shadowing.
    /// <para>
    /// The <c>is</c> form is parenthesized because the equality replaces an <c>is</c> and the two do NOT
    /// rank alike in C#: type testing outranks equality, so <c>flag == d is Direction.North</c> -- which
    /// Mist parses with the test on the right -- would re-associate to
    /// <c>(flag == d) == Direction.North</c> written bare.
    /// </para>
    /// <para>
    /// A combinator answers null here and falls through to <see cref="EmitComparison"/>, which refuses
    /// it: the comparison is an expression and an expression cannot sit inside a C# pattern.
    /// </para>
    /// </summary>
    private static Expression? EnumVariantTested(Pattern pattern)
    {
        return pattern is ComparisonPattern { Kind: ComparisonKind.Equal, Bound.Type: EnumType } comparison
            ? comparison.Bound
            : null
            ;
    }

    /// <summary>
    /// <paramref name="hoisted"/> null means an enum variant nested in this pattern may NOT be lifted
    /// into a condition beside it, which <c>or</c> and <c>not</c> impose: the element position would have
    /// to bind for the comparison to read it, and neither establishes a binding. Cleared rather than
    /// checked afterwards so the element falls to <see cref="EmitComparison"/> and is refused there,
    /// instead of emitting a pattern whose comparison silently went nowhere.
    /// </summary>
    private static void EmitPattern(
        Pattern pattern,
        int combinatorPrecedence,
        List<HoistedTest>? hoisted,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var parenthesize = PatternPrecedence(pattern) < combinatorPrecedence;

        if(parenthesize)
        {
            writer.Append("(");
        }

        switch(pattern)
        {
            case TypeTestPattern typeTest:
                EmitTypeTest(typeTest, hoisted, writer, diagnostics);
                break;

            case RegexPattern regex:
                EmitRegexPattern(regex, hoisted, writer);
                break;

            case CatchAllPattern catchAll:
                writer.Append(catchAll.Binding is { } caught ? $"var {LocalNames.Of(caught)}" : "var _");
                break;

            case ComparisonPattern comparison:
                EmitComparison(comparison, writer, diagnostics);
                break;

            case NegatedPattern negated:
                writer.Append("not ");
                EmitPattern(negated.Operand, NegationPrecedence, hoisted: null, writer, diagnostics);
                break;

            case ConjunctionPattern conjunction:
                EmitList.Separated(conjunction.Operands, " and ", writer, operand =>
                    EmitPattern(operand, ConjunctionPrecedence, hoisted, writer, diagnostics));
                break;

            case DisjunctionPattern disjunction:
                EmitList.Separated(disjunction.Operands, " or ", writer, operand =>
                    EmitPattern(operand, DisjunctionPrecedence, hoisted: null, writer, diagnostics));
                break;

            case PropertyPattern property:
                EmitPropertyPattern(property, hoisted, writer, diagnostics);
                break;

            case TuplePattern tuple:
                EmitTuplePattern(tuple, hoisted, writer, diagnostics);
                break;

            case ListPattern list:
                EmitListPattern(list, hoisted, writer, diagnostics);
                break;
        }

        if(parenthesize)
        {
            writer.Append(")");
        }
    }

    /// <summary>
    /// A test that the scrutinee holds a type, and -- where that type is a REFINEMENT -- that its
    /// predicate holds of the value.
    /// <para>
    /// THE PREDICATE CANNOT BE ERASED HERE, though the type name everywhere else can. `x is AdultAge`
    /// asks a question that the refinement is the whole of: spelled as its base it becomes `x is int`,
    /// which is true of every int and silently turns a runtime check into a constant. Emitted as a
    /// conjunction rather than woven into the type name because a relational predicate is not
    /// something a declaration pattern can carry -- `int >= 18 age` is not C# -- while
    /// `int and >= 18 and var age` is, at every shape of predicate.
    /// </para>
    /// </summary>
    private static void EmitTypeTest(
        TypeTestPattern typeTest, List<HoistedTest>? hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(typeTest.NarrowedType is RefinedType unbound && typeTest.Refinement is null)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnboundRefinementPredicate, typeTest.Node, null,
                unbound.Name, unbound.BaseType.Name);
        }

        writer.Append(TypeNameEmitter.EmitAsPatternType(typeTest.NarrowedType));

        if(typeTest.Refinement is { } predicate)
        {
            writer.Append(" and ");

            // The predicate's own hoists travel OUT of the pattern with everything else's. A
            // refinement whose predicate is a regex has no pattern form at all, so dropping them
            // here would leave the type test alone -- true of every value of the base type.
            EmitPattern(predicate, ConjunctionPrecedence, hoisted, writer, diagnostics);

            if(typeTest.Binding is { } bound)
            {
                writer.Append($" and var {LocalNames.Of(bound)}");
            }

            return;
        }

        if(typeTest.Binding is { } tested)
        {
            writer.Append($" {LocalNames.Of(tested)}");
        }
    }

    /// <summary>
    /// A regex in pattern position, which C# has no pattern for: it binds a temporary and hoists
    /// <c>Regex.IsMatch</c> into the guard the caller writes after the pattern.
    /// <para>
    /// FULL MATCH, because that is what Mist means by a regex pattern -- <c>s is `^[a-z]+$`</c> asks
    /// whether s IS that shape, not whether it contains one.
    /// </para>
    /// </summary>
    private static void EmitRegexPattern(RegexPattern regex, List<HoistedTest>? hoisted, CSharpWriter writer)
    {
        var bound = TemporaryName();
        writer.Append($"var {bound}");

        hoisted?.Add(new HoistedTest(
            RegexLowering.IsFullMatch(bound, RegexLowering.Quote(regex.Pattern))));
    }

    private static void EmitTuplePattern(
        TuplePattern tuple, List<HoistedTest>? hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(tuple.TestedType is { } tested)
        {
            writer.Append(TypeNameEmitter.EmitAsPatternType(tested));
        }

        writer.Append("(");

        for(var i = 0; i < tuple.Elements.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            var element = tuple.Elements[i];

            if(hoisted is not null && EnumVariantTested(element) is { } variant)
            {
                var bound = TemporaryName();
                writer.Append($"var {bound}");
                hoisted.Add(new HoistedTest($"{bound} == {Render(variant, diagnostics)}"));
                continue;
            }

            EmitPattern(element, 0, hoisted, writer, diagnostics);
        }

        writer.Append(")");

        if(tuple.Binding is { } whole)
        {
            writer.Append($" {LocalNames.Of(whole)}");
        }
    }

    /// <summary>
    /// A positional element threads <paramref name="hoisted"/> as a tuple element does, being the same
    /// kind of binding site. A SLICE clears it: what a slice matches is a sub-sequence, so lifting an
    /// enum variant out of one would compare a sub-sequence against a variant, and clearing sends the
    /// variant to <see cref="EmitComparison"/> to be refused rather than emitting that.
    /// </summary>
    private static void EmitListPattern(
        ListPattern list, List<HoistedTest>? hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Append("[");

        for(var i = 0; i < list.Elements.Count; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            var element = list.Elements[i];

            if(element.IsSlice)
            {
                writer.Append("..");

                if(element.Subpattern is { } slice)
                {
                    writer.Append(" ");
                    EmitPattern(slice, 0, hoisted: null, writer, diagnostics);
                }

                continue;
            }

            if(element.Subpattern is not { } positional)
            {
                continue;
            }

            if(hoisted is not null && EnumVariantTested(positional) is { } variant)
            {
                var bound = TemporaryName();
                writer.Append($"var {bound}");
                hoisted.Add(new HoistedTest($"{bound} == {Render(variant, diagnostics)}"));
                continue;
            }

            EmitPattern(positional, 0, hoisted, writer, diagnostics);
        }

        writer.Append("]");
    }

    private static void EmitComparison(
        ComparisonPattern comparison, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(comparison.Bound.Type is EnumType)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedEnumPattern, comparison.Node, null,
                VariantName(comparison.Bound));
            writer.Append("/* unsupported: enum variant in a C# pattern */");
            return;
        }

        if(comparison.Kind != ComparisonKind.Equal)
        {
            writer.Append($"{MapComparison(comparison.Kind)} ");
        }

        EmitExpression(comparison.Bound, writer, diagnostics);
    }

    private static string VariantName(Expression variant)
    {
        return variant is MemberAccessExpression access
            ? $"{access.Receiver.Type.Name}.{access.MemberName}"
            : variant.Type.Name
            ;
    }

    private static void EmitPropertyPattern(
        PropertyPattern property,
        List<HoistedTest>? hoisted,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(property.Subpatterns.Count == 0)
        {
            writer.Append("{ }");
            return;
        }

        if(property.NarrowedType is UnknownType)
        {
            EmitUnknownPropertyPattern(property, hoisted, writer, diagnostics);
            return;
        }

        writer.Append("{ ");

        EmitList.Separated(property.Subpatterns, ", ", writer, subpattern =>
        {
            writer.Append($"{subpattern.MemberName}: ");
            EmitPattern(subpattern.Pattern, 0, hoisted: null, writer, diagnostics);
        });

        writer.Append(" }");
    }

    /// <summary>
    /// C# CANNOT PROPERTY-MATCH AN OBJECT, the member names being resolved at compile time against a
    /// type that by then has none. So the pattern becomes a null test that BINDS the scrutinee, plus one
    /// test per member beside it reading that binding through <c>dynamic</c> -- which defers the read,
    /// and with it the whole subpattern, to the runtime binder. Every subpattern shape therefore
    /// survives, including the relational and constant ones C# would refuse against an object.
    /// <para>
    /// Each member test goes into the condition rather than ahead of the branch, which is where classic
    /// puts it. One evaluation of the scrutinee either way, and the binding then reaches the REST OF THE
    /// CONDITION as well as the branch -- so a later conjunct may read it, which a statement hoisted
    /// into the branch cannot offer.
    /// </para>
    /// <para>
    /// A MISSING MEMBER THROWS rather than failing the match, exactly as it does on classic: nothing
    /// here checks the shape, because there is no shape to check against.
    /// </para>
    /// </summary>
    private static void EmitUnknownPropertyPattern(
        PropertyPattern property,
        List<HoistedTest>? hoisted,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(hoisted is null)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedUnknownPropertyPattern, property.Node, null);
            writer.Append("/* unsupported: property pattern on unknown under or/not */");
            return;
        }

        var bound = TemporaryName();
        writer.Append($"not null and var {bound}");

        foreach(var subpattern in property.Subpatterns)
        {
            var (text, nested) = RenderPattern(subpattern.Pattern, diagnostics);

            hoisted.Add(new HoistedTest($"((dynamic){bound}).{subpattern.MemberName} is {text}"));
            hoisted.AddRange(nested);
        }
    }

    private static int PatternPrecedence(Pattern pattern)
    {
        return pattern switch
        {
            DisjunctionPattern => DisjunctionPrecedence,
            ConjunctionPattern => ConjunctionPrecedence,
            TypeTestPattern { Refinement: not null } => ConjunctionPrecedence,
            NegatedPattern => NegationPrecedence,
            _ => int.MaxValue,
        };
    }

    private static string MapComparison(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => "<",
            ComparisonKind.GreaterThan => ">",
            ComparisonKind.LessThanOrEqual => "<=",
            ComparisonKind.GreaterThanOrEqual => ">=",
            _ => "==",
        };
    }

    private static void EmitNew(NewExpression newExpression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        // A parameterized construction on a type parameter calls the factory the constraint grants.
        // C# picks between its overloads by argument type, which is what lets one parameter carry
        // new(int) and new(bool) at once without this emitter choosing between them.
        if(ArrayLowering.Generated(newExpression.Type, newExpression.Arguments.Count) is { } generated)
        {
            writer.Append(
                $"MistArray.Generate<{generated.Element}>({generated.Length}, "
                + $"{Render(newExpression.Arguments[0], diagnostics)})");
            return;
        }

        if(newExpression.Constructor is { Modifiers.IsIntrinsic: true } constructor
            && !IntrinsicSupply.Supplies(constructor))
        {
            ReportMissingIntrinsic(IntrinsicSupply.Describe(constructor), newExpression.Node, writer, diagnostics);
            return;
        }

        var target = newExpression.Type is TypeParameter && newExpression.Arguments.Count > 0
            ? $"{TypeNameEmitter.Emit(newExpression.Type)}.{ConstructorConstraintInterfaces.MemberName}("
            : UnsafeReach.Construction(newExpression) is { } reachedConstructor
                ? $"{reachedConstructor}("
                : $"new {TypeNameEmitter.Emit(newExpression.Type)}("
            ;

        var initialized = UnsafeReach.Initializer(newExpression);

        if(initialized is { } helper)
        {
            writer.Append($"{helper.Helper}(");
        }

        writer.Append(target);
        EmitArguments(
            newExpression.Arguments, newExpression.Constructor?.ParametersList, writer, diagnostics);
        writer.Append(")");

        if(initialized is { } entries)
        {
            writer.Append($", {entries.Target} => {{ ");

            foreach(var entry in newExpression.Initializer)
            {
                writer.Append(UnsafeReach.Entry(newExpression, entry, entries.Target, Render(entry.Value, diagnostics)) + " ");
            }

            writer.Append("})");
            return;
        }

        EmitObjectInitializer(newExpression.Initializer, writer, diagnostics);
    }

    private static void EmitObjectInitializer(
        IReadOnlyList<ObjectInitializerAssignment> initializer, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(initializer.Count == 0)
        {
            return;
        }

        writer.Append(" { ");

        EmitList.Separated(initializer, ", ", writer, assignment =>
        {
            // A collection element writes only its value: C# reads a bare element as a call to Add,
            // which is what the model already resolved it to.
            if(assignment.MemberName is { } member)
            {
                writer.Append($"{member} = ");
            }

            EmitExpression(assignment.Value, writer, diagnostics);
        });

        writer.Append(" }");
    }

    private static void EmitMemberAccess(
        MemberAccessExpression memberAccess,
        IReadOnlyList<TypeParameter>? parameters,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        // A size-agnostic read of the pack, answered by the specialization rather than emitted: at
        // this width the arity is a constant, and there is no C# value standing for the pack to
        // read it off.
        if(PackArity(memberAccess) is { } arity)
        {
            writer.Append(arity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        if(IntrinsicCalls.RespellsProperty(memberAccess.MemberName, memberAccess.Receiver.Type))
        {
            writer.Append(IntrinsicCalls.RewriteProperty(RenderAt(memberAccess.Receiver, PostfixPrecedence, diagnostics)));
            return;
        }

        if(memberAccess.Member is PropertyResult { Property: { Modifiers.IsIntrinsic: true } property }
            && !IntrinsicSupply.Supplies(property))
        {
            ReportMissingIntrinsic(IntrinsicSupply.Describe(property), memberAccess.Node, writer, diagnostics);
            return;
        }

        if(UnsafeReach.Access(memberAccess, () => RenderAt(memberAccess.Receiver, PostfixPrecedence, diagnostics)) is { } reached)
        {
            writer.Append(reached);
            return;
        }

        UnitHoisting.Refuse(memberAccess.Receiver, diagnostics);
        EmitSubExpression(memberAccess.Receiver, PostfixPrecedence, writer, diagnostics);
        writer.Append(memberAccess.IsConditional ? "?." : ".");
        writer.Append(CalleeName(memberAccess.MemberName, memberAccess.TypeArguments, parameters));
        writer.Append(EmitTypeArguments(memberAccess.TypeArguments, parameters));
    }

    /// <summary>
    /// The parameter a flat argument list collapses into, when it is a value PACK -- the
    /// homogeneous `params T[]` form collects into an array instead and is not this.
    /// </summary>
    private static int? FlattenedTupleSlot(IReadOnlyList<Parameter> parameters)
    {
        for(var i = 0; i < parameters.Count; i++)
        {
            if(parameters[i].IsFlattened && parameters[i].Type is TupleType or NamedTupleType)
            {
                return i;
            }
        }

        return null;
    }

    private static void EmitFlattened(
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter> parameters,
        int flattened,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var trailing = parameters.Count - flattened - 1;
        var absorbed = arguments.Count - flattened - trailing;

        for(var i = 0; i < flattened; i++)
        {
            Separate(i, writer);
            EmitExpression(arguments[i], writer, diagnostics);
        }

        Separate(flattened, writer);
        EmitPacked(arguments, flattened, absorbed, writer, diagnostics);

        for(var i = arguments.Count - trailing; i < arguments.Count; i++)
        {
            writer.Append(", ");
            EmitExpression(arguments[i], writer, diagnostics);
        }
    }

    /// <summary>
    /// The absorbed arguments as ONE tuple value. The widths C#'s tuple syntax cannot spell are
    /// named through ValueTuple, which is also why the single case uses Create rather than a
    /// constructor -- it infers the element type the call site never wrote.
    /// </summary>
    private static void EmitPacked(
        IReadOnlyList<Expression> arguments,
        int start,
        int count,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(count <= 0)
        {
            writer.Append("default(System.ValueTuple)");
            return;
        }

        writer.Append(count == 1 ? "System.ValueTuple.Create(" : "(");

        for(var i = 0; i < count; i++)
        {
            Separate(i, writer);
            EmitExpression(arguments[start + i], writer, diagnostics);
        }

        writer.Append(")");
    }

    private static void Separate(int index, CSharpWriter writer)
    {
        if(index > 0)
        {
            writer.Append(", ");
        }
    }

    /// <summary>
    /// How many elements a tuple type has, or null when the type is not one whose width is fixed --
    /// a value pack whose spread is still symbolic among them.
    /// </summary>
    private static int? TupleWidth(IMistType type)
    {
        var elements = type switch
        {
            NamedTupleType named => named.Underlying.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            _ => null,
        };

        if(elements is null)
        {
            return null;
        }

        if(!elements.Any(e => e is SpreadType))
        {
            return elements.Count;
        }

        return Monomorph.InScope() is { } monomorph && SpreadPack(elements) is { } pack && monomorph.Binds(pack)
            ? elements.Count - 1 + monomorph.Expansion(pack).Count
            : null;
    }

    private static TypeParameter? SpreadPack(IReadOnlyList<IMistType> elements)
    {
        return elements.OfType<SpreadType>().Select(spread => spread.Operand).OfType<TypeParameter>().FirstOrDefault();
    }

    private static void EmitTupleElements(
        Expression operand, int width, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        for(var i = 0; i < width; i++)
        {
            if(i > 0)
            {
                writer.Append(", ");
            }

            EmitExpression(operand, writer, diagnostics);
            writer.Append($".Item{i + 1}");
        }
    }

    /// <summary>
    /// The arity a <c>TArgs.Length</c> stands for in the specialization being emitted, or null when
    /// this access is anything else.
    /// </summary>
    private static int? PackArity(MemberAccessExpression memberAccess)
    {
        return memberAccess.MemberName == "Length"
            && memberAccess.Receiver is TypeReferenceExpression { ReferencedType: TypeParameter { IsPack: true } pack }
            && Monomorph.InScope() is { } monomorph
            && monomorph.Binds(pack)
                ? monomorph.Expansion(pack).Count
                : null;
    }

    /// <param name="parameters">
    /// The slots being filled, when the caller knows them. A dependent slot takes the value's
    /// CARRIER rather than its domain, and only the slot says which of the two an argument is --
    /// the argument itself is a lifted value either way.
    /// </param>
    /// <summary>
    /// The name a call writes, which for a variadic method is the specialization its width asked
    /// for.
    /// </summary>
    private static string CalleeName(
        string declaredName, IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter>? parameters)
    {
        return MonomorphReference.MethodWidths(typeArguments, parameters) is { } widths
            ? MonomorphSet.SpecializedName(declaredName, widths)
            : declaredName;
    }

    private static string EmitTypeArguments(
        IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter>? parameters = null)
    {
        if(typeArguments.Count == 0)
        {
            return "";
        }

        // A pack argument is not written: the specialization already fixed the width, and its
        // elements stand where the pack did.
        if(MonomorphReference.MethodWidths(typeArguments, parameters) is not null)
        {
            return MonomorphReference.SpecializedArguments(typeArguments, parameters!, TypeNameEmitter.Emit);
        }

        var spelled = typeArguments.Select((argument, ordinal) =>
            parameters is not null
                && ordinal < parameters.Count
                && argument is LiftedType lifted
                && parameters[ordinal].Domain is { } domain
                && DependentCarrierNames.Carrier(domain, lifted) is { } carrier
                    ? carrier
                    : TypeNameEmitter.Emit(argument));

        var fillers = parameters is null
            ? []
            : SlotLift.Fillers(typeArguments, parameters).Select(TypeNameEmitter.Emit);

        return $"<{string.Join(", ", spelled.Concat(fillers))}>";
    }

    internal static string EmitLiteral(LiteralExpression literal)
    {
        if(literal.Value is null)
        {
            return "null";
        }

        return literal.Value switch
        {
            string s => $"\"{s}\"",
            long l => EmitIntegerLiteral(l, literal.Type),
            int i => i.ToString(),
            float f => $"{f}f",
            double d => EmitDoubleLiteral(d),
            decimal m => $"{m}m",
            bool b => b ? "true" : "false",
            char c => $"'{c}'",
            _ => literal.Value.ToString()!,
        };
    }

    /// <summary>
    /// Emits an integer literal with the appropriate type suffix based on the
    /// expression's resolved type. Without suffixes, C# interprets bare integer
    /// literals as <c>int</c>, which fails to compile when the target type requires
    /// a wider or unsigned representation (e.g., <c>ulong x = 3000000000;</c>).
    /// </summary>
    private static string EmitIntegerLiteral(long value, IMistType type)
    {
        var typeName = type is MemberType mt ? mt.Name : "";
        return typeName switch
        {
            "UInt64" => $"{(ulong)value}UL",
            "Int64" => $"{value}L",
            "UInt32" => $"{(uint)value}U",
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// Emits a double literal, ensuring it always contains a decimal point or
    /// exponent so C# doesn't parse it as an integer.
    /// </summary>
    private static string EmitDoubleLiteral(double d)
    {
        var text = d.ToString("G");
        if(!text.Contains('.') && !text.Contains('E'))
        {
            return text + ".0";
        }

        return text;
    }

    /// <summary>
    /// Emits an operand, parenthesizing it when it binds looser than the operator applying to it.
    /// Required because binding drops the parentheses source wrote, so they have to be re-derived
    /// here: a negated conjunction arrives as a negation OF a conjunction, and written bare it is C#
    /// for a conjunction of a negation. A type test is ranked with C#'s relational operators, the
    /// precedence no <see cref="BinaryOperatorKind"/> names.
    /// </summary>
    private static void EmitSubExpression(
        Expression child, int operatorPrecedence, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(GetPrecedence(child) < operatorPrecedence)
        {
            writer.Append("(");
            EmitExpression(child, writer, diagnostics);
            writer.Append(")");
            return;
        }

        EmitExpression(child, writer, diagnostics);
    }

    private static int GetPrecedence(Expression expression)
    {
        return expression switch
        {
            BinaryExpression or UnaryExpression when NarrowArithmetic.Promotes(expression.Type) => int.MaxValue,
            BinaryExpression binary => GetPrecedence(binary.OperatorKind),
            UnaryExpression => PrefixPrecedence,
            InvocationExpression drop when TaskResult.Dropped(drop) is not null => PrefixPrecedence,
            IsPatternExpression => RelationalPrecedence,
            AsExpression { Kind: AsConversionKind.Identity } identity => GetPrecedence(identity.Operand),
            SwitchExpression => SwitchPrecedence,
            CoalesceExpression => CoalescePrecedence,
            AssignmentExpression => AssignmentPrecedence,
            _ => int.MaxValue,
        };
    }

    private static int GetPrecedence(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.LogicalOr => 2,
            BinaryOperatorKind.LogicalAnd => 3,
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => 4,
            BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThanOrEqual => RelationalPrecedence,
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Concatenate => 6,
            BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide or
            BinaryOperatorKind.Modulo => 7,
            _ => 0,
        };
    }

    private static string MapBinaryOperator(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Add => "+",
            BinaryOperatorKind.Subtract => "-",
            BinaryOperatorKind.Multiply => "*",
            BinaryOperatorKind.Divide => "/",
            BinaryOperatorKind.Modulo => "%",
            BinaryOperatorKind.Equal => "==",
            BinaryOperatorKind.NotEqual => "!=",
            BinaryOperatorKind.LessThan => "<",
            BinaryOperatorKind.GreaterThan => ">",
            BinaryOperatorKind.LessThanOrEqual => "<=",
            BinaryOperatorKind.GreaterThanOrEqual => ">=",
            BinaryOperatorKind.LogicalAnd => "&&",
            BinaryOperatorKind.LogicalOr => "||",
            BinaryOperatorKind.Concatenate => "+",
            _ => "/* unknown op */",
        };
    }

    private static string MapUnaryOperator(UnaryOperatorKind kind)
    {
        return kind switch
        {
            UnaryOperatorKind.Negate => "-",
            UnaryOperatorKind.LogicalNot => "!",
            UnaryOperatorKind.FromEnd => "^",
            _ => "/* unknown unary */",
        };
    }
}