using System.Globalization;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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

    internal const int RelationalPrecedence = 5;

    private const int SwitchPrecedence = 8;

    private const int PrefixPrecedence = 9;

    private const int PostfixPrecedence = 10;

    private const int DisjunctionPrecedence = 1;

    private const int ConjunctionPrecedence = 2;

    private const int NegationPrecedence = 3;

    [ThreadStatic]
    private static IMistType? enclosingType;

    public static void EmitBlock(Block block, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        EmitBlock(block, [], writer, diagnostics);
    }

    public static void EmitBlock(
        Block block, IEnumerable<string> parameterNames, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        using var names = LocalNames.Enter(block, parameterNames);
        using var lifetimes = LifetimeLowering.Enter(block);
        using var reach = UnitHoisting.Enter(block);

        foreach(var (temp, flag, type) in LifetimeLowering.Renewed(block))
        {
            writer.WriteLine($"if ({flag})");

            using(writer.Block())
            {
                writer.WriteLine($"{flag} = false;");
                EmitRelease(temp, type, writer);
            }
        }

        EmitStatements(block.Statements, 0, writer, diagnostics);
    }

    /// <summary>
    /// A method body, with the drops its OWNED parameters oblige. The caller handed those values over,
    /// so the body owes the release; the nesting gives the same reverse order a block's own bindings
    /// get, for the same reason.
    /// </summary>
    public static void EmitMethodBody(
        Method method, Block body, CSharpWriter writer, DiagnosticBag diagnostics, Func<Parameter, string>? spell = null,
        MemberType? owner = null, IEnumerable<string>? additionalParameterNames = null, bool capturesContext = false)
    {
        var previous = enclosingType;
        enclosingType = owner ?? method.DeclaringType ?? previous;
        try
        {
            using var keys = capturesContext ? null : ContextKeyWitnesses.Entering(method);
            using var names = LocalNames.Enter(body, method.ParametersList.Select(parameter => parameter.Name)
                .Concat(method.ContextBindingsList.Select(binding => binding.Identifier))
                .Concat(additionalParameterNames ?? []));
            var holder = owner is ConcreteType concrete && concrete.MethodsList.Contains(method) ? concrete : null;
            using var lifetimes = LifetimeLowering.Enter(body, method, holder);
            using var reach = UnitHoisting.Enter(body);
            EmitContextBindings(method, writer);
            EmitWrapped(Held(method, body, spell, holder, writer), 0, () => EmitBlock(body, writer, diagnostics), writer);
        }
        finally
        {
            enclosingType = previous;
        }
    }

    public static void EmitSynchronousMethodBody(
        Method method, Block body, CSharpWriter writer, DiagnosticBag diagnostics, MemberType? owner = null)
    {
        var enclosing = blockingAwaits;
        blockingAwaits = true;

        try
        {
            EmitMethodBody(method, body, writer, diagnostics, owner: owner);
        }
        finally
        {
            blockingAwaits = enclosing;
        }
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
            EmitMethodBody(function.Function, function.Body, writer, diagnostics, spell, capturesContext: true);
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
            var peek = ContextKeyWitnesses.Held(binding.ContextType) is { } held
                ? $"MistContext.Peek({held})"
                : $"MistContext.Peek<{TypeNameEmitter.EmitContextKey(binding.ContextType)}>()";

            writer.WriteLine($"var {binding.Identifier} = ({type}){peek};");
        }
    }

    private static List<(string? Flag, string Release)> Held(
        Method method, Block body, Func<Parameter, string>? spell, ConcreteType? owner, CSharpWriter writer)
    {
        var held = new List<(string?, string)>();

        foreach(var (parameter, release) in DropInsertion.ReleasedParameters(method, body, LifetimeLowering.Active))
        {
            var flag = release is LifetimeRelease.Guarded ? LifetimeLowering.Flag(parameter, owned: true, writer) : null;
            held.Add((flag, Released(spell?.Invoke(parameter) ?? parameter.Name, parameter.Type)));
        }

        if(owner is not null)
        {
            foreach(var (field, release) in Enumerable.Reverse(DropInsertion.ReleasedFields(method, owner, LifetimeLowering.Active)))
            {
                var flag = release is LifetimeRelease.Guarded ? LifetimeLowering.Flag(field, owned: true, writer) : null;
                held.Add((flag, DropInsertion.FieldDrop(field)));
            }
        }

        return held;
    }

    private static void EmitWrapped(
        IReadOnlyList<(string? Flag, string Release)> held, int index, Action body, CSharpWriter writer)
    {
        if(index >= held.Count)
        {
            body();
            return;
        }

        writer.WriteLine("try");

        using(writer.Block())
        {
            EmitWrapped(held, index + 1, body, writer);
        }

        writer.WriteLine("finally");

        using(writer.Block())
        {
            EmitGuarded(held[index].Flag, held[index].Release, writer);
        }
    }

    private static void EmitGuarded(string? flag, string release, CSharpWriter writer)
    {
        if(flag is null)
        {
            writer.WriteLine(release);
            return;
        }

        writer.WriteLine($"if ({flag})");

        using(writer.Block())
        {
            writer.WriteLine(release);
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
            if(LifetimeLowering.Lend(statements[i], writer) is { Count: > 0 } lent)
            {
                List<(string?, string)> held = [.. lent.Select(value => ((string?)value.Flag, Released(value.Temp, value.Type)))];

                if(statements[i] is ReturnStatement or IfStatement or WhileStatement or ForStatement or ForEachStatement
                    || (statements[i] is ExpressionStatement evaluated && !DropInsertion.IsDiscardedTask(evaluated.Expression)))
                {
                    EmitWrapped(held, 0, () => EmitStatements([statements[i]], 0, writer, diagnostics), writer);
                    continue;
                }

                EmitWrapped(held, 0, () => EmitStatements(statements, i, writer, diagnostics), writer);
                return;
            }

            if(statements[i] is LocalDeclarationStatement declaration
                && LifetimeLowering.ReleaseOf(declaration, Remainder(statements, i + 1)) is not LifetimeRelease.None and var release)
            {
                var guard = LifetimeLowering.Declare(declaration, release, writer, diagnostics);
                EmitWrapped([(guard, Released(LocalNames.Of(declaration.Local), declaration.Local.Type))], 0, () => EmitStatements(statements, i + 1, writer, diagnostics), writer);
                return;
            }

            if(statements[i] is TupleDeconstructionStatement deconstruction
                && deconstruction.Targets.Any(target => LifetimeLowering.Active.ReleaseOf(target) is not LifetimeRelease.None))
            {
                EmitStatement(deconstruction, writer, diagnostics);
                var held = new List<(string?, string)>();

                foreach(var target in deconstruction.Targets.Where(target => LifetimeLowering.Active.ReleaseOf(target) is not LifetimeRelease.None))
                {
                    held.Add((LifetimeLowering.Flag(target, owned: true, writer), Released(LocalNames.Of(target), target.Type)));
                }

                EmitWrapped(held, 0, () => EmitStatements(statements, i + 1, writer, diagnostics), writer);
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

        EmitSequenced(discarded.Expression, $"var {name} = ", writer, diagnostics);
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
        writer.WriteLine(Released(name, type));
    }

    private static string Released(string name, IMistType type)
    {
        if(!DropInsertion.IsAwaitable(type))
        {
            return $"{DropInsertion.DropChain(name, type)};";
        }

        return blockingAwaits ? $"{name}.GetAwaiter().GetResult();" : $"await {name};";
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

    internal static void EmitStatement(Statement statement, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        switch(statement)
        {
            case ExpressionStatement { Expression: InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } } conditional }
                when CompletesPromoted(conditional):
                ConditionalAsyncCalls.Emit(conditional, blockingAwaits, statement: true, writer, diagnostics, EmitDirect);
                break;

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
                    using var hoisted = EmitHoisted(exprStmt.Expression, writer, diagnostics, discarded: true);

                    if(hoisted.Consumes(exprStmt.Expression))
                    {
                        break;
                    }

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
                EmitReturn(returnStmt, writer, diagnostics);
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
    private static void EmitOperand(
        Expression operand, Operator? declared, IReadOnlyList<IMistType>? closed, int position, int precedence, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(declared is { Modifiers.IsIntrinsic: false } && position < declared.ParametersList.Count
            && OverloadPinning.CastFor(operand, declared.ParametersList[position], closed?[position] ?? declared.ParametersList[position].Type, declared) is { } pinned)
        {
            writer.Append($"(({pinned})");
            EmitSubExpression(operand, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        EmitSubExpression(operand, precedence, writer, diagnostics);
    }

    private static string Concatenated(Expression operand, int precedence, DiagnosticBag diagnostics)
    {
        return InvariantCulture.Fixes(operand.Type)
            ? RenderAt(operand, precedence, diagnostics)
            : InvariantCulture.Text(RenderAt(operand, PostfixPrecedence, diagnostics), operand.Type);
    }

    private static void EmitCarrierCall(
        InvocationExpression call, MemberAccessExpression callee, string carrier, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(call.Method, callee.TypeArguments);
        writer.Append($"{carrier}.{CalleeName(call.Method!.Name, arguments, parameters)}{EmitTypeArguments(arguments, parameters)}(");
        if(!call.Method.Modifiers.IsStatic)
        {
            EmitExpression(callee.Receiver, writer, diagnostics);

            if(HasArguments(call.Arguments, call.Method.ParametersList)
                || DefaultWitnesses.MethodArguments(call.Method, callee.TypeArguments, callee.Receiver.Type, call.ContextTypes).Count > 0)
            {
                writer.Append(", ");
            }
        }

        EmitWitnesses(call, writer, diagnostics);
        EmitArguments(call.Arguments, call.Method.ParametersList, call.ParameterTypes, writer, diagnostics, call.Method);
        writer.Append(")");
    }

    private static void EmitWitnesses(
        InvocationExpression invocation, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var typeArguments = invocation.Receiver switch
        {
            MemberAccessExpression access => access.TypeArguments,
            MethodReferenceExpression reference => reference.TypeArguments,
            _ => [],
        };

        if(invocation.Method is not { } method)
        {
            return;
        }

        var receiverType = invocation.Receiver is MemberAccessExpression reached ? reached.Receiver.Type : method.DeclaringType;
        var witnesses = DefaultWitnesses.MethodArguments(method, typeArguments, receiverType, invocation.ContextTypes);

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

        if(HasArguments(invocation.Arguments, invocation.Parameters))
        {
            writer.Append(", ");
        }
    }

    internal static bool HasArguments(IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        return (parameters is not null && FlattenedTupleSlot(parameters) is not null)
            || arguments.Any(argument => argument is not SpreadExpression spread || TupleWidth(spread.Operand.Type) is not 0);
    }

    internal static void EmitArguments(
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter>? parameters,
        IReadOnlyList<IMistType>? parameterTypes,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        Method? callee = null)
    {
        // The call site is FLAT and the parameter is one tuple, which is the whole of what `params`
        // means on the value side. C# has no such convention, so the arguments the pack absorbs are
        // packed here -- the one place that knows both how many there are and where they end.
        if(parameters is not null && FlattenedTupleSlot(parameters) is { } flattened)
        {
            EmitFlattened(arguments, parameters, parameterTypes, flattened, writer, diagnostics, callee);
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

            EmitArgument(arguments[i],
                parameters is not null && i < parameters.Count ? parameters[i] : null,
                parameterTypes is not null && i < parameterTypes.Count ? parameterTypes[i] : null,
                writer, diagnostics, callee);
        }
    }

    internal static void EmitArgument(
        Expression argument, Parameter? parameter, IMistType? closedTarget,
        CSharpWriter writer, DiagnosticBag diagnostics, Method? callee)
    {
        if(parameter is not null && InvariantCulture.Writes(callee, parameter) && InvariantCulture.WritesText(argument.Type))
        {
            writer.Append(InvariantCulture.Written(RenderAt(argument, PostfixPrecedence, diagnostics), argument.Type));
            return;
        }

        if(callee?.Modifiers.IsIntrinsic == true || callee?.Modifiers.IsClrProjected == true)
        {
            // A projected CLR member may expect a nominal delegate where its Mist target is func.
            EmitCoerced(argument, parameter?.Type, writer, diagnostics, entered: closedTarget);
            return;
        }

        if(parameter is not null
            && OverloadPinning.CastFor(argument, parameter, closedTarget ?? parameter.Type, callee) is { } pinned)
        {
            writer.Append($"(({pinned})");
            if(argument is LambdaExpression)
            {
                writer.Append("(");
            }

            EmitSubExpression(argument, PrefixPrecedence, writer, diagnostics);
            if(argument is LambdaExpression)
            {
                writer.Append(")");
            }

            writer.Append(")");
            return;
        }

        EmitCoerced(argument, closedTarget ?? parameter?.Type, writer, diagnostics);
    }

    private static void EmitReturn(ReturnStatement returnStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(returnStmt.Value is { } value && OptionalUnion.IsVoid(value.Type))
        {
            using(var hoisted = EmitHoisted(value, writer, diagnostics, discarded: true))
            {
                if(value is InvocationExpression && !hoisted.Consumes(value))
                {
                    writer.Write("");
                    EmitExpression(value, writer, diagnostics);
                    writer.Append(";");
                    writer.AppendLine();
                }
            }

            writer.WriteLine("return;");
        }
        else if(returnStmt is { IsExitCode: true, Value: { } exitCode })
        {
            EmitSequenced(exitCode, "System.Environment.ExitCode = ", writer, diagnostics, slot: returnStmt.TargetType);
            writer.WriteLine("return;");
        }
        else if(returnStmt.Value is not null)
        {
            EmitSequenced(returnStmt.Value, returnStmt.IsRef ? "return ref " : "return ", writer, diagnostics, slot: returnStmt.TargetType);
        }
        else
        {
            writer.WriteLine("return;");
        }
    }

    internal static void EmitSequenced(
        Expression value,
        string? lead,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        bool branchTaken = false,
        IMistType? slot = null)
    {
        using var hoisted = EmitHoisted(value, writer, diagnostics, discarded: lead is null, branchTaken);

        if(hoisted.Consumes(value))
        {
            return;
        }

        writer.Write(lead ?? "");
        EmitEntering(value, slot, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
    }

    internal static bool NeedsStatements(Expression value, bool discarded)
    {
        return !UnitHoisting.Plan(value, discarded).IsEmpty;
    }

    internal static UnitHoisting.Hoisting EmitHoisted(
        Expression? root, CSharpWriter writer, DiagnosticBag diagnostics, bool discarded = false, bool branchTaken = false)
    {
        return EmitHoisted(UnitHoisting.Plan(root, discarded, branchTaken), writer, diagnostics);
    }

    internal static UnitHoisting.Hoisting EmitHoisted(
        UnitHoisting.Hoisting hoisting, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var unplaced in hoisting.Unplaced)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnhoistedUnitCall, unplaced.Call.Node, null, unplaced.Call.Method?.Name ?? "The call", unplaced.Reason);
        }

        foreach(var step in hoisting.Steps)
        {
            if(step.Kind is UnitHoisting.StepKind.Lower)
            {
                if(ConditionalLowering.Emit(step.Expression, step.Valued, writer, diagnostics) is { } result)
                {
                    hoisting.Bind(step, result);
                }

                continue;
            }

            if(step.Kind is UnitHoisting.StepKind.Hoist)
            {
                if(step.Expression is InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } } conditional
                    && CompletesPromoted(conditional))
                {
                    ConditionalAsyncCalls.Emit(conditional, blockingAwaits, statement: true, writer, diagnostics, EmitDirect);
                }
                else
                {
                    writer.WriteLine($"{Render(step.Expression, diagnostics)};");
                }

                hoisting.Bind(step, "Standard.Void.Instance");
                continue;
            }

            var temporary = TemporaryName();
            var text = Render(step.Expression, diagnostics);
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
        if(local.IsDiscard)
        {
            writer.WriteLine($"_ = {LocalNames.Of(local)};");
        }
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

        var binding = withBlock.Binding;
        var bound = binding is not null ? LocalNames.Of(binding) : TemporaryName();

        // The KEY the value is filed under, which a form may have written and which is not the
        // value's own type -- see WithBlockStatement.KeyType. The drop below still asks the VALUE
        // what it owes, those being different questions about the same instance.
        var keyed = ContextKeyWitnesses.Held(withBlock.KeyType);
        var type = keyed ?? TypeNameEmitter.EmitContextKey(withBlock.KeyType);

        EmitSequenced(withBlock.Value, $"var {bound} = ", writer, diagnostics);
        var plan = LifetimeLowering.Active;
        var owning = binding is not null && plan.Covers(binding) ? binding : null;
        var flag = owning is null ? null : LifetimeLowering.Flag(owning, owned: true, writer);
        writer.WriteLine(keyed is null ? $"MistContext.Push<{type}>({bound});" : $"MistContext.Push({type}, {bound});");
        writer.WriteLine("try");

        using(writer.Block())
        {
            EmitBlock(withBlock.Body, writer, diagnostics);
        }

        writer.WriteLine("finally");

        using(writer.Block())
        {
            writer.WriteLine(keyed is null ? $"MistContext.Pop<{type}>();" : $"MistContext.Pop({type});");

            if(plan.ReleaseAt(withBlock) is var (held, release))
            {
                EmitScopeRelease(held, release, withBlock.Value, writer);
            }
            else if(owning is not null)
            {
                if(plan.ReleaseOf(owning) is not LifetimeRelease.None)
                {
                    EmitGuarded(flag, $"{DropInsertion.DropChain(bound, withBlock.Value.Type)};", writer);
                }
            }
            else if(!LifetimeOwners.IsView(withBlock.Value) && DropInsertion.ExitAction(withBlock.Value.Type) is not null)
            {
                writer.WriteLine($"{DropInsertion.DropChain(bound, withBlock.Value.Type)};");
            }
        }
    }

    /// <summary>
    /// A with given a value something else in the body holds releases it through that holder: what the
    /// holder still owns when the scope ends, under its flag where it has one, which the release clears
    /// so the holder's own scope does not release it again.
    /// </summary>
    private static void EmitScopeRelease(object held, LifetimeRelease release, Expression value, CSharpWriter writer)
    {
        var (name, type) = held switch
        {
            LocalVariable local => (LocalNames.Of(local), local.Type),
            Parameter parameter when value is LocalReferenceExpression { Local: { } reached } && ReferenceEquals(reached.Parameter, parameter)
                => (LocalNames.Of(reached), parameter.Type),
            Parameter parameter => (parameter.Name, parameter.Type),
            Field field => ($"this.{field.Name}", field.Type),
            _ => throw new InvalidOperationException($"ICE: a with scope cannot release a {held.GetType().Name}."),
        };

        if(LifetimeLowering.FlagOf(held) is { } flag)
        {
            writer.WriteLine($"if ({flag})");

            using(writer.Block())
            {
                writer.WriteLine($"{flag} = false;");
                EmitRelease(name, type, writer);
            }
        }
        else if(release is not LifetimeRelease.None)
        {
            EmitRelease(name, type, writer);
        }
    }

    private static void EmitWhile(WhileStatement whileStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var guards = GuardChain.Split(whileStmt.Condition);

        if(guards.Count == 1 && UnitHoisting.Plan(whileStmt.Condition).IsEmpty)
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
            GuardChain.EmitBreaks(guards, writer, diagnostics);
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
        if(GuardChain.Split(ifStmt.Condition) is { Count: > 1 } guards)
        {
            GuardChain.EmitIf(ifStmt, guards, writer, diagnostics);
            return;
        }

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
            && (bindingElseIf.ConditionBinds || !UnitHoisting.Plan(bindingElseIf.Condition).IsEmpty))
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
        if(forStmt.Initializer is LocalDeclarationStatement held
            && LifetimeLowering.Active.ReleaseOf(held) is not LifetimeRelease.None and var release)
        {
            using(writer.Block())
            {
                var guard = LifetimeLowering.Declare(held, release, writer, diagnostics);
                writer.WriteLine("try");

                using(writer.Block())
                {
                    EmitLoop(forStmt, initialized: true, writer, diagnostics);
                }

                writer.WriteLine("finally");

                using(writer.Block())
                {
                    EmitGuarded(guard, Released(LocalNames.Of(held.Local), held.Local.Type), writer);
                }
            }

            return;
        }

        EmitLoop(forStmt, initialized: false, writer, diagnostics);
    }

    private static void EmitLoop(ForStatement forStmt, bool initialized, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(ForLowering.Applies(forStmt, initialized))
        {
            ForLowering.Emit(forStmt, initialized, writer, diagnostics);
            return;
        }

        writer.Write("for (");

        // Initializer
        var initializer = initialized ? null : forStmt.Initializer;

        if(initializer is LocalDeclarationStatement initDecl)
        {
            var typeName = TypeNameEmitter.Emit(initDecl.Local.Type);
            writer.Append($"{typeName} {LocalNames.Of(initDecl.Local)}");
            if(initDecl.Initializer is not null)
            {
                writer.Append(" = ");
                EmitExpression(initDecl.Initializer, writer, diagnostics);
            }
        }
        else if(initializer is ExpressionStatement initExpr)
        {
            EmitExpression(initExpr.Expression, writer, diagnostics);
        }

        writer.Append("; ");

        // Condition
        var discard = (forStmt.Initializer as LocalDeclarationStatement)?.Local;
        if(discard is { IsDiscard: true })
        {
            writer.Append($"{LocalNames.Of(discard)} is var _");
            if(forStmt.Condition is not null)
            {
                writer.Append(" && (");
            }
        }

        if(forStmt.Condition is not null)
        {
            EmitExpression(forStmt.Condition, writer, diagnostics);
            if(discard is { IsDiscard: true })
            {
                writer.Append(")");
            }
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
        if(LifetimeLowering.Captured(expression) is { } receiver)
        {
            writer.Append("MistOwnership.Present(");
            EmitExpression(expression, writer, diagnostics);
            writer.Append($", out var {receiver})");
            return;
        }

        if(LifetimeLowering.LentAt(expression) is var (temp, flag))
        {
            writer.Append($"MistOwnership.Taken({temp} = ");
            EmitUnmarked(expression, writer, diagnostics);
            writer.Append($", ref {flag})");
            return;
        }

        if(LifetimeLowering.MarksAfter(expression) is { Count: > 0 } marks)
        {
            LifetimeLowering.Open(marks, writer);
            EmitUnmarked(expression, writer, diagnostics);
            LifetimeLowering.Close(marks, writer);
            return;
        }

        EmitUnmarked(expression, writer, diagnostics);
    }

    private static void EmitUnmarked(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(UnitHoisting.Substitution(expression) is { } substituted)
        {
            writer.Append(substituted);
            return;
        }

        if(!CompletesPromoted(expression))
        {
            EmitDirect(expression, writer, diagnostics);
        }
        else if(expression is InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } } conditional)
        {
            ConditionalAsyncCalls.Emit(conditional, blockingAwaits, statement: false, writer, diagnostics, EmitDirect);
        }
        else if(blockingAwaits)
        {
            EmitDirect(expression, writer, diagnostics);
            writer.Append(".GetAwaiter().GetResult()");
        }
        else
        {
            writer.Append("await ");
            EmitDirect(expression, writer, diagnostics);
        }
    }

    /// <summary>
    /// Whether the expression calls a PROMOTED callee. Its task exists only in the emitted C#, so the
    /// model carries no await for it and <see cref="EmitExpression"/> writes one, blocking where
    /// <see cref="EmitTaskDrop"/> would. <see cref="GetPrecedence(Expression)"/> asks the same question,
    /// so an operator applied to the call parenthesizes an await the model cannot show it. See
    /// <see cref="AsyncPromotion"/>.
    /// </summary>
    private static bool CompletesPromoted(Expression expression)
    {
        return expression switch
        {
            InvocationExpression invocation => AsyncPromotion.IsPromoted(invocation.Method),
            IndexExpression { Slice.Method: { } slice } => AsyncPromotion.IsPromoted(slice),
            _ => false,
        };
    }

    private static void EmitDirect(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        switch(expression)
        {
            // A lift needs a task built around a value C# has no other way to wrap; a suppression needs
            // nothing at all, the operand already being the task and this having only kept the sync
            // context from awaiting it.
            case AsyncExpression { IsLift: false } suppression:
                EmitExpression(suppression.Operand, writer, diagnostics);
                break;

            case AsyncExpression lift:
                var needsAwait = AsyncLifts.NeedsAwait(lift.Operand);
                writer.Append(needsAwait
                    ? "System.Threading.Tasks.Task.Run(async () => "
                    : "System.Threading.Tasks.Task.Run(() => ");
                var enclosingBlocking = blockingAwaits;
                blockingAwaits = false;
                try
                {
                    EmitExpression(lift.Operand, writer, diagnostics);
                }
                finally
                {
                    blockingAwaits = enclosingBlocking;
                }

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
                        [.. respelled.Arguments.Select(argument => RenderAt(argument, PostfixPrecedence, diagnostics))],
                        access is { IsConditional: true }));
                    break;
                }

            case InvocationExpression { Method: { Modifiers.IsIntrinsic: true } unsupplied } missing
                when !IntrinsicSupply.Supplies(unsupplied):
                ReportMissingIntrinsic(IntrinsicSupply.Describe(unsupplied), missing.Node, writer, diagnostics);
                break;

            case InvocationExpression { Receiver: MemberAccessExpression { IsConditional: false } extended } extension
                when ExtensionCalls.Carrier(extension.Method) is { } carrier:
                EmitCarrierCall(extension, extended, carrier, writer, diagnostics);
                break;

            case InvocationExpression { Receiver: MemberAccessExpression { IsConditional: false } formatted } formatting
                when InvariantCulture.FormatsItself(formatting.Method):
                writer.Append(InvariantCulture.ObjectText(RenderAt(formatted.Receiver, PostfixPrecedence, diagnostics), formatted.Receiver.Type));
                break;

            case InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } conditioned } conditionalFormatting
                when InvariantCulture.FormatsItself(conditionalFormatting.Method) && !InvariantCulture.FixesPresent(conditioned.Receiver.Type):
                writer.Append(InvariantCulture.ConditionalText(RenderAt(conditioned.Receiver, PostfixPrecedence, diagnostics), conditioned.Receiver.Type));
                break;

            case InvocationExpression invocation:
                if(UnsafeReach.Call(invocation, receiver => RenderAt(receiver, PostfixPrecedence, diagnostics)) is { } reached)
                {
                    writer.Append(reached.Open);
                    EmitArguments(invocation.Arguments, invocation.Parameters, invocation.ParameterTypes, writer, diagnostics, invocation.Method);
                    writer.Append(reached.Close);
                    break;
                }

                LifetimeLowering.Capture(invocation);

                // The member access is emitted from HERE when it carries type arguments, because
                // only the invocation knows which overload they fill -- and a dependent slot is
                // spelled by its carrier rather than by its domain.
                if(invocation.Receiver is MemberAccessExpression hiddenCallee
                    && (HiddenBaseCall.ReceiverType(hiddenCallee.Receiver.Type, invocation.Method)
                        ?? NativeDefaultCall.ReceiverType(hiddenCallee.Receiver.Type, invocation.Method)) is { } baseType)
                {
                    EmitMemberAccess(hiddenCallee,
                        ExtensionBlockEmitter.CallTypeArguments(invocation.Method, hiddenCallee.TypeArguments),
                        writer, diagnostics, CollidingName(invocation.Method, baseType), baseType,
                        invocation.Method?.Modifiers.IsStatic == true);
                }
                else if(invocation.Receiver is MemberAccessExpression callee
                    && (callee.TypeArguments is { Count: > 0 } || CollidingName(invocation.Method, callee.Receiver.Type) is not null))
                {
                    EmitMemberAccess(
                        callee, ExtensionBlockEmitter.CallTypeArguments(invocation.Method, callee.TypeArguments), writer, diagnostics,
                        CollidingName(invocation.Method, callee.Receiver.Type));
                }
                else
                {
                    EmitSubExpression(invocation.Receiver, PostfixPrecedence, writer, diagnostics);
                }

                writer.Append("(");
                EmitWitnesses(invocation, writer, diagnostics);
                EmitArguments(invocation.Arguments, invocation.Parameters, invocation.ParameterTypes, writer, diagnostics, invocation.Method);
                writer.Append(")");
                break;

            case MemberAccessExpression memberAccess:
                EmitMemberAccess(memberAccess, (memberAccess.TypeArguments, null), writer, diagnostics);
                break;

            // Named on the carrier rather than written as dot-notation, which is what the extension
            // METHOD case relies on. That is not available here: `get_IsEmpty` is a name the user
            // never wrote, so C#'s own extension lookup has nothing to find.
            case ExtensionPropertyAccessExpression extensionProperty:
                writer.Append($"{extensionProperty.CarrierName}.get_{extensionProperty.Property.Name}(");
                if(!extensionProperty.Property.Modifiers.IsStatic)
                {
                    EmitExpression(extensionProperty.Receiver, writer, diagnostics);
                }

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

            case BinaryExpression concatenation when InvariantCulture.Concatenates(concatenation):
                writer.Append(
                    $"{Concatenated(concatenation.Left, GetPrecedence(concatenation.OperatorKind), diagnostics)} + "
                    + Concatenated(concatenation.Right, GetPrecedence(concatenation.OperatorKind) + 1, diagnostics));
                break;

            // Every binary operator Mist has is left-associative, so a right operand of the same precedence is
            // one the source parenthesized: `a - (b - c)` written bare is `(a - b) - c`.
            case BinaryExpression binary when NarrowArithmetic.Promotes(binary.Type):
                writer.Append(NarrowArithmetic.Spell(
                    binary.Type,
                    $"{RenderAt(binary.Left, GetPrecedence(binary.OperatorKind), diagnostics)} {MapBinaryOperator(binary.OperatorKind)} {RenderAt(binary.Right, GetPrecedence(binary.OperatorKind) + 1, diagnostics)}"));
                break;

            case BinaryExpression binary:
                EmitOperand(binary.Left, binary.Operator, binary.ParameterTypes, 0, GetPrecedence(binary.OperatorKind), writer, diagnostics);
                writer.Append($" {MapBinaryOperator(binary.OperatorKind)} ");
                EmitOperand(binary.Right, binary.Operator, binary.ParameterTypes, 1, GetPrecedence(binary.OperatorKind) + 1, writer, diagnostics);
                break;

            case UnaryExpression unary when NarrowArithmetic.Promotes(unary.Type):
                writer.Append(NarrowArithmetic.Spell(
                    unary.Type, Prefixed(unary.OperatorKind, RenderAt(unary.Operand, PrefixPrecedence, diagnostics))));
                break;

            case UnaryExpression unary:
                writer.Append(Prefixed(
                    unary.OperatorKind, RenderOperand(unary.Operand, unary.Operator, unary.ParameterTypes, 0, PrefixPrecedence, diagnostics)));
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

            // A conversion that replaced a `?.Clone()` keeps that access's short-circuit: C# lifts a
            // user-defined operator only over a nullable value type, so an absent reference operand
            // would otherwise be handed to the operator instead of answering absence. The lift is
            // written only where it spells differently from the result, which is where the operand
            // is a value type and the branches would have no common type.
            case CastExpression { IsConditional: true } lifted:
                {
                    var present = $"__converted{lifted.Node.Span.Start}";
                    var target = TypeNameEmitter.Emit(lifted.TargetType);
                    var result = TypeNameEmitter.Emit(lifted.Type);
                    var converted = $"({target})({present})";

                    writer.Append(
                        $"({Render(lifted.Operand, diagnostics)} is {{ }} {present} ? "
                        + (string.Equals(TypeNameEmitter.EmitUnannotated(lifted.Type), target, StringComparison.Ordinal) ? converted : $"({result})({converted})")
                        + " : null)");
                }

                break;

            case CastExpression { ParameterType: null } cast when UnionEntry.Factory(cast.Operand.Type, cast.Type) is { } entry:
                writer.Append($"{entry}({Render(cast.Operand, diagnostics)})");
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
                EmitEntering(assignment.Value, assignment.Target.Type, writer, diagnostics);
                break;

            case IndexExpression elementAccess
                when UnsafeReach.Index(elementAccess, operand => RenderAt(operand, PostfixPrecedence, diagnostics)) is { } reachedIndex:
                writer.Append(reachedIndex);
                break;

            case IndexExpression elementAccess:
                ReportUnreachedSlice(elementAccess.Slice, elementAccess.Receiver.Type, elementAccess.Node, diagnostics);
                EmitSubExpression(elementAccess.Receiver, PostfixPrecedence, writer, diagnostics);
                writer.Append("[");

                if(elementAccess.Indexer is { ParametersList: [var slot] } indexer
                    && OverloadPinning.CastFor(elementAccess.Index, slot.Type, OverloadPinning.Overloaded(indexer)) is { } pinnedIndex)
                {
                    writer.Append($"(({pinnedIndex})");
                    EmitSubExpression(elementAccess.Index, PrefixPrecedence, writer, diagnostics);
                    writer.Append(")");
                }
                else
                {
                    EmitExpression(elementAccess.Index, writer, diagnostics);
                }

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
                EmitList.Separated(arrayLiteral.Elements, ", ", writer, element => EmitEntering(element, arrayLiteral.ElementType, writer, diagnostics));

                if(arrayLiteral.Elements.Count > 0)
                {
                    writer.Append(" }");
                }

                break;

            case TupleLiteralExpression tupleLiteral:
                EmitTupleLiteral(tupleLiteral, writer, diagnostics,
                    enteringTuple is { } entering && ReferenceEquals(entering.Literal, tupleLiteral) ? entering.Slots : null);
                break;

            case AnonymousObjectExpression anonymous:
                EmitAnonymousObject(anonymous, writer, diagnostics);
                break;

            case LambdaExpression lambda:
                EmitLambda(lambda, writer, diagnostics);
                break;

            case InterpolatedStringExpression interpolated:
                writer.Append(InterpolationEmitter.Emit(interpolated, hole => Render(hole, diagnostics)));
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
                if(enclosingType is { } enclosing
                    && (HiddenBaseCall.ReceiverType(enclosing, methodRef.Method)
                        ?? NativeDefaultCall.ReceiverType(enclosing, methodRef.Method)) is { } baseOwner)
                {
                    writer.Append(methodRef.Method.Modifiers.IsStatic
                        ? TypeNameEmitter.Emit(baseOwner) + "."
                        : "((" + TypeNameEmitter.Emit(baseOwner) + ")" + ReceiverSpelling.This + ").");
                }
                else
                {
                    writer.Append(ReceiverSpelling.Qualifier(methodRef.Method.DeclaringType, methodRef.Method.Modifiers.IsStatic));
                }

                writer.Append(
                    CalleeName(
                        CollidingName(methodRef.Method, receiverType: null) ?? LocalNames.Of(methodRef.Method),
                        methodRef.TypeArguments,
                        methodRef.Method.TypeParametersList)
                    + EmitTypeArguments(methodRef.TypeArguments, methodRef.Method.TypeParametersList));
                break;

            case FunctionReferenceExpression functionRef
                when UnsafeReach.Function(functionRef, operand => RenderAt(operand, PostfixPrecedence, diagnostics)) is { } reachedFunction:
                writer.Append(reachedFunction);
                break;

            case FunctionReferenceExpression respelledGroup when RespelledGroups.Applies(respelledGroup):
                writer.Append(RespelledGroups.Lambda(
                    respelledGroup,
                    respelledGroup.Receiver is { } groupReceiver && !respelledGroup.Method.Modifiers.IsStatic ? Render(groupReceiver, diagnostics) : null));
                break;

            case FunctionReferenceExpression functionRef:
                if(functionRef.Receiver is { } functionTarget
                    && NativeDefaultCall.ReceiverType(functionTarget.Type, functionRef.Method) is { } nativeTarget)
                {
                    writer.Append($"(({TypeNameEmitter.Emit(nativeTarget)})");
                    EmitSubExpression(functionTarget, PrefixPrecedence, writer, diagnostics);
                    writer.Append(").");
                }
                else if(functionRef.Receiver is { } plainTarget)
                {
                    EmitExpression(plainTarget, writer, diagnostics);
                    writer.Append(".");
                }
                else if(enclosingType is { } functionOwner
                    && NativeDefaultCall.ReceiverType(functionOwner, functionRef.Method) is { } nativeOwner)
                {
                    writer.Append($"(({TypeNameEmitter.Emit(nativeOwner)}){ReceiverSpelling.This}).");
                }
                else
                {
                    writer.Append(ReceiverSpelling.Qualifier(functionRef.Method.DeclaringType, functionRef.Method.Modifiers.IsStatic));
                }

                writer.Append(FunctionName(functionRef));
                break;

            case MethodGroupExpression methodGroup:
                diagnostics.ReportAtNode(
                    BackendDiagnostics.MethodGroupReachedEmission, methodGroup.Node, null, methodGroup.MemberName);
                writer.Append($"/* unresolved method group: {methodGroup.MemberName} */");
                break;

            case LocalReferenceExpression localRef:
                writer.Append(LocalNames.Of(localRef.Local));

                if(localRef.IsNarrowed
                    && OptionalUnion.IsOptional(TypeNameEmitter.Closed(localRef.Local.Type))
                    && TypeNameEmitter.IsValueKind(TypeNameEmitter.Closed(localRef.Type)))
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
            case PropertyReferenceExpression projected when ProjectedValueAccess.Declaring(projected) is { } declaring:
                writer.Append($"{TypeNameEmitter.Emit(declaring)}.{projected.Property.Name}");
                break;

            case PropertyReferenceExpression propertyRef:
                writer.Append(
                    UnsafeReach.Implicit(propertyRef.Property)
                    ?? ReceiverSpelling.Qualifier(propertyRef.Property.DeclaringType, propertyRef.Property.Modifiers.IsStatic) + propertyRef.Property.Name);
                break;
            case BackingFieldReferenceExpression backing:
                writer.Append(BackingStorage.DeclaredField(backing.Property) ?? "field");
                break;

            case NewExpression newExpression:
                EmitNew(newExpression, writer, diagnostics);
                break;

            case LiteralExpression literal:
                writer.Append(EmitLiteral(literal));
                break;

            case VoidLiteralExpression voidLiteral:
                writer.Append(
                    voidLiteral.Reading == VoidReading.Absence && OptionalUnion.IsOptional(TypeNameEmitter.Closed(voidLiteral.Type))
                        ? "null"
                        : "Standard.Void.Instance");
                break;

            case RegexLiteralExpression regexLiteral:
                writer.Append(RegexLowering.Construct(RegexLowering.Quote(regexLiteral.Pattern)));
                break;


            case TypeLiteralExpression typeLiteral:
                writer.Append($"typeof({TypeNameEmitter.EmitUnannotated(typeLiteral.ReferencedType)})");
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
        TupleLiteralExpression tupleLiteral,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        IReadOnlyList<IMistType>? slots = null)
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

            EmitEntering(tupleLiteral.Elements[i], slots?[i], writer, diagnostics);
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
            // A lambda needs a delegate type before C# can pass it through a union carrier's operator.
            if(lambda.UnionCarrierDelegateType is { } arm)
            {
                writer.Append($"({TypeNameEmitter.Emit(arm)})(");
            }

            EmitLambdaBody(lambda, writer, diagnostics);

            if(lambda.UnionCarrierDelegateType is not null)
            {
                writer.Append(")");
            }
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

    private static bool ReturnsNothing(LambdaExpression lambda)
    {
        var result = ResultTypeOf(lambda);

        return OptionalUnion.IsVoid(lambda.IsAsync ? TaskResult.Of(result) ?? result : result);
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
            var discarded = ReturnsNothing(lambda);

            var slot = lambda.IsAsync ? TaskResult.Of(ResultTypeOf(lambda)) : ResultTypeOf(lambda);

            if(!NeedsStatements(result, discarded) && LentValues.Within(result).Count == 0)
            {
                EmitEntering(result, slot, writer, diagnostics);
                return;
            }

            writer.AppendLine();

            using(writer.Block())
            {
                List<(string?, string)> held = [.. LifetimeLowering.Lend(result, writer).Select(value => ((string?)value.Flag, Released(value.Temp, value.Type)))];
                EmitWrapped(held, 0, () => EmitSequenced(result, discarded ? null : "return ", writer, diagnostics, slot: slot), writer);
            }

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
        if(TypeNameEmitter.Closed(isPattern.Scrutinee.Type) is UnionType union && !OptionalUnion.IsOptional(union))
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

        EmitScrutinee(isPattern.Scrutinee, RelationalPrecedence, writer, diagnostics);
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

    private static string RenderPatternCondition(Pattern pattern, string value, DiagnosticBag diagnostics)
    {
        var (text, hoisted) = RenderPattern(pattern, diagnostics);
        return $"({value} is {text}{string.Concat(hoisted.Select(test => $" && {test.Text}"))})";
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

        if(PatternConstituent(isPattern.Pattern) is not { } constituent)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedUnionPattern, isPattern.Node, null, union.Name);
            writer.Append($"/* unsupported: pattern against union {union.Name} */");
            return;
        }

        var (text, hoisted) = RenderPattern(isPattern.Pattern, diagnostics);

        if(hoisted.Count > 0)
        {
            writer.Append("(");
        }

        EmitSubExpression(isPattern.Scrutinee, PrefixPrecedence, writer, diagnostics);
        writer.Append($".{UnionCarrierNames.NarrowingAccessor(TypeNameEmitter.Closed(constituent), union)} is {text}");

        if(hoisted.Count > 0)
        {
            EmitHoistedTests(hoisted, " && ", writer, diagnostics);
            writer.Append(")");
        }
    }

    /// <summary>
    /// Emits a pattern, parenthesizing it when it binds looser than the combinator applying to it.
    /// Needed for the same reason the expression side needs it: the model carries no grouping, so
    /// <c>(a or b) and c</c> arrives as a conjunction OF a disjunction and written bare would be C# for
    /// <c>a or (b and c)</c>. Mist and C# rank these identically -- or, then and, then not.
    /// </summary>
    internal static void EmitScrutinee(Expression scrutinee, int precedence, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(scrutinee.Type is UnionType && TypeNameEmitter.Closed(scrutinee.Type) is not UnionType)
        {
            writer.Append("((object)");
            EmitSubExpression(scrutinee, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        EmitSubExpression(scrutinee, precedence, writer, diagnostics);
    }

    private static void EmitSwitch(
        SwitchExpression switchExpression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var narrowed = NarrowedUnion(switchExpression);

        EmitScrutinee(switchExpression.Scrutinee, SwitchPrecedence + 1, writer, diagnostics);
        writer.Append(" switch { ");

        EmitList.Separated(switchExpression.Arms, ", ", writer, arm =>
        {
            EmitArmHead(arm, narrowed, writer, diagnostics);
            writer.Append(" => ");
            EmitEntering(arm.Result, switchExpression.Type, writer, diagnostics);
        });

        writer.Append(" }");
    }

    internal static UnionType? NarrowedUnion(SwitchExpression switchExpression)
    {
        return TypeNameEmitter.Closed(switchExpression.Scrutinee.Type) is UnionType union && !OptionalUnion.IsOptional(union)
            ? union
            : null
            ;
    }

    internal static void EmitArmHead(
        SwitchArm arm, UnionType? narrowed, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var hoisted = new List<HoistedTest>();

        if(narrowed is { } union)
        {
            EmitUnionArmPattern(arm.Pattern, union, hoisted, writer, diagnostics);
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
        Pattern pattern, UnionType union, List<HoistedTest> hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(pattern is CatchAllPattern)
        {
            EmitPattern(pattern, 0, hoisted: null, writer, diagnostics);
            return;
        }

        if(PatternConstituent(pattern) is not { } constituent)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedUnionPattern, pattern.Node, null, union.Name);
            writer.Append($"var _ /* unsupported: pattern against union {union.Name} */");
            return;
        }

        writer.Append($"{{ {UnionCarrierNames.NarrowingAccessor(TypeNameEmitter.Closed(constituent), union)}: ");
        EmitPattern(pattern, 0, hoisted, writer, diagnostics);
        writer.Append(" }");
    }

    private static IMistType? PatternConstituent(Pattern pattern)
    {
        return pattern switch
        {
            TypeTestPattern typeTest => typeTest.Constituent,
            PropertyPattern property => property.TypeTest?.Constituent,
            _ => null,
        };
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
    /// being the same authority that spells the optional this produces. A target whose spelling drops a
    /// length takes the pattern too, <c>as</c> being able to test only the spelling; see
    /// <see cref="ErasedShape"/>.
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

        var target = TypeNameEmitter.EmitUnannotated(asExpression.TargetType);

        if(asExpression.Kind is AsConversionKind.Widening && UnionEntry.Factory(asExpression.Operand.Type, asExpression.TargetType) is { } entry)
        {
            writer.Append($"{entry}({Render(asExpression.Operand, diagnostics)})");
            return;
        }

        if(asExpression.Kind is AsConversionKind.Widening)
        {
            writer.Append($"(({target})");
            EmitSubExpression(asExpression.Operand, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        var accessor = asExpression.Constituent is { } constituent && TypeNameEmitter.Closed(asExpression.Operand.Type) is UnionType carrier
            ? $".{UnionCarrierNames.NarrowingAccessor(TypeNameEmitter.Closed(constituent), carrier)}"
            : null
            ;

        var checkedAtRuntime = accessor is null
            || (asExpression.Constituent is { } narrowedTo ? TypeNameEmitter.Closed(narrowedTo).FullyQualifiedName : null)
                != TypeNameEmitter.Closed(asExpression.TargetType).FullyQualifiedName
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

        var tested = TestedType(asExpression.TargetType, asExpression.Node, diagnostics);

        if(!TypeNameEmitter.IsValueKind(TypeNameEmitter.Closed(asExpression.TargetType))
            && ErasedShape.Members(asExpression.TargetType).Count == 0)
        {
            writer.Append($" as {target})");
            return;
        }

        var bound = $"__as{asExpression.Node.Span.Start}";
        writer.Append($" is {tested} {bound} ? {bound} : default({TypeNameEmitter.EmitOptional(asExpression.TargetType)}))");
    }

    private static void EmitCoerced(
        Expression expression, IMistType? target, CSharpWriter writer, DiagnosticBag diagnostics, IMistType? entered = null)
    {
        if(DelegateLowering.NeedsConstruction(expression.Type, target))
        {
            var source = Render(expression, diagnostics);
            var bare = expression is LambdaExpression { UnionCarrierDelegateType: null }
                || (expression is FunctionReferenceExpression reference && !RespelledGroups.Applies(reference));
            writer.Append($"new {TypeNameEmitter.Emit(target!)}({(bare ? source : $"({source}).Invoke")})");
            return;
        }

        EmitEntering(expression, entered ?? target, writer, diagnostics);
    }

    [ThreadStatic]
    private static (TupleLiteralExpression Literal, IReadOnlyList<IMistType> Slots)? enteringTuple;

    internal static void EmitEntering(
        Expression value, IMistType? slot, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(UnionEntry.Unwrapped(value.Type, slot) is { } underlying)
        {
            var through = UnionEntry.Factory(underlying, slot);
            writer.Append($"{through}(({TypeNameEmitter.Emit(underlying)})");
            EmitSubExpression(value, PrefixPrecedence, writer, diagnostics);
            writer.Append(")");
            return;
        }

        if(UnionEntry.Factory(value.Type, slot) is { } factory)
        {
            writer.Append($"{factory}(");
            EmitExpression(value, writer, diagnostics);
            writer.Append(")");
            return;
        }

        if(value is not TupleLiteralExpression && UnionEntry.Decomposed(value.Type, slot, "") is not null)
        {
            var bound = TemporaryName();
            writer.Append("(");
            EmitSubExpression(value, RelationalPrecedence, writer, diagnostics);
            writer.Append($" is var {bound} ? {UnionEntry.Decomposed(value.Type, slot, bound)} : default)");
            return;
        }

        if(value is not TupleLiteralExpression tuple || UnionEntry.Elements(slot, tuple) is not { } elements)
        {
            EmitExpression(value, writer, diagnostics);
            return;
        }

        var enclosing = enteringTuple;
        enteringTuple = (tuple, elements);

        try
        {
            EmitExpression(value, writer, diagnostics);
        }
        finally
        {
            enteringTuple = enclosing;
        }
    }

    internal static string Render(Expression expression, DiagnosticBag diagnostics)
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

    internal static string RenderAt(Expression expression, int operatorPrecedence, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        EmitSubExpression(expression, operatorPrecedence, scratch, diagnostics);
        return scratch.ToString();
    }

    private static string RenderOperand(
        Expression operand, Operator? declared, IReadOnlyList<IMistType>? closed, int position, int precedence, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        EmitOperand(operand, declared, closed, position, precedence, scratch, diagnostics);
        return scratch.ToString();
    }

    /// <summary>
    /// C# lexes two adjacent minus signs as a decrement, so a negated operand that opens with a minus of
    /// its own, a nested negation or a negative literal, is parenthesized rather than written bare.
    /// </summary>
    private static string Prefixed(UnaryOperatorKind kind, string operand)
    {
        var symbol = MapUnaryOperator(kind);

        return symbol == "-" && operand.StartsWith('-') ? $"-({operand})" : symbol + operand;
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
    internal static string TemporaryName()
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
    /// A predicate requiring a call or numeric promotion travels as a guard on a captured value.
    /// <see cref="BooleanPatternConditions"/> keeps those guards inside their Boolean branch;
    /// structural patterns retain C# pattern precedence and bindings.
    /// </summary>
    private static void EmitPattern(
        Pattern pattern,
        int combinatorPrecedence,
        List<HoistedTest>? hoisted,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(hoisted is not null && BooleanPatternConditions.Requires(pattern))
        {
            var bound = TemporaryName();
            writer.Append($"var {bound}");
            hoisted.Add(new HoistedTest(BooleanPatternConditions.Render(pattern, bound,
                (nested, value) => RenderPatternCondition(nested, value, diagnostics), expression => Render(expression, diagnostics),
                typeTest => TestedType(typeTest.NarrowedType, typeTest.Node, diagnostics))));
            return;
        }

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

        if(typeTest.TestsAbsence)
        {
            writer.Append("null");
            return;
        }

        if(typeTest is { Constituent: null, Binding: null } && OptionalUnion.IsVoid(typeTest.NarrowedType))
        {
            writer.Append("(null or Standard.Void)");
            return;
        }

        writer.Append(TestedType(typeTest.NarrowedType, typeTest.Node, diagnostics));

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

    private static string TestedType(IMistType type, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        ReportUnstatedElements(type, node, diagnostics);
        return ErasedShape.Tested(type);
    }

    private static void ReportUnstatedElements(IMistType type, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        if(ErasedShape.UnstatedElements(type) is { } sequence)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedSizedElementTest, node, null, TypeDisplay.Mist(sequence));
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
    private static void ReportUnreachedSlice(CountedSlice? slice, IMistType receiverType, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics)
    {
        if(slice is not null && UnsafeReach.Unreached(slice, receiverType) is { } member)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedImplicitSlice, node, null,
                TypeDisplay.Mist(receiverType), member, "a conversion run through a Clone body cannot reach it from the newtype");
        }
    }

    private static void EmitListPattern(
        ListPattern list, List<HoistedTest>? hoisted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(list.Slice is { Method: { } promoted } && AsyncPromotion.IsPromoted(promoted))
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.UnsupportedImplicitSlice, list.Node, null,
                TypeDisplay.Mist(list.NarrowedType), promoted.Name, "it waits on a task a list pattern has no place to wait for");
        }

        ReportUnreachedSlice(list.Slice, list.NarrowedType, list.Node, diagnostics);

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
                    EmitPattern(slice, 0, hoisted, writer, diagnostics);
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
        if(property.NarrowedType is UnknownType && property.Subpatterns.Count > 0)
        {
            EmitUnknownPropertyPattern(property, hoisted, writer, diagnostics);
            return;
        }

        IReadOnlyList<string> shape = [];

        if(property.TypeTest is { Refinement: null, TestsAbsence: false, Binding: null, NarrowedType: not RefinedType } merged
            && ErasedShape.Members(merged.NarrowedType) is { Count: > 0 } members)
        {
            ReportUnstatedElements(merged.NarrowedType, merged.Node, diagnostics);
            writer.Append($"{TypeNameEmitter.EmitAsPatternType(merged.NarrowedType)} ");
            shape = members;
        }
        else if(property.TypeTest is { } typeTest)
        {
            EmitTypeTest(typeTest, hoisted, writer, diagnostics);
            writer.Append(typeTest.Refinement is null && !typeTest.TestsAbsence ? " " : " and ");
        }

        if(shape.Count == 0 && property.Subpatterns.Count == 0)
        {
            writer.Append("{ }");
        }
        else
        {
            writer.Append($"{{ {string.Join(", ", shape)}");
            writer.Append(shape.Count > 0 && property.Subpatterns.Count > 0 ? ", " : "");
            EmitList.Separated(property.Subpatterns, ", ", writer, subpattern =>
            {
                writer.Append($"{subpattern.MemberName}: ");
                EmitPattern(subpattern.Pattern, 0, hoisted, writer, diagnostics);
            });
            writer.Append(" }");
        }

        if(property.Binding is { } whole)
        {
            writer.Append($" {LocalNames.Of(whole)}");
        }
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

        var bound = property.Binding is { } whole ? LocalNames.Of(whole) : TemporaryName();
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
            PropertyPattern { TypeTest.Refinement: not null } => ConjunctionPrecedence,
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
        // A type-parameter construction uses its supplied factory; native static initialization
        // retains the CLR constructor contract when no instance or method witness is available.
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

        if(RegexLowering.Is(newExpression.Type) && newExpression.Arguments is [var pattern])
        {
            writer.Append(RegexLowering.Construct(Render(pattern, diagnostics)));
            return;
        }

        var constructionFactory = TypeNameEmitter.Closed(newExpression.Type) is TypeParameter parameter
            ? ConstructorWitnesses.For(parameter, newExpression.ParameterTypes ?? newExpression.Arguments.Select(argument => argument.Type).ToList())
            : null;
        var target = constructionFactory is not null
            ? $"{ConstructorWitnesses.Name(constructionFactory)}("
            : newExpression.Type is TypeParameter && newExpression.Arguments.Count > 0
            ? $"{TypeNameEmitter.Emit(newExpression.Type)}.{ConstructorConstraintInterfaces.MemberName}("
            : UnsafeReach.Construction(newExpression) is { } reachedConstructor
                ? $"{reachedConstructor}("
                : $"new {TypeNameEmitter.Emit(newExpression.Type)}("
            ;

        var initialized = UnsafeReach.Initializer(newExpression);

        if(constructionFactory is not null && newExpression.Initializer.Count > 0 && initialized is null)
        {
            writer.Append(ConstructorInitializers.Apply(newExpression, expression => Render(expression, diagnostics)));
            return;
        }

        if(initialized is { } helper)
        {
            writer.Append($"{helper.Helper}(");
        }

        writer.Append(target);
        var declaration = ProgramModel.Construction.Defaultability.DeclarationOf(newExpression.Type);
        var witnesses = declaration is null ? [] : DefaultWitnesses.ConstructionArguments(declaration, newExpression.Type);
        witnesses?.AddRange(newExpression.Constructor is { } entered ? ContextKeyWitnesses.Arguments(entered, newExpression.ContextTypes) : []);

        if(witnesses is { Count: > 0 })
        {
            writer.Append(string.Join(", ", witnesses));

            if(HasArguments(newExpression.Arguments, newExpression.Constructor?.ParametersList))
            {
                writer.Append(", ");
            }
        }

        var tupleFactory = constructionFactory is not null && ConstructorWitnesses.UsesTuple(constructionFactory);

        if(tupleFactory)
        {
            writer.Append("(");
        }

        EmitArguments(
            newExpression.Arguments, newExpression.Constructor?.ParametersList, newExpression.ParameterTypes,
            writer, diagnostics, newExpression.Constructor);

        if(tupleFactory)
        {
            writer.Append(")");
        }

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

    private static string? CollidingName(Method? method, IMistType? receiverType)
    {
        var closed = receiverType is null ? null : TypeNameEmitter.Closed(receiverType);
        var receiver = closed is null || ReferenceEquals(closed, UnionSpecialization.Current()?.Definition)
            ? UnionSpecialization.Current()
            : closed;

        return method is not null && receiver is not null ? UnionSpecialization.CollidingName(method, receiver) : null;
    }

    private static void EmitMemberAccess(
        MemberAccessExpression memberAccess,
        (IReadOnlyList<IMistType> Arguments, IReadOnlyList<TypeParameter>? Parameters) typeArguments,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        string? memberName = null,
        IMistType? receiverAs = null,
        bool staticReceiver = false)
    {
        // A size-agnostic read of the pack, answered by the specialization rather than emitted: at
        // this width the arity is a constant, and there is no C# value standing for the pack to
        // read it off.
        if(PackArity(memberAccess) is { } arity)
        {
            writer.Append(arity.ToString(CultureInfo.InvariantCulture));
            return;
        }

        if(ProjectedValueAccess.Supplied(memberAccess) is { } supplied && DependentCarrierNames.ValueOf(supplied, memberAccess.Type) is { } value)
        {
            writer.Append(value);
            return;
        }

        if(ProjectedValueAccess.Declaring(memberAccess) is { } declaring)
        {
            writer.Append($"{TypeNameEmitter.Emit(declaring)}.{memberAccess.MemberName}");
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

        if(receiverAs is { } boundBase)
        {
            if(staticReceiver)
            {
                writer.Append(TypeNameEmitter.Emit(boundBase));
            }
            else
            {
                writer.Append("((");
                writer.Append(TypeNameEmitter.Emit(boundBase));
                writer.Append(")");
                EmitSubExpression(memberAccess.Receiver, PrefixPrecedence, writer, diagnostics);
                writer.Append(")");
            }
        }
        else
        {
            EmitSubExpression(memberAccess.Receiver, PostfixPrecedence, writer, diagnostics);
        }

        writer.Append(memberAccess.IsConditional ? "?." : ".");
        writer.Append(CalleeName(memberName ?? memberAccess.MemberName, typeArguments.Arguments, typeArguments.Parameters));
        writer.Append(EmitTypeArguments(typeArguments.Arguments, typeArguments.Parameters));
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
        IReadOnlyList<IMistType>? parameterTypes,
        int flattened,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        Method? callee)
    {
        var trailing = parameters.Count - flattened - 1;
        var absorbed = arguments.Count - flattened - trailing;

        for(var i = 0; i < flattened; i++)
        {
            Separate(i, writer);
            EmitArgument(arguments[i], parameters[i], parameterTypes is not null && i < parameterTypes.Count ? parameterTypes[i] : null,
                writer, diagnostics, callee);
        }

        Separate(flattened, writer);
        EmitPacked(arguments, flattened, absorbed,
            parameterTypes is not null && flattened < parameterTypes.Count ? parameterTypes[flattened] : parameters[flattened].Type,
            writer, diagnostics);

        for(var i = arguments.Count - trailing; i < arguments.Count; i++)
        {
            writer.Append(", ");
            var parameterIndex = parameters.Count - (arguments.Count - i);
            EmitArgument(arguments[i], parameters[parameterIndex],
                parameterTypes is not null && parameterIndex < parameterTypes.Count ? parameterTypes[parameterIndex] : null,
                writer, diagnostics, callee);
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
        IMistType tupleType,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        if(count <= 0)
        {
            writer.Append("default(System.ValueTuple)");
            return;
        }

        if(count == 1 && arguments[start] is SpreadExpression forwarded)
        {
            EmitExpression(forwarded.Operand, writer, diagnostics);
            return;
        }

        writer.Append(count == 1 ? "System.ValueTuple.Create(" : "(");

        var elements = tupleType switch
        {
            TupleType tuple => tuple.ElementTypes,
            NamedTupleType named => named.ElementTypes,
            _ => [],
        };

        for(var i = 0; i < count; i++)
        {
            Separate(i, writer);
            var argument = arguments[start + i];
            var target = i < elements.Count ? elements[i] : null;
            var pinned = target is null ? null : OverloadPinning.CastFor(argument, target, overloaded: true);

            if(pinned is not null)
            {
                writer.Append($"(({pinned})");
                if(argument is LambdaExpression)
                {
                    writer.Append("(");
                }

                EmitSubExpression(argument, PrefixPrecedence, writer, diagnostics);
                if(argument is LambdaExpression)
                {
                    writer.Append(")");
                }

                writer.Append(")");
            }
            else
            {
                EmitCoerced(argument, target, writer, diagnostics);
            }
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
        var elements = TypeNameEmitter.Closed(type) switch
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
    /// The arity a <c>TArgs.Length</c> stands for where it is being emitted, or null when this access
    /// is anything else.
    /// </summary>
    private static int? PackArity(MemberAccessExpression memberAccess)
    {
        return memberAccess.MemberName == "Length"
            && memberAccess.Receiver is TypeReferenceExpression { ReferencedType: TypeParameter { IsPack: true } pack }
                ? MonomorphReference.Width(TypeNameEmitter.Closed(pack))
                : null;
    }

    internal static string FunctionName(FunctionReferenceExpression reference)
    {
        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(reference.Method, reference.TypeArguments);
        return CalleeName(CollidingName(reference.Method, reference.Receiver?.Type) ?? LocalNames.Of(reference.Method), arguments, parameters)
            + EmitTypeArguments(arguments, parameters);
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
        if(TypeNameEmitter.SpecializedCallName(typeArguments, parameters) is { } specialized)
        {
            return specialized;
        }

        return MonomorphReference.MethodWidths([.. typeArguments.Select(TypeNameEmitter.Closed)], parameters) is { } widths
            ? MonomorphSet.SpecializedName(declaredName, widths)
            : declaredName;
    }

    private static string EmitTypeArguments(
        IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter>? parameters = null)
    {
        if(typeArguments.Count == 0 || TypeNameEmitter.SpecializedCallName(typeArguments, parameters) is not null)
        {
            return "";
        }

        // A pack argument is not written: the specialization already fixed the width, and its
        // elements stand where the pack did.
        var closed = typeArguments.Select(TypeNameEmitter.Closed).ToList();

        if(MonomorphReference.MethodWidths(closed, parameters) is not null)
        {
            return MonomorphReference.SpecializedArguments(closed, parameters!, TypeNameEmitter.Emit);
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
            float f => $"{f.ToString("R", CultureInfo.InvariantCulture)}f",
            double d => EmitDoubleLiteral(d),
            decimal m => $"{m.ToString(CultureInfo.InvariantCulture)}m",
            bool b => b ? "true" : "false",
            char c => c is >= ' ' and <= '~' and not ('\'' or '\\' or '"')
                ? $"'{c}'"
                : DependentCarrierNames.Literal(c),
            var value when IntegralConstant.Of(value) is { } integral => EmitIntegerLiteral(integral, literal.Type),
            _ => Convert.ToString(literal.Value, CultureInfo.InvariantCulture)!,
        };
    }

    /// <summary>
    /// Emits an integer literal with the appropriate type suffix based on the
    /// expression's resolved type. Without suffixes, C# interprets bare integer
    /// literals as <c>int</c>, which fails to compile when the target type requires
    /// a wider or unsigned representation (e.g., <c>ulong x = 3000000000;</c>).
    /// </summary>
    private static string EmitIntegerLiteral(Int128 value, IMistType type)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var typeName = type is MemberType mt ? mt.Name : "";
        return typeName switch
        {
            "UInt64" => $"{digits}UL",
            "Int64" => $"{digits}L",
            "UInt32" => $"{digits}U",
            _ => digits,
        };
    }

    /// <summary>
    /// Emits a double literal, ensuring it always contains a decimal point or
    /// exponent so C# doesn't parse it as an integer.
    /// </summary>
    private static string EmitDoubleLiteral(double d)
    {
        var text = d.ToString("R", CultureInfo.InvariantCulture);
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
            LiteralExpression negative when EmitLiteral(negative).StartsWith('-') => PrefixPrecedence,
            InvocationExpression drop when TaskResult.Dropped(drop) is not null => PrefixPrecedence,
            InvocationExpression or IndexExpression when CompletesPromoted(expression) && !blockingAwaits => PrefixPrecedence,
            IsPatternExpression => RelationalPrecedence,
            AsExpression { Kind: AsConversionKind.Identity } identity => GetPrecedence(identity.Operand),
            SwitchExpression => SwitchPrecedence,
            CoalesceExpression => CoalescePrecedence,
            AssignmentExpression => AssignmentPrecedence,
            _ => int.MaxValue,
        };
    }

    internal static int GetPrecedence(BinaryOperatorKind kind)
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