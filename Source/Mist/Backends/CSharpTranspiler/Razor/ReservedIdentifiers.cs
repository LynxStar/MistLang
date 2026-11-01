using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Razor.Syntax;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using SyntaxNode = Storm.Runtime.SyntaxNode;
using Token = Storm.Runtime.Token;

namespace Mist.Backends.CSharpTranspiler.Razor;

/// <summary>
/// Requirement 4.7 for the identifiers a component declares inside bodies, in its Code_Blocks and in the
/// markup code the render method holds: the translated class is parsed once, and a declaration whose
/// name the file wrote is refused where it was written, while the names the translator synthesizes,
/// <c>__builder</c> and the lambda parameters among them, are its own.
/// </summary>
internal static class ReservedIdentifiers
{
    public static bool IsReserved(string name)
    {
        return name.StartsWith("__", StringComparison.Ordinal);
    }

    public static void Refuse(SourceText unit, RazorSyntaxTree tree, TranslationContext context, RazorReporter reporter)
    {
        var bag = new DiagnosticBag(context.Diagnostics.Operation);
        var tokens = context.Lexer.Lex(unit, bag);
        var parsed = bag.HasErrors ? null : context.Parser.Parse(tokens, bag);
        foreach(var diagnostic in parsed?.Diagnostics ?? [])
        {
            bag.Add(diagnostic);
        }

        if(parsed?.Root is not { } root || unit.Origin is not { } origin || bag.HasErrors)
        {
            return;
        }

        foreach(var name in Declared(root).Where(name => IsReserved(name.Lexeme)))
        {
            var segment = origin.Segments.Last(segment => segment.GeneratedStart <= name.Span.Start);
            var start = segment.OriginalStart + name.Span.Start - segment.GeneratedStart;
            if(segment.Verbatim && ReferenceEquals(segment.Original, tree.Text)
                && !tree.Directives.Any(directive => directive.Span.Start <= start && start < directive.Span.End))
            {
                reporter.Report(tree.Text, RazorDiagnostics.ReservedIdentifier, new SourceSpan(start, name.Span.Length), new NameValue(name.Lexeme));
            }
        }
    }

    private static IEnumerable<Token> Declared(SyntaxNode node)
    {
        foreach(var name in Names(node))
        {
            yield return name;
        }

        foreach(var child in node.Children().Where(child => child is not null))
        {
            foreach(var name in Declared(child))
            {
                yield return name;
            }
        }
    }

    private static IEnumerable<Token> Names(SyntaxNode node)
    {
        return node switch
        {
            ParameterNode parameter => [parameter.Name],
            LocalFunctionDeclarationNode function => [function.Name],
            UninitializedVariableDeclarationNode variable => [variable.Name],
            TypedVariableDeclarationVariableDeclarationStatementNode variable => [variable.Name],
            InferredVariableDeclarationVariableDeclarationStatementNode variable => [variable.Name],
            VarTupleDeconstructionVariableDeclarationStatementNode tuple => [tuple.FirstName, .. tuple.RestNames],
            TupleDeconstructionElementNode element => [element.Name],
            ForInitializerVariableDeclarationForInitializerNode variable => [variable.Name],
            ForInitializerVarDeclarationForInitializerNode variable => [variable.Name],
            TypedForeachForeachStatementNode loop => [loop.Name],
            VarForeachForeachStatementNode loop => [loop.Name],
            WithVarDeclarationStatementNode with => [with.Name],
            WithTypedDeclarationStatementNode with => [with.Name],
            WithVarBlockStatementNode with => [with.Name],
            SingleImplicitParamLambdaParameterListNode lambda => [lambda.Name],
            TypedLambdaParamLambdaParamNode lambda => [lambda.Name],
            UntypedLambdaParamLambdaParamNode lambda => [lambda.Name],
            TypePatternPrimaryPatternNode pattern => [pattern.Name],
            VarPatternPrimaryPatternNode pattern => [pattern.Name],
            PropertyPatternPrimaryPatternNode { Name: { } name } => [name],
            _ => [],
        };
    }
}