using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Mist.Driver;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelType = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Projects .NET assembly types into the ProgramModel as first-class IMistType instances.
/// CLR types are projected into the same namespace/type graph that Mist source types occupy.
/// </summary>
internal sealed class AmbientTypeProviderV2 : IAmbientTypeProvider, IDisposable
{
    /// <summary>
    /// Well-known type map: CLR (namespace, typeName) -> Mist (namespace, typeName).
    /// Keys use CLR names (with backtick arity suffix for generics).
    /// Returns the existing Standard Library instance rather than projecting.
    /// </summary>
    private static readonly Dictionary<(string Namespace, string TypeName), (string MistNamespace, string MistTypeName)> WellKnownTypeMap = new()
    {
        // Numeric types
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
        [("System.Threading.Tasks", "Task`1")] = ("Standard", "Task<>"),
        [("System.Threading.Tasks", "ValueTask")] = ("Standard", "ValueTask"),
        [("System.Threading.Tasks", "ValueTask`1")] = ("Standard", "ValueTask<>"),
        // Func family (arity 1-17)
        [("System", "Func`1")] = ("Standard", "Func<>"),
        [("System", "Func`2")] = ("Standard", "Func<,>"),
        [("System", "Func`3")] = ("Standard", "Func<,,>"),
        [("System", "Func`4")] = ("Standard", "Func<,,,>"),
        [("System", "Func`5")] = ("Standard", "Func<,,,,>"),
        [("System", "Func`6")] = ("Standard", "Func<,,,,,>"),
        [("System", "Func`7")] = ("Standard", "Func<,,,,,,>"),
        [("System", "Func`8")] = ("Standard", "Func<,,,,,,,>"),
        [("System", "Func`9")] = ("Standard", "Func<,,,,,,,,>"),
        [("System", "Func`10")] = ("Standard", "Func<,,,,,,,,,>"),
        [("System", "Func`11")] = ("Standard", "Func<,,,,,,,,,,>"),
        [("System", "Func`12")] = ("Standard", "Func<,,,,,,,,,,,>"),
        [("System", "Func`13")] = ("Standard", "Func<,,,,,,,,,,,,>"),
        [("System", "Func`14")] = ("Standard", "Func<,,,,,,,,,,,,,>"),
        [("System", "Func`15")] = ("Standard", "Func<,,,,,,,,,,,,,,>"),
        [("System", "Func`16")] = ("Standard", "Func<,,,,,,,,,,,,,,,>"),
        [("System", "Func`17")] = ("Standard", "Func<,,,,,,,,,,,,,,,,>"),
        // Action family (arity 0-16)
        [("System", "Action")] = ("Standard", "Action"),
        [("System", "Action`1")] = ("Standard", "Action<>"),
        [("System", "Action`2")] = ("Standard", "Action<,>"),
        [("System", "Action`3")] = ("Standard", "Action<,,>"),
        [("System", "Action`4")] = ("Standard", "Action<,,,>"),
        [("System", "Action`5")] = ("Standard", "Action<,,,,>"),
        [("System", "Action`6")] = ("Standard", "Action<,,,,,>"),
        [("System", "Action`7")] = ("Standard", "Action<,,,,,,>"),
        [("System", "Action`8")] = ("Standard", "Action<,,,,,,,>"),
        [("System", "Action`9")] = ("Standard", "Action<,,,,,,,,>"),
        [("System", "Action`10")] = ("Standard", "Action<,,,,,,,,,>"),
        [("System", "Action`11")] = ("Standard", "Action<,,,,,,,,,,>"),
        [("System", "Action`12")] = ("Standard", "Action<,,,,,,,,,,,>"),
        [("System", "Action`13")] = ("Standard", "Action<,,,,,,,,,,,,>"),
        [("System", "Action`14")] = ("Standard", "Action<,,,,,,,,,,,,,>"),
        [("System", "Action`15")] = ("Standard", "Action<,,,,,,,,,,,,,,>"),
        [("System", "Action`16")] = ("Standard", "Action<,,,,,,,,,,,,,,,>"),
    };

    private readonly MetadataLoadContext? metadataContext;
    private readonly Dictionary<string, HashSet<string>> namespaceIndex = new();
    private readonly ConcurrentDictionary<(string Namespace, string TypeName), IMistType> projectionCache = new();
    private static readonly Scope EmptyScope = new(null);

    /// <summary>
    /// Creates an AmbientTypeProviderV2 with pre-loaded assemblies.
    /// </summary>
    public AmbientTypeProviderV2(
        ImmutableArray<LoadedAssemblyInfo> assemblies,
        TypeInterner typeInterner,
        MetadataLoadContext? metadataContext)
    {
        LoadedAssemblies = assemblies;
        this.metadataContext = metadataContext;
        BuildNamespaceIndex(assemblies);
    }

