using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits C# source for method bodies by walking the Statement/Expression model.
/// </summary>
internal static class BodyEmitter
{
    public static void EmitBlock(Block block, CSharpWriter writer)
    {
        foreach(var statement in block.Statements)
        {
            EmitStatement(statement, writer);
        }
    }

    private static void EmitStatement(Statement statement, CSharpWriter writer)
    {
        switch(statement)
        {
            case ExpressionStatement exprStmt:
                writer.Write("");
                EmitExpression(exprStmt.Expression, writer);
                writer.Append(";");
                writer.AppendLine();
                break;

            case ReturnStatement returnStmt:
                if(returnStmt.Value is not null)
                {
                    writer.Write("return ");
                    EmitExpression(returnStmt.Value, writer);
                    writer.Append(";");
                    writer.AppendLine();
                }
                else
                {
                    writer.WriteLine("return;");
                }

                break;

            case LocalDeclarationStatement localDecl:
                EmitLocalDeclaration(localDecl, writer);
                break;

            case IfStatement ifStmt:
                EmitIf(ifStmt, writer);
                break;

            case WhileStatement whileStmt:
                writer.Write("while (");
                EmitExpression(whileStmt.Condition, writer);
                writer.Append(")");
                writer.AppendLine();
                using(writer.Block())
                {
                    EmitBlock(whileStmt.Body, writer);
                }

                break;

            case ForStatement forStmt:
                EmitFor(forStmt, writer);
                break;

            case ForEachStatement foreachStmt:
                writer.Write($"foreach ({TypeNameEmitter.Emit(foreachStmt.Variable.Type)} {foreachStmt.Variable.Name} in ");
                EmitExpression(foreachStmt.Collection, writer);
                writer.Append(")");
                writer.AppendLine();
                using(writer.Block())
                {
                    EmitBlock(foreachStmt.Body, writer);
                }

                break;

            case BreakStatement:
                writer.WriteLine("break;");
                break;

            case Block nestedBlock:
                using(writer.Block())
                {
                    EmitBlock(nestedBlock, writer);
                }

                break;

            default:
                writer.WriteLine($"/* unsupported statement: {statement.GetType().Name} */");
                break;
        }
    }

    private static void EmitLocalDeclaration(LocalDeclarationStatement localDecl, CSharpWriter writer)
    {
        var typeName = TypeNameEmitter.Emit(localDecl.Local.Type);
        writer.Write($"{typeName} {localDecl.Local.Name}");
        if(localDecl.Initializer is not null)
        {
            writer.Append(" = ");
            EmitExpression(localDecl.Initializer, writer);
        }

        writer.Append(";");
        writer.AppendLine();
    }

    private static void EmitIf(IfStatement ifStmt, CSharpWriter writer)
    {
        writer.Write("if (");
        EmitExpression(ifStmt.Condition, writer);
        writer.Append(")");
        writer.AppendLine();
        using(writer.Block())
        {
            EmitBlock(ifStmt.ThenBranch, writer);
        }

        if(ifStmt.ElseBranch is IfStatement elseIf)
        {
            writer.Write("else ");
            EmitIf(elseIf, writer);
        }
        else if(ifStmt.ElseBranch is Block elseBlock)
        {
            writer.WriteLine("else");
            using(writer.Block())
            {
                EmitBlock(elseBlock, writer);
            }
        }
    }

    private static void EmitFor(ForStatement forStmt, CSharpWriter writer)
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
                EmitExpression(initDecl.Initializer, writer);
            }
        }
        else if(forStmt.Initializer is ExpressionStatement initExpr)
        {
            EmitExpression(initExpr.Expression, writer);
        }

        writer.Append("; ");

        // Condition
        if(forStmt.Condition is not null)
        {
            EmitExpression(forStmt.Condition, writer);
        }

        writer.Append("; ");

        // Iterator
        if(forStmt.Iterator is not null)
        {
            EmitExpression(forStmt.Iterator, writer);
        }

        writer.Append(")");
        writer.AppendLine();
        using(writer.Block())
        {
            EmitBlock(forStmt.Body, writer);
        }
    }

    private static void EmitExpression(Expression expression, CSharpWriter writer)
    {
        switch(expression)
        {
            case InvocationExpression invocation:
                EmitExpression(invocation.Receiver, writer);
                writer.Append("(");
                for(var i = 0; i < invocation.Arguments.Count; i++)
                {
                    if(i > 0)
                    {
                        writer.Append(", ");
                    }

                    EmitExpression(invocation.Arguments[i], writer);
                }

                writer.Append(")");
                break;

            case MemberAccessExpression memberAccess:
                EmitExpression(memberAccess.Receiver, writer);
                writer.Append($".{memberAccess.MemberName}");
                break;

            case BinaryExpression binary:
                EmitSubExpression(binary.Left, binary, writer);
                writer.Append($" {MapBinaryOperator(binary.OperatorKind)} ");
                EmitSubExpression(binary.Right, binary, writer);
                break;

            case UnaryExpression unary:
                writer.Append(MapUnaryOperator(unary.OperatorKind));
                EmitExpression(unary.Operand, writer);
                break;

            case AssignmentExpression assignment:
                EmitExpression(assignment.Target, writer);
                writer.Append(" = ");
                EmitExpression(assignment.Value, writer);
                break;

            case IndexExpression elementAccess:
                EmitExpression(elementAccess.Receiver, writer);
                writer.Append("[");
                EmitExpression(elementAccess.Index, writer);
                writer.Append("]");
                break;

            case ArrayCreationExpression arrayCreation:
                writer.Append($"new {TypeNameEmitter.Emit(arrayCreation.ElementType)}[");
                EmitExpression(arrayCreation.Size, writer);
                writer.Append("]");
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
                            EmitExpression(hole.Expression, writer);
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
                writer.Append(methodRef.Method.Name);
                break;

            case LocalReferenceExpression localRef:
                writer.Append(localRef.Local.Name);
                break;

            case LiteralExpression literal:
                writer.Append(EmitLiteral(literal));
                break;

            default:
                writer.Append($"/* unsupported: {expression.GetType().Name} */");
                break;
        }
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
    private static void EmitSubExpression(Expression child, BinaryExpression parent, CSharpWriter writer)
    {
        if(child is BinaryExpression childBinary && GetPrecedence(childBinary.OperatorKind) < GetPrecedence(parent.OperatorKind))
        {
            writer.Append("(");
            EmitExpression(child, writer);
            writer.Append(")");
        }
        else
        {
            EmitExpression(child, writer);
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