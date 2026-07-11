using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using ProgramModelInstance = Mist.ProgramModel.Model.ProgramModel;
using V2ExtensionMethodInfo = Mist.ProgramModel.Contracts.AmbientExtensionMethodInfo;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// V2 ambient type provider that projects .NET assembly types into the ProgramModel
/// as first-class IMistType instances. CLR types are projected into the same namespace/type
/// graph that Mist source types occupy.
/// </summary>
internal sealed class AmbientTypeProviderV2 : IAmbientTypeProvider, IDisposable
{
    /// <summary>
    /// Alias map: maps CLR (namespace, typeName) to Mist (namespace, typeName).
    /// When the provider is asked to resolve a CLR type with an alias entry, it returns
    /// the existing Standard Library instance rather than projecting a new type.
    /// </summary>
    private static readonly Dictionary<(string Namespace, string TypeName), (string MistNamespace, string MistTypeName)> AliasMap = new()
    {
        // 11 numeric types
        [("System", "Int32")] = ("Standard", "Int32"),
        [("System", "Int64")] = ("Standard", "Int64"),
        [("System", "Int16")] = ("Standard", "Int16"),
        [("System", "Byte")] = ("Standard", "Byte"),
        [("System", "SByte")] = ("Standard", "SByte"),
        [("System", "UInt32")] = ("Standard", "UInt32"),
        [("System", "UInt64")] = ("Standard", "UInt64"),
        [("System", "UInt16")] = ("Standard", "UInt16"),
        [("System", "Single")] = ("Standard", "Single"),
        [("System", "Double")] = ("Standard", "Double"),
        [("System", "Decimal")] = ("Standard", "Decimal"),
        // Primitives
        [("System", "String")] = ("Standard", "String"),
        [("System", "Boolean")] = ("Standard", "Boolean"),
        [("System", "Char")] = ("Standard", "Char"),
        [("System", "Void")] = ("Standard", "Void"),
        // Async
        [("System.Threading.Tasks", "Task")] = ("Standard", "Task"),
        [("System.Threading.Tasks", "ValueTask")] = ("Standard", "ValueTask"),
    };

    /// <summary>
    /// The fully-qualified name of the extension attribute used by the C# compiler
    /// to mark extension methods and their containing static classes.
    /// </summary>
    private const string ExtensionAttributeFullName =
        "System.Runtime.CompilerServices.ExtensionAttribute";

    private readonly ProgramModelInstance programModel;
    private readonly MetadataLoadContext? metadataContext;
    private readonly Dictionary<string, HashSet<string>> namespaceIndex = new();
    private readonly ConcurrentDictionary<(string Namespace, string TypeName), IMistType> projectionCache = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<V2ExtensionMethodInfo>> extensionCache = new();
    private readonly TypeProjectorV2 typeProjector;

    /// <summary>
    /// Creates an AmbientTypeProviderV2 with the given program model and pre-loaded assemblies.
    /// Use the <see cref="Create"/> factory method to load assemblies from paths with error handling.
    /// </summary>
    /// <param name="programModel">The ProgramModel instance to project types into.</param>
    /// <param name="assemblies">Pre-loaded assembly metadata for type resolution.</param>
    /// <param name="metadataContext">
    /// The <see cref="MetadataLoadContext"/> that owns the loaded assemblies.
    /// Pass null when assemblies were loaded externally (e.g., in tests).
    /// Disposed when this provider is disposed.
    /// </param>
    public AmbientTypeProviderV2(
        ProgramModelInstance programModel,
        ImmutableArray<LoadedAssemblyInfo> assemblies,
        MetadataLoadContext? metadataContext)
    {
        this.programModel = programModel;
        LoadedAssemblies = assemblies;
        this.metadataContext = metadataContext;
        typeProjector = new TypeProjectorV2(new VisibilityFilterV2(), programModel.TypeInterner);
        BuildNamespaceIndex(assemblies);
    }

