using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
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
                if(returnStmt.Value is not null)
                {
                    writer.Write("return ");
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

            case IfStatement ifStmt:
                EmitIf(ifStmt, writer, diagnostics);
                break;

            case WhileStatement whileStmt:
                writer.Write("while (");
                EmitExpression(whileStmt.Condition, writer, diagnostics);
                writer.Append(")");
                writer.AppendLine();
                using(writer.Block())
                {
                    EmitBlock(whileStmt.Body, writer, diagnostics);
                }

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

    private static void EmitLocalDeclaration(LocalDeclarationStatement localDecl, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var typeName = TypeNameEmitter.Emit(localDecl.Local.Type);
        writer.Write($"{typeName} {localDecl.Local.Name}");
        if(localDecl.Initializer is not null)
        {
            writer.Append(" = ");
            EmitExpression(localDecl.Initializer, writer, diagnostics);
        }

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

        if(ifStmt.ElseBranch is IfStatement elseIf)
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
            case InvocationExpression invocation:
                EmitExpression(invocation.Receiver, writer, diagnostics);
                writer.Append("(");
                EmitList.Separated(invocation.Arguments, ", ", writer, argument => EmitExpression(argument, writer, diagnostics));
                writer.Append(")");
                break;

            case MemberAccessExpression memberAccess:
                EmitExpression(memberAccess.Receiver, writer, diagnostics);
                writer.Append($".{memberAccess.MemberName}{EmitTypeArguments(memberAccess.TypeArguments)}");
                break;

            case BinaryExpression binary:
                EmitSubExpression(binary.Left, binary, writer, diagnostics);
                writer.Append($" {MapBinaryOperator(binary.OperatorKind)} ");
                EmitSubExpression(binary.Right, binary, writer, diagnostics);
                break;

            case UnaryExpression unary:
                writer.Append(MapUnaryOperator(unary.OperatorKind));
                EmitExpression(unary.Operand, writer, diagnostics);
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

            case AssignmentExpression assignment:
                EmitExpression(assignment.Target, writer, diagnostics);
                writer.Append(" = ");
                EmitExpression(assignment.Value, writer, diagnostics);
                break;

            case IndexExpression elementAccess:
                EmitExpression(elementAccess.Receiver, writer, diagnostics);
                writer.Append("[");
                EmitExpression(elementAccess.Index, writer, diagnostics);
                writer.Append("]");
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
                            EmitExpression(hole.Expression, writer, diagnostics);
                            writer.Append("}");
                            break;
                    }
                }

                writer.Append("\"");
                break;

            case TypeReferenceExpression typeRef:
                writer.Append(TypeNameEmitter.Emit(typeRef.ReferencedType));
                break;

            case MethodReferenceExpression methodRef:
                writer.Append($"{methodRef.Method.Name}{EmitTypeArguments(methodRef.TypeArguments)}");
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
        EmitList.Separated(newExpression.Arguments, ", ", writer, argument => EmitExpression(argument, writer, diagnostics));
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

    private static string EmitTypeArguments(IReadOnlyList<IMistType> typeArguments)
    {
        if(typeArguments.Count == 0)
        {
            return "";
        }

        return $"<{string.Join(", ", typeArguments.Select(TypeNameEmitter.Emit))}>";
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
    /// Emits a sub-expression of a binary expression, wrapping it in parentheses when
    /// the child has lower precedence than the parent to preserve evaluation order.
    /// </summary>
    private static void EmitSubExpression(Expression child, BinaryExpression parent, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(child is BinaryExpression childBinary && GetPrecedence(childBinary.OperatorKind) < GetPrecedence(parent.OperatorKind))
        {
            writer.Append("(");
            EmitExpression(child, writer, diagnostics);
            writer.Append(")");
        }
        else
        {
            EmitExpression(child, writer, diagnostics);
        }
    }

    private static int GetPrecedence(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.LogicalOr => 1,
            BinaryOperatorKind.LogicalAnd => 2,
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => 3,
            BinaryOperatorKind.LessThan or BinaryOperatorKind.GreaterThan or
            BinaryOperatorKind.LessThanOrEqual or BinaryOperatorKind.GreaterThanOrEqual => 4,
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
            _ => "/* unknown unary */",
        };
    }
}