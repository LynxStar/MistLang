using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
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
    private const int RelationalPrecedence = 4;

    private const int PrefixPrecedence = 7;

    private const int DisjunctionPrecedence = 1;

    private const int ConjunctionPrecedence = 2;

    private const int NegationPrecedence = 3;

    public static void EmitBlock(Block block, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var statement in block.Statements)
        {
            EmitStatement(statement, writer, diagnostics);
        }
    }

    private static void EmitStatement(Statement statement, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        switch(statement)
        {
            case ExpressionStatement exprStmt:
                writer.Write("");
                EmitExpression(exprStmt.Expression, writer, diagnostics);
                writer.Append(";");
                writer.AppendLine();
                break;

            case ReturnStatement returnStmt:
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
                writer.Write($"foreach ({TypeNameEmitter.Emit(foreachStmt.Variable.Type)} {foreachStmt.Variable.Name} in ");
                EmitExpression(foreachStmt.Collection, writer, diagnostics);
                writer.Append(")");
                writer.AppendLine();
                using(writer.Block())
                {
                    EmitBlock(foreachStmt.Body, writer, diagnostics);
                }

                break;

            case BreakStatement:
                writer.WriteLine("break;");
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

            EmitCoerced(
                arguments[i],
                parameters is not null && i < parameters.Count ? parameters[i].Type : null,
                writer,
                diagnostics);
        }
    }

    private static void EmitLocalDeclaration(LocalDeclarationStatement localDecl, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var local = localDecl.Local;
        var refKeyword = RefKeywords.For(local.IsRef, local.IsReadonly);
        var typeName = $"{refKeyword}{TypeNameEmitter.Emit(local.Type)}";

        if(localDecl.Initializer is InvocationExpression call && OptionalUnion.IsVoid(call.Type))
        {
            writer.Write("");
            EmitExpression(call, writer, diagnostics);
            writer.Append(";");
            writer.AppendLine();
            writer.WriteLine($"{typeName} {localDecl.Local.Name} = Standard.Void.Instance;");
            return;
        }

        writer.Write($"{typeName} {local.Name}");
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

    private static void EmitWhile(WhileStatement whileStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Write("while (");
        EmitExpression(whileStmt.Condition, writer, diagnostics);
        writer.Append(")");
        writer.AppendLine();

        using(writer.Block())
        {
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
        var targets = deconstruction.Targets.Select(
            target => $"{TypeNameEmitter.Emit(target.Type)} {target.Name}");

        writer.Write($"({string.Join(", ", targets)}) = ");
        EmitExpression(deconstruction.Initializer, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
    }

    private static void EmitIf(IfStatement ifStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Write("if (");
        EmitExpression(ifStmt.Condition, writer, diagnostics);
        writer.Append(")");
        writer.AppendLine();
        using(writer.Block())
        {
            EmitBlock(ifStmt.ThenBranch, writer, diagnostics);
        }

        if(ifStmt.ElseBranch is IfStatement { ConditionBinds: true } bindingElseIf)
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
            writer.Append($"{typeName} {initDecl.Local.Name}");
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
        switch(expression)
        {
            case InvocationExpression invocation when FullMatchCall(invocation) is { } fullMatch:
                writer.Append(fullMatch);
                break;

            case InvocationExpression invocation:
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
                    EmitExpression(invocation.Receiver, writer, diagnostics);
                }

                writer.Append("(");
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
                EmitExpression(coalesce.Left, writer, diagnostics);
                writer.Append(" ?? ");
                EmitExpression(coalesce.Right, writer, diagnostics);
                break;

            case BinaryExpression binary:
                EmitSubExpression(binary.Left, GetPrecedence(binary.OperatorKind), writer, diagnostics);
                writer.Append($" {MapBinaryOperator(binary.OperatorKind)} ");
                EmitSubExpression(binary.Right, GetPrecedence(binary.OperatorKind), writer, diagnostics);
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

            case CastExpression cast:
                // The operand needs no parentheses of its own because the grammar bounds it to a
                // Unary, every form of which binds tighter than any binary operator in C# as well.
                // So `(Kelvin)a + b` arrives already shaped as a cast of `a`, and emitting the
                // operand bare cannot capture the `+ b`.
                writer.Append($"(({TypeNameEmitter.Emit(cast.Type)})");
                EmitExpression(cast.Operand, writer, diagnostics);
                writer.Append(")");
                break;

            case AsExpression asExpression:
                EmitAs(asExpression, writer, diagnostics);
                break;

            case AssignmentExpression assignment:
                EmitExpression(assignment.Target, writer, diagnostics);
                writer.Append(assignment.IsCoalescing ? " ??= " : " = ");
                EmitExpression(assignment.Value, writer, diagnostics);
                break;

            case IndexExpression elementAccess:
                EmitExpression(elementAccess.Receiver, writer, diagnostics);
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

            case ArrayLiteralExpression arrayLiteral:
                // Emitted with an explicit element type rather than as a C# collection
                // expression so the target type is never required at the use site.
                writer.Append($"new {TypeNameEmitter.Emit(arrayLiteral.ElementType)}[]");
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
                    CalleeName(methodRef.Method.Name, methodRef.TypeArguments, methodRef.Method.TypeParametersList)
                    + EmitTypeArguments(methodRef.TypeArguments, methodRef.Method.TypeParametersList));
                break;

            case FunctionReferenceExpression functionRef:
                if(functionRef.Receiver is { } functionTarget)
                {
                    EmitExpression(functionTarget, writer, diagnostics);
                    writer.Append(".");
                }

                writer.Append(functionRef.Method.Name);
                break;

            case MethodGroupExpression methodGroup:
                diagnostics.ReportAtNode(
                    BackendDiagnostics.MethodGroupReachedEmission, methodGroup.Node, null, methodGroup.MemberName);
                writer.Append($"/* unresolved method group: {methodGroup.MemberName} */");
                break;

            case LocalReferenceExpression localRef:
                writer.Append(localRef.Local.Name);
                break;

            case ThisExpression:
                writer.Append("this");
                break;

            case FieldReferenceExpression fieldRef:
                writer.Append(fieldRef.Field.Name);
                break;
            case PropertyReferenceExpression propertyRef:
                writer.Append(propertyRef.Property.Name);
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

            case DefaultExpression @default when EmitTypeDefault(@default.Type) is { } value:
                writer.Append(value);
                break;

            case DefaultExpression @default when @default.Type is MemberType memberType
                && DefaultSynthesis.IsDefaultable(memberType):
                writer.Append($"{TypeNameEmitter.Emit(@default.Type)}.{DefaultSynthesis.FactoryName}()");
                break;

            // An enum is defaultable exactly when it designates a variant, there being no other way
            // to choose between them -- unlike a class, whose fields each have a zero of their own.
            case DefaultExpression @default when @default.Type is EnumType { MembersList: { } variants }
                && variants.Exists(variant => variant.IsDefault):
                writer.Append($"{TypeNameEmitter.Emit(@default.Type)}.{DefaultSynthesis.FactoryName}()");
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
    /// </summary>
    internal static string? EmitTypeDefault(IMistType type)
    {
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
    private static void EmitLambda(
        LambdaExpression lambda, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var parameters = lambda.Parameters.Select(
            parameter => $"{TypeNameEmitter.Emit(parameter.Type)} {parameter.Name}");

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

        EmitExpression(switchExpression.Scrutinee, writer, diagnostics);
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
            EmitExpression(asExpression.Operand, writer, diagnostics);
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

    /// <summary>
    /// <c>r.IsFullMatch(x)</c>, which the CLR cannot be asked directly -- it matches partially, so a
    /// full match is the same question put to an anchored copy of the pattern.
    /// </summary>
    private static string? FullMatchCall(InvocationExpression invocation)
    {
        return invocation is
        {
            Receiver: MemberAccessExpression { MemberName: "IsFullMatch" } callee,
            Arguments: [{ } input],
        }
            && RegexLowering.Is(callee.Receiver.Type)
                ? RegexLowering.IsFullMatch(
                    Render(input, NoDiagnostics), RegexLowering.PatternOf(Render(callee.Receiver, NoDiagnostics)))
                : null;
    }

    /// <summary>
    /// A regex where a string is expected. Mist declares the conversion implicit and the CLR type
    /// has none, so it is materialised at the sites the emitter already knows a target type for.
    /// </summary>
    private static bool ConvertsRegexToString(Expression expression, IMistType? target)
    {
        return RegexLowering.Is(expression.Type)
            && target is not null
            && target.FullyQualifiedName == WellKnownTypes.String.FullyQualifiedName;
    }

    private static void EmitCoerced(
        Expression expression, IMistType? target, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(ConvertsRegexToString(expression, target))
        {
            writer.Append(RegexLowering.PatternOf(Render(expression, diagnostics)));
            return;
        }

        EmitExpression(expression, writer, diagnostics);
    }

    private static readonly DiagnosticBag NoDiagnostics = new();

    private static string Render(Expression expression, DiagnosticBag diagnostics)
    {
        var scratch = new CSharpWriter();
        EmitExpression(expression, scratch, diagnostics);
        return scratch.ToString();
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
    /// declared the same local. Deterministic because emission walks in a fixed order, and
    /// thread-static so parallel emissions cannot hand out the same number.
    /// </para>
    /// </summary>
    private static string TemporaryName()
    {
        return $"__e{++temporaries}";
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
                writer.Append(catchAll.Binding is { } caught ? $"var {caught.Name}" : "var _");
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
                EmitPropertyPattern(property, writer, diagnostics);
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
                writer.Append($" and var {bound.Name}");
            }

            return;
        }

        if(typeTest.Binding is { } tested)
        {
            writer.Append($" {tested.Name}");
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
            writer.Append($" {whole.Name}");
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
        PropertyPattern property, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(property.Subpatterns.Count == 0)
        {
            writer.Append("{ }");
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
        var target = newExpression.Type is TypeParameter && newExpression.Arguments.Count > 0
            ? $"{TypeNameEmitter.Emit(newExpression.Type)}.{ConstructorConstraintInterfaces.MemberName}("
            : $"new {TypeNameEmitter.Emit(newExpression.Type)}("
            ;

        writer.Append(target);
        EmitArguments(
            newExpression.Arguments, newExpression.Constructor?.ParametersList, writer, diagnostics);
        writer.Append(")");
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
            writer.Append($"{assignment.MemberName} = ");
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

        if(memberAccess.MemberName == "Pattern" && RegexLowering.Is(memberAccess.Receiver.Type))
        {
            writer.Append(RegexLowering.PatternOf(Render(memberAccess.Receiver, diagnostics)));
            return;
        }

        EmitExpression(memberAccess.Receiver, writer, diagnostics);
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

        return $"<{string.Join(", ", spelled)}>";
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
            BinaryExpression binary => GetPrecedence(binary.OperatorKind),
            IsPatternExpression => RelationalPrecedence,
            _ => int.MaxValue,
        };
    }

    private static int GetPrecedence(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.LogicalOr => 1,
            BinaryOperatorKind.LogicalAnd => 2,
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => 3,
            BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThanOrEqual => RelationalPrecedence,
            BinaryOperatorKind.Add or BinaryOperatorKind.Subtract or
            BinaryOperatorKind.Concatenate => 5,
            BinaryOperatorKind.Multiply or BinaryOperatorKind.Divide or
            BinaryOperatorKind.Modulo => 6,
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