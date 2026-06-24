using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Implements <see cref="IAmbientTypeResolver"/> by reading .NET assembly metadata
/// via <see cref="MetadataLoadContext"/>. Types are projected lazily on first access
/// and cached for reference equality on subsequent resolutions.
///
/// <para>
/// Initialization:
/// <list type="number">
///   <item>Receives assembly paths from <c>SdkDiscovery</c> + <c>PackageResolver</c>.</item>
///   <item>Creates <see cref="MetadataLoadContext"/> with all assembly paths as the resolver.</item>
///   <item>Builds namespace index by scanning all assemblies' <c>ExportedTypes</c> (names only).</item>
///   <item>Types are projected lazily on first <see cref="ResolveType"/> call.</item>
/// </list>
/// </para>
///
/// <para>Requirements: 3.1, 3.2, 3.3, 3.4, 3.5.</para>
/// </summary>
internal sealed class AssemblyTypeProvider : IAmbientTypeResolver, IDisposable
{
    private readonly MetadataLoadContext _metadataContext;
    private readonly IntrinsicTypeMap _intrinsicMap;
    private readonly VisibilityFilter _filter;
    private readonly TypeProjector _projector;
    private readonly AmbientExtensionScanner _extensionScanner;

    /// <summary>
    /// Cache: (namespace, typeName) → (IType, ImmutableArray&lt;ISymbol&gt;).
    /// Ensures reference equality for repeated resolutions of the same type (Requirement 3.4).
    /// Thread-safe: this instance is shared across parallel compilations via the
    /// backend's cached provider singleton.
    /// </summary>
    private readonly ConcurrentDictionary<(string Namespace, string TypeName), (IType Type, ImmutableArray<ISymbol> Members)> _cache = new();

    /// <summary>
    /// Namespace index: namespace → set of type names in that namespace.
    /// Built during initialization by scanning all assemblies' ExportedTypes (names only).
    /// </summary>
    private readonly Dictionary<string, HashSet<string>> _namespaceIndex = new();

    /// <summary>
    /// Loaded assembly metadata for type resolution.
    /// </summary>
    private readonly ImmutableArray<LoadedAssemblyInfo> _loadedAssemblies;

    /// <summary>
    /// The stdlib type provider used to look up Mist types for member signatures.
    /// Starts as <see cref="FallbackStdlibTypeProvider"/> and is upgraded by
    /// <see cref="UpdateStdlibProvider"/> after Pass 1 registers all stdlib types.
    /// Thread-local: each compilation thread gets its own provider referencing
    /// its own global scope, preventing cross-thread scope iteration races.
    /// </summary>
    [ThreadStatic]
    private static IStdlibTypeProvider? _stdlibThreadLocal;

    /// <summary>
    /// Fallback provider used when no thread-local provider has been set.
    /// </summary>
    private readonly IStdlibTypeProvider _stdlibFallback = FallbackStdlibTypeProvider.Instance;

    private IStdlibTypeProvider Stdlib => _stdlibThreadLocal ?? _stdlibFallback;

    /// <summary>
    /// Creates a new <see cref="AssemblyTypeProvider"/> from the given assembly paths.
    /// Loads all assemblies via <see cref="MetadataLoadContext"/> and builds the namespace index.
    /// </summary>
    /// <param name="assemblyPaths">
    /// All assembly DLL paths (SDK + NuGet packages) to make available for type resolution.
    /// </param>
    /// <param name="diagnostics">Diagnostic bag for reporting assembly load failures.</param>
    public AssemblyTypeProvider(
        ImmutableArray<string> assemblyPaths,
        DiagnosticBag diagnostics)
    {
        _intrinsicMap = new IntrinsicTypeMap();
        _filter = new VisibilityFilter();

        // Inject a type-reference resolver so the projector can connect base types and
        // implemented interfaces to the cached, fully-projected instances this provider
        // returns from ResolveType — the foundation for cross-assembly inheritance
        // (Requirements 6.1, 6.2, 6.3). The delegate captures `this`; it is only invoked
        // later during projection, after the constructor has finished wiring the cache and
        // loaded assemblies below.
        _projector = new TypeProjector(_intrinsicMap, _filter, ResolveProjectedType, ResolveProjectedTypeCacheOnly);

        // Create MetadataLoadContext with a PathAssemblyResolver for all assembly paths.
        var resolver = new PathAssemblyResolver(assemblyPaths);
        _metadataContext = new MetadataLoadContext(resolver);

        // Load assemblies and build namespace index.
        _loadedAssemblies = LoadAssemblies(assemblyPaths, diagnostics);
        BuildNamespaceIndex();

        // Create the extension scanner for lazy discovery of ambient extension methods.
        _extensionScanner = new AmbientExtensionScanner(_loadedAssemblies);
    }