    /// <summary>
    /// Gets the loaded assemblies available for type resolution.
    /// </summary>
    public ImmutableArray<LoadedAssemblyInfo> LoadedAssemblies { get; }

    /// <inheritdoc />
    public IMistType? ResolveType(string namespaceName, string typeName, ProgramModelType programModel)
    {
        // 1. Translate Mist arity notation to CLR notation
        var clrTypeName = MistNameToClrName(typeName);

        // 2. Check well-known type map — return existing Standard Library instance
        if(WellKnownTypeMap.TryGetValue((namespaceName, clrTypeName), out var wellKnown))
        {
            return LookupWellKnownType(wellKnown.MistNamespace, wellKnown.MistTypeName, programModel);
        }

        // 3. Check projection cache
        if(projectionCache.TryGetValue((namespaceName, clrTypeName), out var cached))
        {
            return cached;
        }

        // 4. Find the CLR Type from loaded assemblies (returns null if not in namespace index)
        var clrType = FindClrType(namespaceName, clrTypeName);
        if(clrType is null)
        {
            return null;
        }

        // 5. Create type shell
        var shell = CreateTypeShell(clrType, namespaceName);
        if(shell is null)
        {
            return null;
        }

        // 6. Thread-safe cache insertion (recursion safety)
        if(projectionCache.TryAdd((namespaceName, clrTypeName), shell))
        {
            // Won the race — register in ProgramModel namespace
            var ns = programModel.GetOrAddNamespace(namespaceName);
            ns.Types[shell.Name] = shell;
            return shell;
        }

        // Lost the race — return the winner's instance
        return projectionCache[(namespaceName, clrTypeName)];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        metadataContext?.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name Format Translation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converts Mist arity notation to CLR backtick notation.
    /// "List&lt;&gt;" -> "List`1", "Dict&lt;,&gt;" -> "Dict`2".
    /// Non-generic names pass through unchanged.
    /// </summary>
    private static string MistNameToClrName(string mistName)
    {
        var angleBracketStart = mistName.IndexOf('<');
        if(angleBracketStart < 0)
        {
            return mistName;
        }

        var baseName = mistName[..angleBracketStart];
        var commas = mistName.Count(c => c == ',');
        var arity = commas + 1;
        return $"{baseName}`{arity}";
    }

    /// <summary>
    /// Converts CLR backtick notation to Mist arity notation for registration.
    /// "List`1" -> "List&lt;&gt;", "Dictionary`2" -> "Dictionary&lt;,&gt;".
    /// Non-generic names pass through unchanged.
    /// </summary>
    private static string ClrNameToMistName(string clrName)
    {
        var backtickIndex = clrName.IndexOf('`');
        if(backtickIndex < 0)
        {
            return clrName;
        }

        var baseName = clrName[..backtickIndex];
        var arityStr = clrName[(backtickIndex + 1)..];
        if(!int.TryParse(arityStr, out var arity) || arity < 1)
        {
            return clrName;
        }

        var commas = new string(',', arity - 1);
        return $"{baseName}<{commas}>";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Shell Creation
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates an empty type shell for the given CLR type.
    /// Returns null if the type kind is not supported.
    /// </summary>
    private static MistType? CreateTypeShell(Type clrType, string namespaceName)
    {
        var mistName = ClrNameToMistName(clrType.Name);

        MistType shell;
        if(clrType.IsInterface)
        {
            shell = new AbstractType(mistName, namespaceName, EmptyScope, null, null);
        }
        else if(clrType.IsEnum)
        {
            shell = new EnumType(mistName, namespaceName, EmptyScope, null, null);
        }
        else if(clrType.IsValueType)
        {
            shell = new StructType(mistName, namespaceName, EmptyScope, null, null);
        }
        else
        {
            shell = new ClassType(mistName, namespaceName, EmptyScope, null, null!);
        }

        shell.Origin = TypeOrigin.Ambient;
        return shell;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Internal Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static IMistType LookupWellKnownType(string namespaceName, string typeName, ProgramModelType programModel)
    {
        if(programModel.Namespaces.TryGetValue(namespaceName, out var ns)
            && ns.Types.TryGetValue(typeName, out var type))
        {
            return type;
        }

        throw new InvalidOperationException(
            $"ICE: Well-known type '{namespaceName}.{typeName}' not found in ProgramModel. " +
            "The Standard Library must be loaded before ambient type resolution.");
    }

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
}