    /// <summary>
    /// Creates an AmbientTypeProviderV2 by loading assemblies from the given paths.
    /// Uses <see cref="MetadataLoadContext"/> for reflection-only loading.
    /// Assemblies that fail to load emit a diagnostic and are skipped.
    /// </summary>
    /// <param name="assemblyPaths">
    /// All assembly DLL paths (SDK + NuGet packages) to make available for type resolution.
    /// </param>
    /// <param name="programModel">The ProgramModel instance to project types into.</param>
    /// <param name="diagnostics">Diagnostic bag for reporting assembly load failures.</param>
    /// <returns>A fully initialized <see cref="AmbientTypeProviderV2"/> instance.</returns>
    public static AmbientTypeProviderV2 Create(
        ImmutableArray<string> assemblyPaths,
        ProgramModelInstance programModel,
        DiagnosticBag diagnostics)
    {
        var resolver = new PathAssemblyResolver(assemblyPaths);
        var context = new MetadataLoadContext(resolver);
        var loaded = LoadAssemblies(assemblyPaths, context, diagnostics);

        return new AmbientTypeProviderV2(programModel, loaded, context);
    }

    /// <summary>
    /// Gets the loaded assemblies available for namespace and type resolution.
    /// </summary>
    public ImmutableArray<LoadedAssemblyInfo> LoadedAssemblies { get; }