    /// <summary>
    /// Upgrades the stdlib type provider used by lazy type projection.
    /// Called by <see cref="Mist.Semantics.Model.SemanticModelBuilder"/>
    /// after Pass 1 registers all stdlib types so that projected member
    /// signatures reference the real stdlib <see cref="IType"/> instances.
    /// Sets the thread-local provider so parallel compilations don't
    /// interfere with each other's scope iteration.
    /// </summary>
    internal static void UpdateStdlibProvider(IStdlibTypeProvider provider)
    {
        _stdlibThreadLocal = provider;
    }

    /// <summary>
    /// Clears the thread-local stdlib provider after a compilation completes.
    /// Prevents stale references to compilation-specific scopes from leaking
    /// across subsequent compilations on the same thread.
    /// </summary>
    internal static void ClearStdlibProvider()
    {
        _stdlibThreadLocal = null;
    }

    /// <summary>
    /// Exposes the type projector for ambient extension registration.
    /// </summary>
    internal TypeProjector Projector => _projector;

    /// <summary>
    /// Surfaces the inheritance relationships that were omitted during projection because the
    /// referenced CLR base type or interface could not be resolved from any loaded assembly
    /// (Requirement 6.6). Each entry identifies the omitted relationship (base type vs
    /// interface), the derived type, and the unresolved type's name. Accumulates lazily as
    /// types are projected through <see cref="ResolveType(string, string, int)"/>.
    /// </summary>
    internal IReadOnlyCollection<UnresolvedRelationship> UnresolvedRelationships
        => _projector.UnresolvedRelationships;

    /// <summary>
    /// Exposes the current stdlib type provider for ambient extension registration.
    /// </summary>
    internal IStdlibTypeProvider CurrentStdlib => Stdlib;

    /// <inheritdoc />
    public bool HasNamespace(string namespaceName)
    {
        return _namespaceIndex.ContainsKey(namespaceName);
    }

