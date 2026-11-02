using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace SnippetEmitter;

internal static class SnippetOutputKind
{
    internal static ProjectOutputType Infer(IEnumerable<string> paths, ILexer lexer, IParser parser)
    {
        foreach(var path in paths)
        {
            var diagnostics = new DiagnosticBag();
            var tokens = lexer.Lex(new SourceText(path, File.ReadAllText(path)), diagnostics);
            var result = parser.Parse(tokens, diagnostics);
            if(diagnostics.HasErrors || result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
                || result.Root is not { } root || MayStart(root))
            {
                return ProjectOutputType.Executable;
            }
        }

        return ProjectOutputType.Library;
    }

    // This only proves absence: the compiler still judges signatures, conflicts and explicit entry points.
    internal static bool MayStart(SyntaxNode node)
    {
        return node is StatementItemTopLevelItemNode
            || node is MethodDeclarationNode { Name.Lexeme: "Main" }
            || node.Children().Any(child => child is not null && MayStart(child));
    }
}