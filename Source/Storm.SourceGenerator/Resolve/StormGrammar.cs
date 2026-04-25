using System;
using System.Collections.Generic;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;

namespace Storm.SourceGenerator.Resolve;

/// <summary>
/// Represents the effective grammar produced by merging a root Storm file
/// with all its resolved imports. Enforces duplicate-name detection for
/// unnamespaced declarations and establishes the effective declaration order
/// for token tiebreaking (R6.8).
/// </summary>
public sealed class StormGrammar
{
    /// <summary>
    /// All declarations in effective order:
    /// root first, then unnamespaced imports in import-appearance order,
    /// then namespaced-only declarations last (R6.8).
    /// </summary>
    public IReadOnlyList<StormDeclaration> Declarations { get; }

    /// <summary>
    /// Qualified name → declaration for namespaced imports.
    /// Keys are of the form "Namespace.Name".
    /// </summary>
    public IReadOnlyDictionary<string, StormDeclaration> NamespacedDeclarations { get; }

    /// <summary>
    /// Diagnostics produced during grammar construction (e.g. duplicate names).
    /// </summary>
    public IReadOnlyList<StormDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Whether grammar construction completed without errors.
    /// </summary>
    public bool Succeeded { get; }

    private Dictionary<string, StormDeclaration> declarationsByName;

    private StormGrammar(
        IReadOnlyList<StormDeclaration> declarations,
        IReadOnlyDictionary<string, StormDeclaration> namespacedDeclarations,
        Dictionary<string, StormDeclaration> declarationsByName,
        IReadOnlyList<StormDiagnostic> diagnostics,
        bool succeeded)
    {
        Declarations = declarations;
        NamespacedDeclarations = namespacedDeclarations;
        this.declarationsByName = declarationsByName;
        Diagnostics = diagnostics;
        Succeeded = succeeded;
    }