    /// <summary>
    /// Returns true if any namespace in the index starts with the given prefix.
    /// Used by the semantic model to detect namespace root identifiers in expression
    /// context (e.g., "Newtonsoft" when "Newtonsoft.Json" is a known namespace).
    /// </summary>
    internal bool HasNamespaceWithPrefix(string prefix)
    {
        foreach (var ns in _namespaceIndex.Keys)
        {
            if (ns.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <inheritdoc />
    public IType? ResolveType(string namespaceName, string typeName)
    {
        return ResolveTypeCore(namespaceName, typeName, typeArgumentCount: -1);
    }

    /// <inheritdoc />
    public IType? ResolveType(string namespaceName, string typeName, int typeArgumentCount)
    {
        return ResolveTypeCore(namespaceName, typeName, typeArgumentCount);
    }

    /// <summary>
    /// Core resolution logic shared by both <see cref="ResolveType(string, string)"/> and
    /// <see cref="ResolveType(string, string, int)"/>. When <paramref name="typeArgumentCount"/>
    /// is &gt;= 0, the CLR type with exactly that many generic parameters is selected
    /// (e.g., <c>Func`2</c> for arity 2). When -1, falls back to first-match behavior.
    /// </summary>
    private IType? ResolveTypeCore(string namespaceName, string typeName, int typeArgumentCount)
    {
        // Build the cache key: include arity to distinguish Foo from Foo`1 from Foo`2.
        var cacheKey = typeArgumentCount >= 0
            ? (namespaceName, $"{typeName}`{typeArgumentCount}")
            : (namespaceName, typeName);

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached.Type;

        // Find the CLR type in loaded assemblies.
        var clrType = typeArgumentCount >= 0
            ? FindClrTypeByArity(namespaceName, typeName, typeArgumentCount)
            : FindClrType(namespaceName, typeName);
        if (clrType is null)
            return null;

        // Skip non-public types: VisibilityFilter.IsTypeExcluded excludes types that are not
        // publicly accessible, so they resolve to null rather than a projected type. Ref-struct
        // types (Span<T>/ReadOnlySpan<T> and any IsByRefLike type) are NOT excluded here — they
        // are projected as Mist stack-only structs (Requirement 9), so this guard does not null
        // them out.
        if (_filter.IsTypeExcluded(clrType))
            return null;

        // Project the type lazily on first access (Requirement 3.3).
        var stdlib = Stdlib;
        var (projectedType, members) = _projector.ProjectType(clrType, stdlib);

        // Insert the in-progress type into the cache BEFORE projecting members so that a
        // base-type or interface reference that resolves back to a type already on the
        // projection stack returns this same instance instead of recursing forever
        // (cache-mediated recursion safety — Requirements 6.1, 6.3). If another thread won
        // the race to project this key, discard our work and return the cached instance so
        // reference equality is preserved (Requirement 3.4).
        if (!_cache.TryAdd(cacheKey, (projectedType, ImmutableArray<ISymbol>.Empty)))
            return _cache[cacheKey].Type;

        // Project members (methods, properties, fields, constructors, indexers).
        var projectedMembers = _projector.ProjectMembers(clrType, projectedType, stdlib);

        // Project operators and add them to the member list.
        var memberScope = GetMemberScope(projectedType);
        if (memberScope is not null)
        {
            var operators = _projector.ProjectOperators(clrType, memberScope, stdlib);
            if (operators.Length > 0)
            {
                projectedMembers = projectedMembers.AddRange(operators);
            }

            // Synthesize duck-typed IEnumerable<T> for types that have
            // GetEnumerator()/MoveNext()/Current but don't implement IEnumerable<T>.
            // Called here (after UpdateStdlibProvider) to ensure the stdlib is available.
            _projector.SynthesizeDuckTypedIEnumerable(clrType, projectedType, stdlib);

            // Synthesize IEnumerable interop slots.
            _projector.SynthesizeEnumeratorSlots(clrType, projectedType, stdlib);
        }

        // Combine initial members from ProjectType (e.g., enum fields) with projected members.
        var allMembers = members.Length > 0
            ? members.AddRange(projectedMembers)
            : projectedMembers;

        // Finalize the cache entry, upgrading the early-inserted (members-empty) entry to the
        // fully-projected member set. The projected IType instance is unchanged, so any base/
        // interface reference captured during the in-progress window above stays reference-equal
        // to this canonical instance (Requirement 3.4). Indexer assignment (not TryAdd) is
        // required because the early insert above already added the key.
        _cache[cacheKey] = (projectedType, allMembers);

        return projectedType;
    }

    /// <summary>
    /// Type-reference resolver injected into the <see cref="TypeProjector"/>. Maps a CLR
    /// <see cref="Type"/> to its cached, fully-projected Mist <see cref="IType"/> via the same
    /// <c>(namespace, name, arity)</c> cache path that <see cref="ResolveType(string, string, int)"/>
    /// uses, so base types and implemented interfaces connect to the cached instances rather
    /// than disconnected shells (Requirements 6.1, 6.2, 6.3).
    /// <para>
    /// Returns <c>null</c> when the type cannot be resolved from any loaded assembly (e.g. a
    /// base type defined in an assembly that was not supplied), letting the projector fall back
    /// to shell-building so projection of the derived type and its remaining relationships
    /// continues. Generic parameters and arrays are never inheritance references and are not
    /// routed here.
    /// </para>
    /// </summary>
    private IType? ResolveProjectedType(Type clrType)
    {
        if (clrType.Namespace is null || clrType.IsGenericParameter || clrType.IsArray)
            return null;

        var typeName = TypeProjector.StripGenericAritySuffix(clrType.Name);
        var arity = clrType.IsGenericType ? clrType.GetGenericArguments().Length : 0;
        return ResolveTypeCore(clrType.Namespace, typeName, arity);
    }

    /// <summary>
    /// Cache-only variant of <see cref="ResolveProjectedType"/>: returns the projected type
    /// ONLY if it has already been projected and exists in the cache. Does NOT trigger
    /// projection of new types, so it is safe to call during member type projection without
    /// risking infinite recursion.
    /// </summary>
    private IType? ResolveProjectedTypeCacheOnly(Type clrType)
    {
        if (clrType.Namespace is null || clrType.IsGenericParameter || clrType.IsArray)
            return null;

        var typeName = TypeProjector.StripGenericAritySuffix(clrType.Name);
        var arity = clrType.IsGenericType ? clrType.GetGenericArguments().Length : 0;

        var cacheKey = arity >= 0
            ? (clrType.Namespace, $"{typeName}`{arity}")
            : (clrType.Namespace, typeName);

        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached.Type;

        // Also try without arity suffix for non-generic types (arity == 0).
        if (arity == 0)
        {
            var altKey = (clrType.Namespace, typeName);
            if (_cache.TryGetValue(altKey, out var altCached))
                return altCached.Type;
        }

        return null;
    }

    /// <inheritdoc />
    public ImmutableArray<ISymbol> GetMembers(IType type)
    {
        // ConcurrentDictionary enumeration is safe against concurrent writes —
        // it takes a snapshot of the internal buckets without locking.
        foreach (var entry in _cache.Values)
        {
            if (ReferenceEquals(entry.Type, type))
                return entry.Members;
        }

        return ImmutableArray<ISymbol>.Empty;
    }

    /// <inheritdoc />
    public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName)
    {
        if (!_namespaceIndex.TryGetValue(namespaceName, out var typeNames))
            return ImmutableArray<string>.Empty;

        return typeNames.ToImmutableArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName)
    {
        return _extensionScanner.GetExtensionsForNamespace(namespaceName);
    }

    /// <summary>
    /// Disposes the <see cref="MetadataLoadContext"/> and releases all loaded assembly resources.
    /// </summary>
    public void Dispose()
    {
        _metadataContext.Dispose();
    }

    /// <summary>
    /// Locates the CLR <see cref="Type"/> for an already-resolved type by namespace and name.
    /// Used by the <c>mist inspect</c> command to enumerate CLR members for classification.
    /// This method is internal because the inspect command lives in the same assembly.
    /// </summary>
    internal Type? FindClrTypeForInspection(string namespaceName, string typeName)
    {
        return FindClrType(namespaceName, typeName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Loads all assemblies from the given paths via <see cref="MetadataLoadContext"/>.
    /// Assemblies that fail to load emit a diagnostic and are skipped (Requirement 3.5).
    /// </summary>
    private ImmutableArray<LoadedAssemblyInfo> LoadAssemblies(
        ImmutableArray<string> assemblyPaths,
        DiagnosticBag diagnostics)
    {
        var loaded = ImmutableArray.CreateBuilder<LoadedAssemblyInfo>();

        foreach (var path in assemblyPaths)
        {
            try
            {
                var assembly = _metadataContext.LoadFromAssemblyPath(path);
                var info = new LoadedAssemblyInfo(path, assembly);
                loaded.Add(info);
            }
            catch (Exception ex)
            {
                diagnostics.Add(new Diagnostic(
                    new DiagnosticId("MIST_ASM_LOAD_FAILED"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Failed to load assembly '{path}': {ex.Message}",
                    Location: default,
                    CompilationUnitPath: "<assembly>",
                    AnalyzerName: null));
            }
        }

        return loaded.ToImmutable();
    }

    /// <summary>
    /// Builds the unified namespace index from all loaded assemblies.
    /// Merges each assembly's <see cref="LoadedAssemblyInfo.NamespaceIndex"/>
    /// into the provider-level <see cref="_namespaceIndex"/>.
    /// </summary>
    private void BuildNamespaceIndex()
    {
        foreach (var assemblyInfo in _loadedAssemblies)
        {
            foreach (var (ns, typeNames) in assemblyInfo.NamespaceIndex)
            {
                if (!_namespaceIndex.TryGetValue(ns, out var existingNames))
                {
                    existingNames = new HashSet<string>();
                    _namespaceIndex[ns] = existingNames;
                }

                foreach (var typeName in typeNames)
                {
                    existingNames.Add(typeName);
                }
            }
        }
    }

    /// <summary>
    /// Finds a CLR <see cref="Type"/> by namespace and type name across all loaded assemblies.
    /// Searches by matching the type's namespace and name (including generic arity suffix).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private Type? FindClrType(string namespaceName, string typeName)
    {
        // The typeName from the caller may not include the generic arity suffix.
        // We need to search for both the exact name and names with arity suffixes.
        foreach (var assemblyInfo in _loadedAssemblies)
        {
            if (!assemblyInfo.NamespaceIndex.TryGetValue(namespaceName, out var typeNames))
                continue;

            // Try exact match first (non-generic types or types with arity suffix already).
            if (typeNames.Contains(typeName))
            {
                var fullName = $"{namespaceName}.{typeName}";
                try
                {
                    var type = assemblyInfo.MetadataAssembly.GetType(fullName);
                    if (type is not null && !_filter.IsTypeExcluded(type))
                        return type;
                }
                catch
                {
                    // Skip types that fail to load.
                }
            }

            // Try matching with generic arity suffixes (e.g., "List" matches "List`1").
            foreach (var candidateName in typeNames)
            {
                var strippedName = TypeProjector.StripGenericAritySuffix(candidateName);
                if (strippedName == typeName && candidateName != typeName)
                {
                    var fullName = $"{namespaceName}.{candidateName}";
                    try
                    {
                        var type = assemblyInfo.MetadataAssembly.GetType(fullName);
                        if (type is not null && !_filter.IsTypeExcluded(type))
                            return type;
                    }
                    catch
                    {
                        // Skip types that fail to load.
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a CLR <see cref="Type"/> by namespace, type name, and exact generic arity.
    /// For arity 0, matches only non-generic types (no backtick suffix).
    /// For arity N &gt; 0, matches only types with exactly <c>`N</c> suffix.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private Type? FindClrTypeByArity(string namespaceName, string typeName, int typeArgumentCount)
    {
        foreach (var assemblyInfo in _loadedAssemblies)
        {
            if (!assemblyInfo.NamespaceIndex.TryGetValue(namespaceName, out var typeNames))
                continue;

            if (typeArgumentCount == 0)
            {
                // Non-generic: match the exact name without any arity suffix.
                if (typeNames.Contains(typeName))
                {
                    var fullName = $"{namespaceName}.{typeName}";
                    try
                    {
                        var type = assemblyInfo.MetadataAssembly.GetType(fullName);
                        if (type is not null && !_filter.IsTypeExcluded(type) && !type.IsGenericTypeDefinition)
                            return type;
                    }
                    catch
                    {
                        // Skip types that fail to load.
                    }
                }
            }
            else
            {
                // Generic: match "TypeName`N" exactly.
                var arityName = $"{typeName}`{typeArgumentCount}";
                if (typeNames.Contains(arityName))
                {
                    var fullName = $"{namespaceName}.{arityName}";
                    try
                    {
                        var type = assemblyInfo.MetadataAssembly.GetType(fullName);
                        if (type is not null && !_filter.IsTypeExcluded(type))
                            return type;
                    }
                    catch
                    {
                        // Skip types that fail to load.
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the <see cref="Scope"/> from a projected Mist type.
    /// Returns <c>null</c> if the type does not have a member scope.
    /// </summary>
    private static Scope? GetMemberScope(IType type)
    {
        return type switch
        {
            Semantics.Model.Types.ClassType ct => ct.MemberScope as Scope,
            Semantics.Model.Types.StructType st => st.MemberScope as Scope,
            Semantics.Model.Types.AbstractType it => it.MemberScope as Scope,
            Semantics.Model.Types.EnumType et => et.MemberScope as Scope,
            Semantics.Model.Types.DelegateType dt => dt.MemberScope as Scope,
            Semantics.Model.Types.BaseDelegateType bdt => bdt.MemberScope as Scope,
            _ => null,
        };
    }
}
