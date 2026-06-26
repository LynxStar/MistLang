using System.Collections.Generic;
using System.Text;

namespace Storm.SourceGenerator.Frontend;

/// <summary>
/// Formats a <see cref="StormFile"/> AST back to Storm source text.
/// Supports mode declarations and action clauses for round-trip testing.
/// </summary>
public static class StormPrettyPrinter
{
    /// <summary>
    /// Pretty-prints a <see cref="StormFile"/> AST to Storm source text.
    /// </summary>
    public static string Print(StormFile file)
    {
        var sb = new StringBuilder();

        // Grammar declaration
        if(file.GrammarDeclaration is not null)
        {
            sb.AppendLine($"grammar {file.GrammarDeclaration.Name};");
            if(file.Imports.Count > 0 || file.Declarations.Count > 0)
            {
                sb.AppendLine();
            }
        }

        // Imports
        for(var i = 0; i < file.Imports.Count; i++)
        {
            var imp = file.Imports[i];
            if(imp.Alias is not null)
            {
                sb.AppendLine($"import \"{EscapeString(imp.Path)}\" as {imp.Alias};");
            }
            else
            {
                sb.AppendLine($"import \"{EscapeString(imp.Path)}\";");
            }
        }

        if(file.Imports.Count > 0 && file.Declarations.Count > 0)
        {
            sb.AppendLine();
        }

        // Declarations
        for(var i = 0; i < file.Declarations.Count; i++)
        {
            if(i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(PrintDeclaration(file.Declarations[i], indent: ""));
        }

        return sb.ToString();
    }

    private static string PrintDeclaration(StormDeclaration decl, string indent)
    {
        return decl switch
        {
            StormModeDecl mode => PrintModeDecl(mode, indent),
            StormRuleDecl rule => $"{indent}rule {rule.Name} = {PrintExpression(rule.Body)};\n",
            StormTokenDecl token => $"{indent}token {token.Name} = {PrintExpression(token.Body)}{PrintActionClause(token.Action)};\n",
            StormFragmentDecl fragment => $"{indent}fragment {fragment.Name} = {PrintExpression(fragment.Body)};\n",
            StormHiddenTokenDecl hidden => $"{indent}hidden token {hidden.Name} = {PrintExpression(hidden.Body)}{PrintActionClause(hidden.Action)};\n",
            _ => ""
        };
    }

    private static string PrintModeDecl(StormModeDecl mode, string indent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{indent}mode {mode.Name} {{");

        var innerIndent = indent + "    ";
        for(var i = 0; i < mode.Declarations.Count; i++)
        {
            sb.Append(PrintDeclaration(mode.Declarations[i], innerIndent));
        }

        sb.AppendLine($"{indent}}}");
        return sb.ToString();
    }

    private static string PrintExpression(StormExpression expr)
    {
        return expr switch
        {
            StormAlternationExpr alt => PrintAlternation(alt),
            StormSequenceExpr seq => PrintSequence(seq),
            StormNamedAlternative named => $"{PrintExpression(named.Expression)} as {named.AlternativeName}",
            StormLabeledExpr labeled => $"{labeled.Label}:{PrintPostfix(labeled.Element)}",
            StormOptionalExpr opt => $"{PrintPostfix(opt.Inner)}?",
            StormZeroOrMoreExpr zom => $"{PrintPostfix(zom.Inner)}*",
            StormOneOrMoreExpr oom => $"{PrintPostfix(oom.Inner)}+",
            StormGroupExpr group => $"({PrintExpression(group.Inner)})",
            StormStringLiteralExpr str => $"\"{EscapeString(str.Value)}\"",
            StormCharClassExpr cc => cc.Text,
            StormRegexLiteralExpr regex => $"`{regex.Pattern}`",
            StormReferenceExpr refExpr => refExpr.Name,
            StormQualifiedReferenceExpr qref => $"{qref.Qualifier}.{qref.Name}",
            _ => ""
        };
    }

    /// <summary>
    /// Prints a postfix-level expression, wrapping in parentheses if needed
    /// to preserve precedence for postfix operators.
    /// </summary>
    private static string PrintPostfix(StormExpression expr)
    {
        return expr switch
        {
            // These need parentheses when used as the inner of a postfix operator
            StormAlternationExpr or StormSequenceExpr or StormNamedAlternative or StormLabeledExpr
                => $"({PrintExpression(expr)})",
            _ => PrintExpression(expr)
        };
    }

    private static string PrintAlternation(StormAlternationExpr alt)
    {
        var sb = new StringBuilder();
        for(var i = 0; i < alt.Alternatives.Count; i++)
        {
            if(i > 0)
            {
                sb.Append(" | ");
            }

            sb.Append(PrintExpression(alt.Alternatives[i]));
        }
        return sb.ToString();
    }

    private static string PrintSequence(StormSequenceExpr seq)
    {
        var sb = new StringBuilder();
        for(var i = 0; i < seq.Elements.Count; i++)
        {
            if(i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(PrintExpression(seq.Elements[i]));
        }
        return sb.ToString();
    }

    private static string PrintActionClause(StormModeAction? action)
    {
        return action switch
        {
            StormPushAction push => $" => push {push.ModeName}",
            StormPopAction => " => pop",
            _ => ""
        };
    }

    private static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach(var c in value)
        {
            switch(c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}