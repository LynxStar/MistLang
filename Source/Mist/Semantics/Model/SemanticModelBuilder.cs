using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Builds an immutable <see cref="SemanticModel"/> from a set of ASTs.
/// Walks ASTs to build symbols, scopes, and types, then freezes all mutable
/// builders before returning the immutable model.
/// </summary>
public sealed class SemanticModelBuilder : ISemanticModelBuilder
{
    public ISemanticModel Build(
        Compilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        DiagnosticBag diagnostics)
    {
        var globalScope = new Scope(parent: null);
        var symbolTable = new SymbolTable();

        // Walk each AST to discover and register symbols.
        // Concrete symbol/type construction is deferred to the Mist-language spec.
        // For now, the builder produces an empty but valid semantic model.
        foreach (var ast in asts)
        {
            try
            {
                WalkNode(ast, globalScope, symbolTable);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_SEMANTIC_ERROR"),
                    DiagnosticSeverity.Error,
                    PipelineStage.SemanticModelConstruction,
                    $"Error during semantic model construction: {ex.Message}",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: null));
            }
        }

        return new SemanticModel(
            compilation,
            compilation.Units.Select(u => u).ToList(),
            globalScope,
            symbolTable);
    }

    private static void WalkNode(
        Storm.Runtime.SyntaxNode node,
        Scope currentScope,
        SymbolTable symbolTable)
    {
        // TODO: Implement AST walking and symbol registration
        // once the Mist language spec defines concrete syntax.
        // For now, recursively visit children.
        foreach (var child in node.Children())
        {
            WalkNode(child, currentScope, symbolTable);
        }
    }
}
