using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

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
                var typeName = TypeNameEmitter.Emit(localDecl.Local.Type);
                writer.Write($"{typeName} {localDecl.Local.Name}");
                if(localDecl.Initializer is not null)
                {
                    writer.Append(" = ");
                    EmitExpression(localDecl.Initializer, writer);
                }

                writer.Append(";");
                writer.AppendLine();
                break;

            default:
                writer.WriteLine($"/* unsupported statement: {statement.GetType().Name} */");
                break;
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

    private static string EmitLiteral(LiteralExpression literal)
    {
        if(literal.Value is null)
        {
            return "null";
        }

        return literal.Value switch
        {
            string s => $"\"{s}\"",
            long l => l.ToString(),
            int i => i.ToString(),
            bool b => b ? "true" : "false",
            char c => $"'{c}'",
            _ => literal.Value.ToString()!,
        };
    }
}