    /// <summary>
    /// Looks up a declaration by name. Supports both unqualified names
    /// (searched in the unnamespaced declaration set) and qualified names
    /// of the form "Namespace.Name" (searched in the namespaced declaration set).
    /// </summary>
    /// <param name="name">The unqualified or qualified name to look up.</param>
    /// <returns>The declaration if found; otherwise null.</returns>
    public StormDeclaration? LookupByName(string name)
    {
        if(name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }
        // Try unqualified lookup first
        if(declarationsByName.TryGetValue(name, out var decl))
        {
            return decl;
        }

        // Try qualified lookup
        if(NamespacedDeclarations is Dictionary<string, StormDeclaration> nsDict)
        {
            if(nsDict.TryGetValue(name, out var nsDecl))
            {
                return nsDecl;
            }
        }
        else
        {
            // Fallback for IReadOnlyDictionary
            if(NamespacedDeclarations.TryGetValue(name, out var nsDecl))
            {
                return nsDecl;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds a <see cref="StormGrammar"/> from an <see cref="ImportResolutionResult"/>.
    /// </summary>
    public static StormGrammar Build(ImportResolutionResult resolution)
    {
        if(resolution == null)
        {
            throw new ArgumentNullException(nameof(resolution));
        }

        var diagnostics = new List<StormDiagnostic>();
        var declarations = new List<StormDeclaration>();
        var namespacedDeclarations = new Dictionary<string, StormDeclaration>(StringComparer.Ordinal);
        var declarationsByName = new Dictionary<string, StormDeclaration>(StringComparer.Ordinal);

        // Track where each unnamespaced declaration name was first defined (for duplicate detection)
        var nameToSourceLocation = new Dictionary<string, (string FilePath, StormDeclaration Decl)>(StringComparer.Ordinal);

        var failed = false;

        // Track which files contribute only via namespaced imports
        // We need to know which files are imported with a namespace alias
        // so we can separate namespaced-only declarations

        // First pass: determine which files are imported with namespace aliases
        // and which are imported without (unnamespaced)
        var rootPath = resolution.RootPath;
        var rootFile = resolution.FilesByPath[rootPath];

        // We need to walk the import tree to determine:
        // 1. Which files contribute unnamespaced declarations (root + unnamespaced imports, recursively)
        // 2. Which files contribute namespaced declarations (namespaced imports)
        // 3. The order of contributions

        // Collect namespaced declarations from the root file's namespaced imports
        // and unnamespaced declarations from root + unnamespaced imports in DFS pre-order

        var visitedForMerge = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var namespacedFiles = new List<(string Alias, string Path, StormFile File)>();

        // Process root file and its unnamespaced imports recursively
        CollectDeclarations(
            rootPath,
            rootFile,
            resolution,
            visitedForMerge,
            declarations,
            namespacedFiles,
            declarationsByName,
            nameToSourceLocation,
            diagnostics,
            ref failed);

        // Now add namespaced declarations (R6.8: namespaced-only declarations last)
        var namespacedDeclsForOrder = new List<StormDeclaration>();
        foreach(var (alias, path, file) in namespacedFiles)
        {
            foreach(var decl in file.Declarations)
            {
                var qualifiedName = alias + "." + decl.Name;
                if(namespacedDeclarations.ContainsKey(qualifiedName))
                {
                    // Duplicate qualified name — this is an error
                    var existing = namespacedDeclarations[qualifiedName];
                    diagnostics.Add(new StormDiagnostic(
                        StormDiagnosticIds.STORM_DUPLICATE_NAME,
                        StormDiagnosticSeverity.Error,
                        $"Duplicate qualified declaration name '{qualifiedName}': first declared at {existing.Line}:{existing.Column}, redeclared at {decl.Line}:{decl.Column} in '{path}'",
                        path,
                        decl.Line,
                        decl.Column));
                    failed = true;
                }
                else
                {
                    namespacedDeclarations[qualifiedName] = decl;
                    namespacedDeclsForOrder.Add(decl);
                }
            }
        }

        // Append namespaced declarations to the effective order (R6.8)
        declarations.AddRange(namespacedDeclsForOrder);

        return new StormGrammar(
            declarations,
            namespacedDeclarations,
            declarationsByName,
            diagnostics,
            !failed);
    }

    /// <summary>
    /// Recursively collects declarations from a file and its unnamespaced imports in DFS pre-order.
    /// Namespaced imports are collected separately for later processing.
    /// </summary>
    private static void CollectDeclarations(
        string filePath,
        StormFile file,
        ImportResolutionResult resolution,
        HashSet<string> visitedForMerge,
        List<StormDeclaration> declarations,
        List<(string Alias, string Path, StormFile File)> namespacedFiles,
        Dictionary<string, StormDeclaration> declarationsByName,
        Dictionary<string, (string FilePath, StormDeclaration Decl)> nameToSourceLocation,
        List<StormDiagnostic> diagnostics,
        ref bool failed)
    {
        if(!visitedForMerge.Add(filePath))
        {
            return;
        }

        // Add this file's declarations to the unnamespaced set (DFS pre-order)
        foreach(var decl in file.Declarations)
        {
            if(nameToSourceLocation.TryGetValue(decl.Name, out var existing))
            {
                // Duplicate name in unnamespaced declarations (R6.5)
                diagnostics.Add(new StormDiagnostic(
                    StormDiagnosticIds.STORM_DUPLICATE_NAME,
                    StormDiagnosticSeverity.Error,
                    $"Duplicate declaration name '{decl.Name}': first declared in '{existing.FilePath}' at {existing.Decl.Line}:{existing.Decl.Column}, redeclared in '{filePath}' at {decl.Line}:{decl.Column}",
                    filePath,
                    decl.Line,
                    decl.Column));
                failed = true;
            }
            else
            {
                nameToSourceLocation[decl.Name] = (filePath, decl);
                declarationsByName[decl.Name] = decl;
                declarations.Add(decl);
            }
        }

        // Process imports in source order
        foreach(var import in file.Imports)
        {
            var resolvedPath = ResolveImportPath(filePath, import.Path);

            if(!resolution.FilesByPath.ContainsKey(resolvedPath))
            {
                // File wasn't resolved — diagnostic already emitted by StormImportResolver
                continue;
            }

            var importedFile = resolution.FilesByPath[resolvedPath];

            if(import.Alias != null)
            {
                // Namespaced import — collect for later processing
                namespacedFiles.Add((import.Alias, resolvedPath, importedFile));
            }
            else
            {
                // Unnamespaced import — recursively merge declarations
                CollectDeclarations(
                    resolvedPath,
                    importedFile,
                    resolution,
                    visitedForMerge,
                    declarations,
                    namespacedFiles,
                    declarationsByName,
                    nameToSourceLocation,
                    diagnostics,
                    ref failed);
            }
        }
    }

    /// <summary>
    /// Resolves an import path relative to the importing file's directory.
    /// </summary>
    private static string ResolveImportPath(string importingFilePath, string importPath)
    {
        var importingDir = GetDirectoryName(importingFilePath);
        var combined = CombinePath(importingDir, importPath);
        return StormImportResolver.CanonicalizePath(combined);
    }

    private static string GetDirectoryName(string path)
    {
        path = path.Replace('\\', '/');
        var lastSlash = path.LastIndexOf('/');
        return lastSlash >= 0 
            ? path.Substring(0, lastSlash) 
            : "."
            ;
    }

    private static string CombinePath(string directory, string relativePath)
    {
        return string.IsNullOrEmpty(directory) || directory == "." 
            ? relativePath 
            : directory + "/" + relativePath
            ;
    }
}
