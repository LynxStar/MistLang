using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
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

    /// <summary>
    /// Cached <see cref="AssemblyTypeProvider"/> instance. Reused when the same merged
    /// assembly path set is used across multiple <c>Compile()</c> calls (the common case).
    /// This avoids recreating the MetadataLoadContext and reloading ~167 DLLs per compilation.
    /// The cache is keyed on the full merged path set (SDK + additional), so different
    /// path sets produce different providers.
    /// </summary>
    private static AssemblyTypeProvider? _cachedProvider;
    private static ImmutableArray<string> _cachedMergedPaths;
    private static readonly object _providerCacheLock = new();

    /// <summary>
    /// Stores the additional (non-SDK) assembly paths from the most recent
    /// <see cref="GetAmbientTypeResolver"/> call on THIS instance. These are passed to the
    /// C# compilation driver during emit so the downstream Roslyn compilation
    /// references the NuGet package and framework assemblies. Instance-scoped to avoid
    /// cross-test contamination when multiple CompilerDriver instances run in parallel.
    /// </summary>
    private ImmutableArray<string> _lastAdditionalAssemblyPaths;

    public IAmbientTypeResolver? GetAmbientTypeResolver(string targetFramework, ImmutableArray<string> additionalAssemblyPaths)
    {
        var diagnostics = new DiagnosticBag();

        // Discover SDK reference assembly path for the specified TFM.
        var sdkRefPath = SdkDiscovery.FindReferenceAssemblyPath(targetFramework, diagnostics);
        if(sdkRefPath is null)
        {
            // SDK not found — fall back to no assembly types.
            // The diagnostics are lost here but the compilation will still proceed
            // with only Mist-declared types and Standard Library types.
            return null;
        }

        // Get all DLL paths from the SDK reference assembly directory.
        var sdkPaths = GetCachedSdkAssemblyPaths(sdkRefPath);

        // Union SDK paths with additional assembly paths and de-duplicate
        // by file path (case-insensitive).
        var mergedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var path in sdkPaths)
        {
            mergedSet.Add(path);
        }

        if(!additionalAssemblyPaths.IsDefaultOrEmpty)
        {
            foreach(var path in additionalAssemblyPaths)
            {
                mergedSet.Add(path);
            }
        }

        var mergedPaths = mergedSet.ToImmutableArray();

        // Store the additional assembly paths for downstream C# compilation.
        _lastAdditionalAssemblyPaths = additionalAssemblyPaths.IsDefaultOrEmpty
            ? ImmutableArray<string>.Empty
            : additionalAssemblyPaths;

        // Reuse cached provider if the merged path set is identical.
        lock(_providerCacheLock)
        {
            if(_cachedProvider is not null
                && _cachedMergedPaths.Length == mergedPaths.Length
                && _cachedMergedPaths.All(p => mergedSet.Contains(p)))
            {
                return _cachedProvider;
            }

            // Create a new provider with the merged paths.
            var provider = new AssemblyTypeProvider(mergedPaths, diagnostics);
            _cachedProvider = provider;
            _cachedMergedPaths = mergedPaths;
            return provider;
        }
    }

    /// <summary>
    /// Cached SDK assembly paths. The SDK assembly list doesn't change during a process lifetime.
    /// </summary>
    private static ImmutableArray<string>? _sdkAssemblyPathsCache;
    private static string? _sdkAssemblyPathsCacheKey;
    private static readonly object _sdkAssemblyPathsCacheLock = new();

    private static ImmutableArray<string> GetCachedSdkAssemblyPaths(string sdkRefPath)
    {
        lock(_sdkAssemblyPathsCacheLock)
        {
            if(_sdkAssemblyPathsCache.HasValue && _sdkAssemblyPathsCacheKey == sdkRefPath)
            {
                return _sdkAssemblyPathsCache.Value;
            }

            var paths = Directory.GetFiles(sdkRefPath, "*.dll").ToImmutableArray();
            _sdkAssemblyPathsCache = paths;
            _sdkAssemblyPathsCacheKey = sdkRefPath;
            return paths;
        }
    }

    public BackendResult Emit(ISemanticModel model, string outputDirectory, string binDirectory, string projectName, DiagnosticBag diagnostics)
    {
        var bag = new DiagnosticBag();

        try
        {
            // Step 0: Run the needs-async propagation pass to determine which
            // methods require the C# `async` keyword in emitted code.
            NeedsAsyncPropagationPass.Run(model);

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
            var compileResult = CSharpCompilationDriver.Compile(outputDirectory, binDirectory, projectName, bag, emittedFileCount, mainTypeName, _lastAdditionalAssemblyPaths);

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

        if(hasRootTls)
        {
            return null;
        }

        // Check if any referenced project has a Main method. If so, we need to
        // specify the root project's Main class to avoid CS0017.
        var hasReferencedMain = false;
        string? rootMainClass = null;

        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.Method || symbol.Name != "Main")
            {
                continue;
            }

            // Find which unit this symbol belongs to.
            Storm.Runtime.SyntaxNode? declaringNode = null;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if(declaringNode is not MethodDeclarationNode methodDecl)
            {
                continue;
            }

            // Must be static.
            if(!methodDecl.Modifiers.Any(m => m is StaticModifierModifierNode))
            {
                continue;
            }

            // Valid entry point return types:
            // - void (standard or async entry point)
            // - int with async modifier (exit code entry point, Requirement 11.4)
            var isVoidReturn = methodDecl.ReturnType.First is VoidSingleTypeRefSingleTypeRefNode
                && methodDecl.ReturnType.SingleTypeRefs.Length == 0;
            var isIntReturn = methodDecl.ReturnType.First is PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef
                && methodDecl.ReturnType.SingleTypeRefs.Length == 0
                && primitiveRef.PrimitiveType is IntTypePrimitiveTypeNode
                && primitiveRef.ArrayRank is null;

            if(!isVoidReturn && !isIntReturn)
            {
                continue;
            }

            // int return type is only valid with the async modifier.
            if(isIntReturn && !methodDecl.Modifiers.Any(m => m is AsyncModifierModifierNode))
            {
                continue;
            }

            // Determine if this is from a referenced project or the root project.
            var isFromReference = false;
            for(var i = 0; i < model.Asts.Length && i < model.Units.Count; i++)
            {
                if(model.Units[i].OriginProjectPath is null)
                {
                    continue;
                }

                if(IsNodeInAst(declaringNode, model.Asts[i]))
                {
                    isFromReference = true;
                    break;
                }
            }

            if(isFromReference)
            {
                hasReferencedMain = true;
            }
            else
            {
                // This is a Main in the root project. Find its containing class.
                var typeScope = symbol.ContainingScope;
                var parentScope = typeScope?.Parent;
                if(parentScope is not null)
                {
                    foreach(var member in parentScope.Members)
                    {
                        if(member.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                        {
                            continue;
                        }

                        var memberScope = member.DeclaredType switch
                        {
                            ClassType ct => ct.MemberScope,
                            StructType st => st.MemberScope,
                            _ => null,
                        };

                        if(memberScope == typeScope)
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
        if(hasReferencedMain && rootMainClass is not null)
        {
            return rootMainClass;
        }

        return null;
    }

    private static bool IsNodeInAst(Storm.Runtime.SyntaxNode target, Storm.Runtime.SyntaxNode root)
    {
        // Walk up via Parent links to find the root — O(depth) instead of O(N) tree walk.
        // Parent links are set during parsing (SetParentLinks), so this is always valid.
        var current = target;
        while(current.Parent is not null)
        {
            current = current.Parent;
        }

        return ReferenceEquals(current, root);
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
            if(Directory.Exists(outputDirectory))
            {
                foreach(var file in Directory.GetFiles(outputDirectory))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort cleanup; the compilation already failed.
        }
    }
}