    /// <inheritdoc />
    public IMistType? ResolveType(string namespaceName, string typeName)
    {
        // Check alias map first - return existing Standard Library instance
        if(AliasMap.TryGetValue((namespaceName, typeName), out var alias))
        {
            return LookupStandardType(alias.MistNamespace, alias.MistTypeName);
        }

        // Detect Func/Action delegate families before normal projection
        if(TryProjectDelegateFamily(namespaceName, typeName, out var delegateResult))
        {
            return delegateResult;
        }

        // Check projection cache - return cached instance if found
        if(projectionCache.TryGetValue((namespaceName, typeName), out var cached))
        {
            return cached;
        }

        // Check namespace index - if type doesn't exist, return null
        if(!namespaceIndex.TryGetValue(namespaceName, out var typeNames) || !typeNames.Contains(typeName))
        {
            return null;
        }

        // Find the CLR Type from loaded assemblies
        var clrType = FindClrType(namespaceName, typeName);
        if(clrType is null)
        {
            return null;
        }

        // Create type shell via TypeProjectorV2
        var shell = typeProjector.CreateTypeShell(clrType, namespaceName);
        if(shell is null)
        {
            return null;
        }

        // Thread-safe cache insertion using TryAdd pattern
        if(projectionCache.TryAdd((namespaceName, typeName), shell))
        {
            // We won the race: insert into ProgramModel namespace
            var ns = programModel.GetOrAddNamespace(namespaceName);
            ns.Types[shell.Name] = shell;

            // Project type parameters before base types (type params may be referenced)
            if(!TypeProjectorV2.ProjectTypeParameters(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, typeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Resolve base type — if unresolvable, hide the type
            if(!TypeProjectorV2.ResolveBaseType(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, typeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Resolve interfaces — if any unresolvable, hide the type
            if(!TypeProjectorV2.ResolveInterfaces(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, typeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Project methods and constructors onto the type shell
            typeProjector.ProjectMethods(shell, clrType, ResolveType);
            typeProjector.ProjectConstructors(shell, clrType, ResolveType);
            typeProjector.ProjectProperties(shell, clrType, ResolveType);
            typeProjector.ProjectFields(shell, clrType, ResolveType);
            typeProjector.ProjectIndexers(shell, clrType, ResolveType);
            typeProjector.ProjectOperators(shell, clrType, ResolveType);

            // IDisposable handling: set HasDrop and IsDisposableDrop flags
            ApplyDisposableFlags(shell, clrType);

            // Project nested types (recursive)
            ProjectNestedTypes(shell, clrType, namespaceName);

            return shell;
        }

        // We lost the race: discard our instance, return the cached winner
        return projectionCache[(namespaceName, typeName)];
    }

    /// <inheritdoc />
    public IMistType? ResolveType(string namespaceName, string typeName, int typeParameterCount)
    {
        // Arity-aware: aliases have no type parameters, so check alias map without arity
        if(typeParameterCount == 0 && AliasMap.TryGetValue((namespaceName, typeName), out var alias))
        {
            return LookupStandardType(alias.MistNamespace, alias.MistTypeName);
        }

        // Build the CLR name with arity suffix for generic types
        var clrTypeName = typeParameterCount > 0
            ? $"{typeName}`{typeParameterCount}"
            : typeName;

        // Detect Func/Action delegate families before normal projection
        if(TryProjectDelegateFamily(namespaceName, clrTypeName, out var delegateResult))
        {
            return delegateResult;
        }

        // Check projection cache
        if(projectionCache.TryGetValue((namespaceName, clrTypeName), out var cached))
        {
            return cached;
        }

        // Check namespace index
        if(!namespaceIndex.TryGetValue(namespaceName, out var typeNames) || !typeNames.Contains(clrTypeName))
        {
            return null;
        }

        // Find the CLR Type from loaded assemblies
        var clrType = FindClrType(namespaceName, clrTypeName);
        if(clrType is null)
        {
            return null;
        }

        // Create type shell via TypeProjectorV2
        var shell = typeProjector.CreateTypeShell(clrType, namespaceName);
        if(shell is null)
        {
            return null;
        }

        // Thread-safe cache insertion using TryAdd pattern
        if(projectionCache.TryAdd((namespaceName, clrTypeName), shell))
        {
            // We won the race: insert into ProgramModel namespace
            var ns = programModel.GetOrAddNamespace(namespaceName);
            ns.Types[shell.Name] = shell;

            // Project type parameters before base types (type params may be referenced)
            if(!TypeProjectorV2.ProjectTypeParameters(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, clrTypeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Resolve base type — if unresolvable, hide the type
            if(!TypeProjectorV2.ResolveBaseType(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, clrTypeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Resolve interfaces — if any unresolvable, hide the type
            if(!TypeProjectorV2.ResolveInterfaces(shell, clrType, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, clrTypeName), out _);
                _ = ns.Types.Remove(shell.Name);
                return null;
            }

            // Project methods and constructors onto the type shell
            typeProjector.ProjectMethods(shell, clrType, ResolveType);
            typeProjector.ProjectConstructors(shell, clrType, ResolveType);
            typeProjector.ProjectProperties(shell, clrType, ResolveType);
            typeProjector.ProjectFields(shell, clrType, ResolveType);
            typeProjector.ProjectIndexers(shell, clrType, ResolveType);
            typeProjector.ProjectOperators(shell, clrType, ResolveType);

            // IDisposable handling: set HasDrop and IsDisposableDrop flags
            ApplyDisposableFlags(shell, clrType);

            // Project nested types (recursive)
            ProjectNestedTypes(shell, clrType, namespaceName);

            return shell;
        }

        // We lost the race: discard our instance, return the cached winner
        return projectionCache[(namespaceName, clrTypeName)];
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetTypeNamesInNamespace(string namespaceName)
    {
        if(namespaceIndex.TryGetValue(namespaceName, out var typeNames))
        {
            return typeNames.ToList();
        }

        return Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<V2ExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName)
    {
        return extensionCache.GetOrAdd(namespaceName, ScanExtensionsInNamespace);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        metadataContext?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Extension method scanning
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans all loaded assemblies for extension methods in the given namespace.
    /// Identifies extension methods by: public, static, [ExtensionAttribute], at least one parameter.
    /// </summary>
    private IReadOnlyList<V2ExtensionMethodInfo> ScanExtensionsInNamespace(string namespaceName)
    {
        var results = new List<V2ExtensionMethodInfo>();

        foreach(var assembly in LoadedAssemblies)
        {
            if(!assembly.NamespaceIndex.ContainsKey(namespaceName))
            {
                continue;
            }

            ScanAssemblyForExtensions(assembly, namespaceName, results);
        }

        return results;
    }

    /// <summary>
    /// Scans a single assembly for extension methods in the given namespace.
    /// Iterates exported types looking for static classes with [ExtensionAttribute].
    /// </summary>
    private static void ScanAssemblyForExtensions(
        LoadedAssemblyInfo assembly,
        string namespaceName,
        List<V2ExtensionMethodInfo> results)
    {
        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.MetadataAssembly.GetExportedTypes();
        }
        catch
        {
            return;
        }

        foreach(var type in exportedTypes)
        {
            if(type.Namespace != namespaceName)
            {
                continue;
            }

            // Static class in CLR: abstract + sealed
            if(!type.IsClass || !type.IsAbstract || !type.IsSealed)
            {
                continue;
            }

            if(!HasExtensionAttribute(type))
            {
                continue;
            }

            ScanExtensionClass(type, results);
        }
    }

    /// <summary>
    /// Scans a static class decorated with [ExtensionAttribute] for qualifying extension methods.
    /// </summary>
    private static void ScanExtensionClass(Type extensionClass, List<V2ExtensionMethodInfo> results)
    {
        MethodInfo[] methods;
        try
        {
            methods = extensionClass.GetMethods(BindingFlags.Public | BindingFlags.Static);
        }
        catch
        {
            return;
        }

        foreach(var method in methods)
        {
            if(!HasExtensionAttribute(method))
            {
                continue;
            }

            ParameterInfo[] parameters;
            try
            {
                parameters = method.GetParameters();
            }
            catch
            {
                continue;
            }

            if(parameters.Length == 0)
            {
                continue;
            }

            var remainingParams = parameters.Length > 1
                ? (IReadOnlyList<ParameterInfo>)parameters[1..]
                : Array.Empty<ParameterInfo>();

            var isGeneric = method.IsGenericMethodDefinition;
            var typeParams = isGeneric
                ? (IReadOnlyList<Type>)method.GetGenericArguments()
                : Array.Empty<Type>();

            results.Add(new V2ExtensionMethodInfo(
                MethodName: method.Name,
                ReceiverClrType: parameters[0].ParameterType,
                ContainingClassName: extensionClass.FullName ?? extensionClass.Name,
                RemainingParameters: remainingParams,
                ReturnClrType: method.ReturnType,
                IsGenericMethodDefinition: isGeneric,
                GenericTypeParameters: typeParams));
        }
    }

    /// <summary>
    /// Checks whether a type or method has [System.Runtime.CompilerServices.ExtensionAttribute]
    /// using metadata-only inspection.
    /// </summary>
    private static bool HasExtensionAttribute(MemberInfo member)
    {
        try
        {
            var attributes = member.GetCustomAttributesData();
            foreach(var attr in attributes)
            {
                if(attr.AttributeType.FullName == ExtensionAttributeFullName)
                {
                    return true;
                }
            }
        }
        catch
        {
            // If GetCustomAttributesData() throws, treat as not having the attribute.
        }

        return false;
    }

    /// <summary>
    /// Attempts to project a Func/Action/Delegate family type from the System namespace
    /// into the corresponding Standard.Func definition or BaseDelegateType equivalent.
    /// Returns true if the type was recognized as a delegate family member (even if projection
    /// returns null due to missing Standard.Func definition).
    /// </summary>
    /// <param name="namespaceName">The CLR namespace of the type.</param>
    /// <param name="typeName">The CLR type name (e.g., "Func`2", "Action`1", "Action", "Delegate").</param>
    /// <param name="result">The projected Standard.Func definition, or null if not found.</param>
    /// <returns>True if the type was recognized as a delegate family member; false to continue normal projection.</returns>
    private bool TryProjectDelegateFamily(string namespaceName, string typeName, out IMistType? result)
    {
        result = null;

        if(namespaceName != "System")
        {
            return false;
        }

        // System.Delegate and System.MulticastDelegate — let them fall through to normal projection.
        // The ProgramModel does not have a BaseDelegateType equivalent, so these will be
        // projected as regular ClassTypes via the normal path.
        if(typeName is "Delegate" or "MulticastDelegate")
        {
            return false;
        }

        // System.Func`N → Standard.Func definition with arity N
        if(typeName.StartsWith("Func`", StringComparison.Ordinal))
        {
            var arityStr = typeName["Func`".Length..];
            if(int.TryParse(arityStr, out var arity) && arity >= 1)
            {
                result = LookupStandardFuncDefinition(arity);
                return true;
            }

            return false;
        }

        // System.Action (non-generic, arity 0) → ConstructedType(Standard.Func<>, [Standard.Void])
        if(typeName == "Action")
        {
            var funcDef = LookupStandardFuncDefinition(1);
            if(funcDef is null)
            {
                result = null;
                return true;
            }

            var voidType = LookupStandardType("Standard", "Void");
            if(voidType is null)
            {
                result = funcDef;
                return true;
            }

            result = programModel.TypeInterner.GetOrCreateConstructedType(funcDef, [voidType]);
            return true;
        }

        // System.Action`N → Standard.Func definition with arity N+1 (N params + Void return)
        if(typeName.StartsWith("Action`", StringComparison.Ordinal))
        {
            var arityStr = typeName["Action`".Length..];
            if(int.TryParse(arityStr, out var arity) && arity >= 1)
            {
                result = LookupStandardFuncDefinition(arity + 1);
                return true;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Looks up the Standard.Func definition with the specified arity from the ProgramModel.
    /// The Standard Library defines Func&lt;&gt; (arity 1), Func&lt;,&gt; (arity 2), etc.
    /// Returns null if the Standard.Func definition with the requested arity is not loaded.
    /// </summary>
    /// <param name="arity">The number of type parameters (1 = Func&lt;&gt;, 2 = Func&lt;,&gt;, etc.).</param>
    /// <returns>The Standard.Func IMistType definition, or null if not found.</returns>
    private IMistType? LookupStandardFuncDefinition(int arity)
    {
        // Standard.Func names use angle-bracket notation:
        // arity 1 → "Func<>"
        // arity 2 → "Func<,>"
        // arity 3 → "Func<,,>"
        var commas = arity > 1 ? new string(',', arity - 1) : "";
        var funcName = $"Func<{commas}>";
        return LookupStandardType("Standard", funcName);
    }

    /// <summary>
    /// Loads all assemblies from the given paths via <see cref="MetadataLoadContext"/>.
    /// Assemblies that fail to load emit a diagnostic and are skipped.
    /// </summary>
    private static ImmutableArray<LoadedAssemblyInfo> LoadAssemblies(
        ImmutableArray<string> assemblyPaths,
        MetadataLoadContext context,
        DiagnosticBag diagnostics)
    {
        var loaded = ImmutableArray.CreateBuilder<LoadedAssemblyInfo>();

        foreach(var path in assemblyPaths)
        {
            try
            {
                var assembly = context.LoadFromAssemblyPath(path);
                var info = new LoadedAssemblyInfo(path, assembly);
                loaded.Add(info);
            }
            catch(Exception ex)
            {
                diagnostics.Report(
                    BackendDiagnostics.AssemblyLoadFailed,
                    default,
                    "<assembly>",
                    (object?)path,
                    ex.Message);
            }
        }

        return loaded.ToImmutable();
    }

    /// <summary>
    /// Builds the unified namespace index from all loaded assemblies.
    /// Merges each assembly's <see cref="LoadedAssemblyInfo.NamespaceIndex"/>
    /// into the provider-level <see cref="namespaceIndex"/>.
    /// </summary>
    private void BuildNamespaceIndex(ImmutableArray<LoadedAssemblyInfo> loadedAssemblies)
    {
        foreach(var assembly in loadedAssemblies)
        {
            foreach(var (ns, typeNames) in assembly.NamespaceIndex)
            {
                if(!namespaceIndex.TryGetValue(ns, out var set))
                {
                    set = new HashSet<string>();
                    namespaceIndex[ns] = set;
                }

                foreach(var typeName in typeNames)
                {
                    _ = set.Add(typeName);
                }
            }
        }
    }

    /// <summary>
    /// Looks up a type in the ProgramModel by namespace and name.
    /// Used by the alias map to return existing Standard Library instances.
    /// </summary>
    private IMistType? LookupStandardType(string namespaceName, string typeName)
    {
        if(programModel.Namespaces.TryGetValue(namespaceName, out var ns)
            && ns.Types.TryGetValue(typeName, out var type))
        {
            return type;
        }

        return null;
    }

    /// <summary>
    /// Finds the CLR <see cref="Type"/> from loaded assemblies matching the given namespace and type name.
    /// The type name should be the CLR name including arity suffix (e.g., "List`1").
    /// </summary>
    private Type? FindClrType(string namespaceName, string typeName)
    {
        foreach(var assembly in LoadedAssemblies)
        {
            if(assembly.NamespaceIndex.TryGetValue(namespaceName, out var typeNames)
                && typeNames.Contains(typeName))
            {
                var clrType = assembly.MetadataAssembly.GetType($"{namespaceName}.{typeName}");
                if(clrType is not null)
                {
                    return clrType;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Projects public nested types from a CLR type into the ProgramModel.
    /// Nested types are registered with a dot-separated key (e.g., "List&lt;&gt;.Enumerator")
    /// and have their DeclaringType set to the parent shell.
    /// Recursively projects nested types' members and their own nested types.
    /// </summary>
    /// <param name="parentShell">The projected parent type shell.</param>
    /// <param name="clrType">The CLR type whose nested types to project.</param>
    /// <param name="namespaceName">The namespace the parent type belongs to.</param>
    private void ProjectNestedTypes(MistType parentShell, Type clrType, string namespaceName)
    {
        Type[] nestedTypes;
        try
        {
            nestedTypes = clrType.GetNestedTypes(BindingFlags.Public);
        }
        catch
        {
            // If metadata cannot be loaded, skip nested types.
            return;
        }

        foreach(var nested in nestedTypes)
        {
            // Create the shell with the parent as declaring type
            var nestedShell = typeProjector.CreateTypeShell(nested, namespaceName, parentShell);
            if(nestedShell is null)
            {
                continue;
            }

            // Build the dot-separated key for namespace registration
            var key = TypeProjectorV2.BuildNestedTypeKey(parentShell, nestedShell.Name);

            // Thread-safe cache insertion
            if(!projectionCache.TryAdd((namespaceName, key), nestedShell))
            {
                continue; // Already projected (e.g., from another thread)
            }

            // Register in namespace with dot-separated key
            var ns = programModel.GetOrAddNamespace(namespaceName);
            ns.Types[key] = nestedShell;

            // Add to parent's nested types list
            parentShell.NestedTypes.Add(nestedShell);

            // Project type parameters
            if(!TypeProjectorV2.ProjectTypeParameters(nestedShell, nested, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, key), out _);
                _ = ns.Types.Remove(key);
                _ = parentShell.NestedTypes.Remove(nestedShell);
                continue;
            }

            // Resolve base type
            if(!TypeProjectorV2.ResolveBaseType(nestedShell, nested, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, key), out _);
                _ = ns.Types.Remove(key);
                _ = parentShell.NestedTypes.Remove(nestedShell);
                continue;
            }

            // Resolve interfaces
            if(!TypeProjectorV2.ResolveInterfaces(nestedShell, nested, ResolveType))
            {
                _ = projectionCache.TryRemove((namespaceName, key), out _);
                _ = ns.Types.Remove(key);
                _ = parentShell.NestedTypes.Remove(nestedShell);
                continue;
            }

            // Project members
            typeProjector.ProjectMethods(nestedShell, nested, ResolveType);
            typeProjector.ProjectConstructors(nestedShell, nested, ResolveType);
            typeProjector.ProjectProperties(nestedShell, nested, ResolveType);
            typeProjector.ProjectFields(nestedShell, nested, ResolveType);
            typeProjector.ProjectIndexers(nestedShell, nested, ResolveType);
            typeProjector.ProjectOperators(nestedShell, nested, ResolveType);

            // IDisposable handling
            ApplyDisposableFlags(nestedShell, nested);

            // Recursively project nested types of the nested type
            ProjectNestedTypes(nestedShell, nested, namespaceName);
        }
    }

    /// <summary>
    /// Checks whether the CLR type implements IDisposable and sets HasDrop/IsDisposableDrop
    /// flags on the projected concrete type accordingly.
    /// </summary>
    /// <param name="shell">The projected MistType shell.</param>
    /// <param name="clrType">The CLR type to check for IDisposable implementation.</param>
    private static void ApplyDisposableFlags(MistType shell, Type clrType)
    {
        if(shell is not ConcreteType concreteShell)
        {
            return;
        }

        // Check if the type implements System.IDisposable
        var implementsDisposable = false;
        try
        {
            var interfaces = clrType.GetInterfaces();
            foreach(var iface in interfaces)
            {
                if(iface.Namespace == "System" && iface.Name == "IDisposable")
                {
                    implementsDisposable = true;
                    break;
                }
            }
        }
        catch
        {
            // If metadata cannot be loaded, skip.
            return;
        }

        if(!implementsDisposable)
        {
            return;
        }

        concreteShell.HasDrop = true;

        // Check if the type has an explicit Drop() method.
        // If it does, IsDisposableDrop = false (emitter emits .Drop()).
        // If not, IsDisposableDrop = true (emitter emits .Dispose()).
        var hasExplicitDrop = concreteShell.MethodsList.Any(m => m.Name == "Drop" && m.ParametersList.Count == 0);
        concreteShell.IsDisposableDrop = !hasExplicitDrop;
    }
}