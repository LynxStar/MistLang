using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that validates entry point rules for top-level statements
/// and <c>static void Main</c> methods. Enforces single-TLS-file constraints,
/// TLS+Main conflict resolution via <c>entry_point</c> config, and namespace
/// restrictions in TLS files.
/// </summary>
public sealed class EntryPointAnalyzer : IAnalyzer
{
    public string AnalyzerId => "EntryPoint";

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        var tlsUnits = model.Compilation.Units
            .Where(u => !u.IsStdlib && u.OriginProjectPath is null && u.HasTopLevelStatements)
            .ToList();

        // Rule 1: Multiple TLS files
        if (tlsUnits.Count > 1)
        {
            var paths = string.Join(", ", tlsUnits.Select(u => u.Source.Path));
            diagnostics.Add(new Diagnostic(
                DiagnosticId.MultipleTopLevelStatementFiles,
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                $"Multiple files contain top-level statements. Only one file per project may have top-level statements. Files: {paths}",
                Location: default,
                CompilationUnitPath: tlsUnits[0].Source.Path,
                AnalyzerName: AnalyzerId));
        }

        // Rule 2: TLS + Main conflict
        if (tlsUnits.Count == 1)
        {
            var mainClasses = FindClassesWithStaticVoidMain(model);
            if (mainClasses.Count > 0)
            {
                var entryPoint = model.Compilation.EntryPoint;
                if (entryPoint is null)
                {
                    // No entry_point config → error MIST_EP_0002
                    var tlsPath = tlsUnits[0].Source.Path;
                    var className = mainClasses[0];
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.TlsAndMainWithoutConfig,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        $"Project has both top-level statements (in '{tlsPath}') and a static void Main (in '{className}'). Add entry_point = \"top-level\" to mist.toml to resolve this conflict.",
                        Location: default,
                        CompilationUnitPath: tlsPath,
                        AnalyzerName: AnalyzerId));
                }
                else if (entryPoint != "top-level")
                {
                    // entry_point is set to a class name → error MIST_EP_0003
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.TlsWithClassEntryPoint,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        $"Cannot specify entry_point = \"{entryPoint}\" when a top-level statement file exists. A top-level statement file must always be the entry point. Use entry_point = \"top-level\" instead.",
                        Location: default,
                        CompilationUnitPath: tlsUnits[0].Source.Path,
                        AnalyzerName: AnalyzerId));
                }
                // If entryPoint == "top-level" → valid, no error
            }
        }

        // Rule 3: Multiple Mains without TLS
        if (tlsUnits.Count == 0)
        {
            var mainClasses = FindClassesWithStaticVoidMain(model);
            if (mainClasses.Count > 1)
            {
                var entryPoint = model.Compilation.EntryPoint;
                if (entryPoint is null)
                {
                    // No entry_point config → error MIST_EP_0004
                    var classNames = string.Join(", ", mainClasses);
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.MultipleMainsWithoutConfig,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        $"Multiple classes contain static void Main: {classNames}. Add entry_point = \"{mainClasses[0]}\" to mist.toml to specify which one to use.",
                        Location: default,
                        CompilationUnitPath: "",
                        AnalyzerName: AnalyzerId));
                }
                else if (entryPoint != "top-level")
                {
                    // entry_point is set to a class name → validate it exists in mainClasses
                    if (!mainClasses.Contains(entryPoint))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticId.EntryPointClassNotFound,
                            DiagnosticSeverity.Error,
                            PipelineStage.Analysis,
                            $"entry_point specifies '{entryPoint}' but no class with that name contains a static void Main method.",
                            Location: default,
                            CompilationUnitPath: "",
                            AnalyzerName: AnalyzerId));
                    }
                    // If the class is found in mainClasses → valid, no error
                }
                // If entryPoint == "top-level" → that's Rule 4, skip here
            }
        }

        // Rule 4: entry_point = "top-level" but no TLS file exists
        if (tlsUnits.Count == 0 && model.Compilation.EntryPoint == "top-level")
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.TopLevelEntryPointButNoTls,
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                "entry_point = \"top-level\" specified in mist.toml but no file contains top-level statements.",
                Location: default,
                CompilationUnitPath: "",
                AnalyzerName: AnalyzerId));
        }

        // Rule 5: TLS + block namespace declaration
        foreach (var tlsUnit in tlsUnits)
        {
            var unitIndex = -1;
            for (var i = 0; i < model.Units.Count; i++)
            {
                if (ReferenceEquals(model.Units[i], tlsUnit))
                {
                    unitIndex = i;
                    break;
                }
            }

            if (unitIndex < 0 || unitIndex >= model.Asts.Length)
                continue;

            if (model.Asts[unitIndex] is not CompilationUnitNode cu)
                continue;

            foreach (var item in cu.Items)
            {
                if (item is NamespaceItemTopLevelItemNode nsItem &&
                    nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.TlsWithBlockNamespace,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        "Top-level statements cannot coexist with block namespace declarations in the same file. Use a file-scoped namespace (namespace X;) instead.",
                        Location: default,
                        CompilationUnitPath: tlsUnit.Source.Path,
                        AnalyzerName: AnalyzerId));
                    break; // One error per file is sufficient
                }
            }
        }
    }

    /// <summary>
    /// Finds all class names that contain a valid <c>static void Main</c> method
    /// in user code (excluding stdlib and referenced project units).
    /// </summary>
    private static List<string> FindClassesWithStaticVoidMain(ISemanticModel model)
    {
        var referencedSourcePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in model.Units)
        {
            if (unit.OriginProjectPath is not null)
                referencedSourcePaths.Add(unit.Source.Path);
        }

        var classNames = new List<string>();

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
                continue;

            // Get the declaring AST node to inspect modifiers and return type.
            Storm.Runtime.SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if (declaringNode is not MethodDeclarationNode methodDecl)
                continue;

            // Must be static.
            if (!methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode))
                continue;

            // Must return void.
            if (methodDecl.ReturnType is not VoidTypeRefTypeReferenceNode)
                continue;

            // Must have either no parameters or a single string[] parameter.
            if (methodDecl.Parameters is not null)
            {
                var parameters = new List<ParameterNode>();
                foreach (var child in methodDecl.Parameters.Children())
                {
                    if (child is ParameterNode param)
                        parameters.Add(param);
                }

                if (parameters.Count != 1 || !IsStringArrayTypeRef(parameters[0].Type))
                    continue;
            }

            // Skip methods from referenced projects.
            if (IsFromReferencedProject(symbol, model, referencedSourcePaths))
                continue;

            // Find the containing class name.
            var className = FindContainingClassName(symbol, model);
            if (className is not null)
                classNames.Add(className);
        }

        return classNames;
    }

    /// <summary>
    /// Checks whether a type reference AST node represents <c>string[]</c>.
    /// </summary>
    private static bool IsStringArrayTypeRef(TypeReferenceNode typeRef)
    {
        if (typeRef is PrimitiveTypeRefTypeReferenceNode primitiveRef)
        {
            return primitiveRef.PrimitiveType is StringTypePrimitiveTypeNode
                   && primitiveRef.ArrayRank is not null;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a symbol belongs to a referenced project (not the root project).
    /// </summary>
    private static bool IsFromReferencedProject(
        ISymbol symbol,
        ISemanticModel model,
        HashSet<string> referencedSourcePaths)
    {
        if (referencedSourcePaths.Count == 0)
            return false;

        // Find the declaring node and check which AST (and thus which unit) it belongs to.
        Storm.Runtime.SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return false; }

        for (var i = 0; i < model.Asts.Length && i < model.Units.Count; i++)
        {
            if (!referencedSourcePaths.Contains(model.Units[i].Source.Path))
                continue;

            if (IsNodeInAst(declaringNode, model.Asts[i]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a node is contained within an AST root by walking the AST's children.
    /// </summary>
    private static bool IsNodeInAst(Storm.Runtime.SyntaxNode target, Storm.Runtime.SyntaxNode root)
    {
        if (ReferenceEquals(target, root))
            return true;

        foreach (var child in root.Children())
        {
            if (child is null)
                continue;

            if (IsNodeInAst(target, child))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the fully qualified class name containing the given method symbol.
    /// </summary>
    private static string? FindContainingClassName(ISymbol methodSymbol, ISemanticModel model)
    {
        // The method's ContainingScope is the class's member scope.
        // Walk up to find the class symbol in the parent scope.
        var typeScope = methodSymbol.ContainingScope;
        var parentScope = typeScope?.Parent;
        if (parentScope is null)
            return null;

        foreach (var member in parentScope.Members)
        {
            if (member.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var memberScope = member.DeclaredType switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope == typeScope)
            {
                // Found the containing type. Build the qualified name.
                var containingNamespace = member.DeclaredType switch
                {
                    ClassType ct => ct.ContainingNamespace,
                    StructType st => st.ContainingNamespace,
                    _ => null,
                };

                return containingNamespace is not null
                    ? $"{containingNamespace}.{member.Name}"
                    : member.Name;
            }
        }

        return null;
    }
}
