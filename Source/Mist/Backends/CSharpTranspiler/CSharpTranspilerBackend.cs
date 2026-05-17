using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Backend that translates a semantic model into C# source code and invokes
/// the .NET compiler to produce an executable assembly.
/// </summary>
public sealed class CSharpTranspilerBackend : IBackend
{
    public BackendId Id => new("csharp");

    public IAmbientTypeResolver? GetAmbientTypeResolver() => new CSharpAmbientTypeResolver();

    public BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, string projectName, DiagnosticBag diagnostics)
    {
        var bag = new DiagnosticBag();

        try
        {
            // Step 1: Emit C# source files
            CSharpEmitter.Emit(model, outputDirectory, projectName, bag);

            if(bag.HasErrors)
            {
                // Requirement 5.10: if MIST_BE_0002 (or any backend error)
                // was raised during emission, produce no output artifact.
                // Clean up any partially-emitted files.
                CleanOutputDirectory(outputDirectory);

                return new BackendResult(
                    Succeeded: false,
                    PrimaryArtifactPath: null,
                    Diagnostics: bag.ToImmutableArray());
            }

            // Count emitted .cs files for progress reporting
            var emittedFileCount = Directory.Exists(outputDirectory)
                ? Directory.GetFiles(outputDirectory, "*.cs", SearchOption.AllDirectories).Length
                : 0;

            // Determine the entry point type name for the Roslyn compilation.
            // If the root project uses top-level statements, C# handles entry point
            // selection automatically. Otherwise, we need to specify which class
            // contains Main to avoid CS0017 when referenced projects also have Main.
            var mainTypeName = DetermineMainTypeName(model);

            // Step 2: Compile the emitted C# project
            var compileResult = CSharpCompilationDriver.Compile(outputDirectory, binDirectory, projectName, bag, emittedFileCount, mainTypeName);

            return compileResult;
        }
        catch(Exception ex)
        {
            var fullMessage = ex.InnerException is not null
                ? $"C# transpiler backend failed: {ex.Message} -> {ex.InnerException.Message}"
                : $"C# transpiler backend failed: {ex.Message}";
            bag.Add(new Diagnostic(
                new DiagnosticId("MIST_BACKEND_ERROR"),
                DiagnosticSeverity.Error,
                PipelineStage.Backend,
                fullMessage,
                Location: default,
                CompilationUnitPath: "<unknown>",
                AnalyzerName: null));

            return new BackendResult(
                Succeeded: false,
                PrimaryArtifactPath: null,
                Diagnostics: bag.ToImmutableArray());
        }
    }

    /// <summary>
    /// Determines the fully qualified type name containing the entry point Main method.
    /// Returns null if the root project uses top-level statements (C# handles this automatically)
    /// or if there is only one Main method across all compilation units.
    /// </summary>
    private static string? DetermineMainTypeName(ISemanticModel model)
    {
        // If the root project has top-level statements, C# uses those as the entry point.
        var hasRootTls = model.Compilation.Units
            .Any(u => !u.IsStdlib && u.OriginProjectPath is null && u.HasTopLevelStatements);

        if (hasRootTls)
            return null;

        // Check if any referenced project has a Main method. If so, we need to
        // specify the root project's Main class to avoid CS0017.
        var hasReferencedMain = false;
        string? rootMainClass = null;

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;
            if (symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
                continue;

            // Find which unit this symbol belongs to.
            Storm.Runtime.SyntaxNode? declaringNode = null;
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

            // Determine if this is from a referenced project or the root project.
            var isFromReference = false;
            for (var i = 0; i < model.Asts.Length && i < model.Units.Count; i++)
            {
                if (model.Units[i].OriginProjectPath is null)
                    continue;
                if (IsNodeInAst(declaringNode, model.Asts[i]))
                {
                    isFromReference = true;
                    break;
                }
            }

            if (isFromReference)
            {
                hasReferencedMain = true;
            }
            else
            {
                // This is a Main in the root project. Find its containing class.
                var typeScope = symbol.ContainingScope;
                var parentScope = typeScope?.Parent;
                if (parentScope is not null)
                {
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
                            var containingNamespace = member.DeclaredType switch
                            {
                                ClassType ct => ct.ContainingNamespace,
                                StructType st => st.ContainingNamespace,
                                _ => null,
                            };

                            rootMainClass = containingNamespace is not null
                                ? $"{containingNamespace}.{member.Name}"
                                : member.Name;
                            break;
                        }
                    }
                }
            }
        }

        // Only specify mainTypeName if there are multiple Main methods
        // (one in root + one or more in referenced projects).
        if (hasReferencedMain && rootMainClass is not null)
            return rootMainClass;

        return null;
    }

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
    /// Removes all emitted files from the output directory to ensure no
    /// output artifact is produced when the backend encounters errors
    /// (Requirement 5.10).
    /// </summary>
    private static void CleanOutputDirectory(string outputDirectory)
    {
        try
        {
            if (Directory.Exists(outputDirectory))
            {
                foreach (var file in Directory.GetFiles(outputDirectory))
                    File.Delete(file);
            }
        }
        catch
        {
            // Best-effort cleanup; the compilation already failed.
        }
    }
}
