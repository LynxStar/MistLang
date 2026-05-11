using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Generates the <c>MistContext.g.cs</c> runtime helper file when any compilation
/// unit in the program uses structured context (context declarations, context blocks,
/// or method with-clauses).
/// </summary>
internal static class MistContextGenerator
{
    /// <summary>
    /// Determines whether any compilation unit uses structured context by scanning
    /// the semantic model for method/constructor symbols with <see cref="Symbol.WithClauses"/>
    /// or context declaration statements in method bodies.
    /// </summary>
    public static bool UsesStructuredContext(ISemanticModel model)
    {
        // Check 1: Any method or constructor symbol with WithClauses.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;

            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;

            if (sym.WithClauses.Count > 0)
                return true;
        }

        // Check 2: Any context declaration or block statement in the ASTs.
        foreach (var ast in model.Asts)
        {
            if (AstContainsContextUsage(ast))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Generates the content of <c>MistContext.g.cs</c>.
    /// </summary>
    public static string GenerateMistContextFile()
    {
        return """
            #nullable enable
            using System;
            using System.Collections.Generic;

            [System.Runtime.CompilerServices.CompilerGenerated]
            internal static class MistContext
            {
                private static readonly Dictionary<Type, Stack<object>> _stacks = new();

                public static void Push<T>(object value)
                {
                    var type = typeof(T);
                    if (!_stacks.TryGetValue(type, out var stack))
                    {
                        stack = new Stack<object>();
                        _stacks[type] = stack;
                    }
                    stack.Push(value);
                }

                public static void Pop<T>()
                {
                    _stacks[typeof(T)].Pop();
                }

                public static T Peek<T>()
                {
                    return (T)_stacks[typeof(T)].Peek();
                }
            }
            """;
    }

    /// <summary>
    /// Recursively walks an AST node looking for any context usage nodes.
    /// </summary>
    private static bool AstContainsContextUsage(Storm.Runtime.SyntaxNode node)
    {
        switch (node)
        {
            case WithVarDeclarationStatementStatementNode:
            case WithTypedDeclarationStatementStatementNode:
            case WithDiscardDeclarationStatementStatementNode:
            case WithPushStatementStatementNode:
            case WithBlockStatementStatementNode:
                return true;
        }

        foreach (var child in node.Children())
        {
            if (child is Storm.Runtime.SyntaxNode childNode && AstContainsContextUsage(childNode))
                return true;
        }

        return false;
    }
}
