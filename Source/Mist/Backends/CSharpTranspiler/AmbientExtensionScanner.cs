using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.Driver;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Scans .NET assemblies for C# extension methods (static methods decorated with
/// [System.Runtime.CompilerServices.ExtensionAttribute] in static classes also
/// decorated with the same attribute). Results are cached per namespace.
/// </summary>
internal sealed class AmbientExtensionScanner
{
    /// <summary>
    /// The fully-qualified name of the extension attribute used by the C# compiler
    /// to mark extension methods and their containing static classes.
    /// </summary>
    private const string ExtensionAttributeFullName =
        "System.Runtime.CompilerServices.ExtensionAttribute";

    private readonly IReadOnlyList<LoadedAssemblyInfo> _assemblies;
    private readonly ConcurrentDictionary<string, IReadOnlyList<AmbientExtensionMethodInfo>> _cache = new();

    /// <summary>
    /// Creates a new <see cref="AmbientExtensionScanner"/> that will scan the given
    /// loaded assemblies for extension methods.
    /// </summary>
    /// <param name="assemblies">
    /// The loaded assemblies to scan (from <see cref="AssemblyTypeProvider"/>).
    /// </param>
    public AmbientExtensionScanner(IReadOnlyList<LoadedAssemblyInfo> assemblies)
    {
        _assemblies = assemblies;
    }

    /// <summary>
    /// Returns all extension methods declared in the given namespace across all
    /// loaded assemblies. Results are cached for subsequent calls.
    /// </summary>
    /// <param name="namespaceName">The CLR namespace to scan for extension methods.</param>
    /// <returns>
    /// A list of <see cref="AmbientExtensionMethodInfo"/> for all extension methods
    /// in the namespace. Returns an empty list if no extension methods are found.
    /// </returns>
    public IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionsForNamespace(string namespaceName)
    {
        // GetOrAdd is atomic — only one thread will scan a given namespace.
        return _cache.GetOrAdd(namespaceName, ScanNamespace);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Scans all loaded assemblies for extension methods in the given namespace.
    /// Only examines exported types in the target namespace (Requirement 11.3).
    /// </summary>
    private IReadOnlyList<AmbientExtensionMethodInfo> ScanNamespace(string namespaceName)
    {
        var results = new List<AmbientExtensionMethodInfo>();

        foreach(var assembly in _assemblies)
        {
            // Only scan assemblies that contain types in the target namespace.
            if(!assembly.NamespaceIndex.ContainsKey(namespaceName))
            {
                continue;
            }

            ScanAssemblyNamespace(assembly, namespaceName, results);
        }

        return results;
    }

    /// <summary>
    /// Scans a single assembly for extension methods in the given namespace.
    /// Iterates exported types in the target namespace, checking for static classes
    /// with [ExtensionAttribute] and their qualifying methods.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static void ScanAssemblyNamespace(
        LoadedAssemblyInfo assembly,
        string namespaceName,
        List<AmbientExtensionMethodInfo> results)
    {
        Type[] exportedTypes;
        try
        {
            exportedTypes = assembly.MetadataAssembly.GetExportedTypes();
        }
        catch
        {
            // If we cannot enumerate exported types, skip this assembly.
            return;
        }

        foreach(var type in exportedTypes)
        {
            // Only examine types in the target namespace (Requirement 11.3).
            if(type.Namespace != namespaceName)
            {
                continue;
            }

            // Must be a static class: abstract + sealed in CLR terms.
            if(!type.IsAbstract || !type.IsSealed)
            {
                continue;
            }

            // Must have [ExtensionAttribute] on the class.
            if(!HasExtensionAttribute(type))
            {
                continue;
            }

            // Scan the class's public static methods for extension methods.
            ScanExtensionClass(type, namespaceName, results);
        }
    }

    /// <summary>
    /// Scans a static class decorated with [ExtensionAttribute] for qualifying
    /// extension methods (public static, has [ExtensionAttribute], at least 1 parameter).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static void ScanExtensionClass(
        Type extensionClass,
        string namespaceName,
        List<AmbientExtensionMethodInfo> results)
    {
        MethodInfo[] methods;
        try
        {
            methods = extensionClass.GetMethods(BindingFlags.Public | BindingFlags.Static);
        }
        catch
        {
            // If we cannot enumerate methods, skip this class.
            return;
        }

        foreach(var method in methods)
        {
            // Must have [ExtensionAttribute] on the method itself.
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
                // If we cannot read parameters, skip this method.
                continue;
            }

            // Must have at least 1 parameter (the receiver).
            if(parameters.Length == 0)
            {
                continue;
            }

            var receiverParam = parameters[0];
            var remainingParams = parameters.Length > 1
                ? parameters[1..].ToList()
                : new List<ParameterInfo>();

            var isGenericMethod = method.IsGenericMethodDefinition;
            var methodTypeParams = isGenericMethod
                ? method.GetGenericArguments().ToList()
                : new List<Type>();

            results.Add(new AmbientExtensionMethodInfo
            {
                Namespace = namespaceName,
                ContainingClassName = extensionClass.FullName ?? extensionClass.Name,
                MethodName = method.Name,
                ReceiverClrType = receiverParam.ParameterType,
                RemainingParameters = remainingParams,
                ReturnClrType = method.ReturnType,
                IsGenericMethod = isGenericMethod,
                MethodTypeParameters = methodTypeParams,
                MethodInfo = method,
            });
        }
    }

    /// <summary>
    /// Checks whether a type or method has [System.Runtime.CompilerServices.ExtensionAttribute]
    /// using <see cref="MemberInfo.GetCustomAttributesData()"/> for metadata-only inspection
    /// (no attribute type loading required via MetadataLoadContext).
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
            // Scanning continues for other types/methods (error recovery).
        }

        return false;
    }
}