using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using ReflectionPropertyInfo = System.Reflection.PropertyInfo;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Builds and prints an inspection report for a CLR type, showing its projected Mist
/// members (visible) and hidden members with reasons from <see cref="VisibilityFilter.Classify"/>.
/// Used by the <c>mist inspect &lt;TypeName&gt;</c> command.
/// </summary>
/// <remarks>
/// The inspector reuses the same SDK + package + framework assembly resolution as a build,
/// constructs the provider, resolves the type, and enumerates CLR members once to produce
/// the visible/hidden report.
/// </remarks>
public static class TypeInspector
{
    /// <summary>
    /// Resolves the specified type and prints its inspection report to the given writer.
    /// Returns 0 on success, non-zero on failure (type not resolved, SDK not found, etc.).
    /// </summary>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="additionalAssemblyPaths">Package + framework reference assembly paths.</param>
    /// <param name="typeName">Fully-qualified CLR type name (e.g., "System.Text.Encoding").</param>
    /// <param name="output">Writer for the report output.</param>
    /// <returns>0 on success, 1 on failure.</returns>
    public static int Inspect(
        string targetFramework,
        ImmutableArray<string> additionalAssemblyPaths,
        string typeName,
        TextWriter output)
    {
        // Create the type provider using the backend's resolution logic
        var backend = new CSharpTranspilerBackend();
        var resolver = backend.GetAmbientTypeResolver(targetFramework, additionalAssemblyPaths);

        if (resolver is not AssemblyTypeProvider provider)
        {
            Console.Error.WriteLine("error: Failed to initialize the assembly type provider. Ensure the .NET SDK is installed.");
            return 1;
        }

        // Resolve the type: split on last '.' to get namespace + name
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot <= 0)
        {
            Console.Error.WriteLine($"error: Could not resolve type '{typeName}'. Provide a fully-qualified CLR type name (e.g., System.Text.Encoding).");
            return 1;
        }

        var typeNamespace = typeName[..lastDot];
        var typeSimpleName = typeName[(lastDot + 1)..];

        var resolvedType = provider.ResolveType(typeNamespace, typeSimpleName);
        if (resolvedType is null)
        {
            Console.Error.WriteLine($"error: Could not resolve type '{typeName}' from any loaded assembly.");
            return 1;
        }

        PrintReport(provider, resolvedType, typeName, typeNamespace, typeSimpleName, output);
        return 0;
    }

    /// <summary>
    /// Prints the full inspection report for a resolved type.
    /// </summary>
    private static void PrintReport(
        AssemblyTypeProvider provider,
        IType resolvedType,
        string fqn,
        string typeNamespace,
        string typeSimpleName,
        TextWriter output)
    {
        // Determine kind
        var kind = resolvedType.Kind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Abstract => "interface",
            TypeKind.Enum => "enum",
            _ => "class",
        };

        output.WriteLine($"{fqn} ({kind})");

        // Find the CLR type through the provider's loaded assemblies for member enumeration
        var clrType = FindClrType(provider, typeNamespace, typeSimpleName);
        if (clrType is null)
        {
            // Shouldn't happen if ResolveType succeeded, but handle gracefully
            output.WriteLine("  (unable to enumerate CLR members)");
            return;
        }

        // Set up the filter and projector for classification and type projection
        var filter = new VisibilityFilter();
        var intrinsicMap = new IntrinsicTypeMap();
        var projector = new TypeProjector(intrinsicMap, filter, t => ResolveProjectedTypeForInspection(provider, t));
        var stdlib = FallbackStdlibTypeProvider.Instance;

        // Enumerate all public CLR members (including inherited) for classification.
        // We enumerate methods, properties, and fields — the same set the provider projects.
        var clrMembers = GetAllPublicMembers(clrType);

        var visibleProperties = new List<InspectedMember>();
        var visibleMethods = new List<InspectedMember>();
        var hiddenMembers = new List<InspectedMember>();

        foreach (var member in clrMembers)
        {
            var classification = filter.Classify(member);
            var declaringFqn = GetDeclaringTypeFqn(member, clrType);

            if (classification.Hidden)
            {
                var reasonText = FormatHiddenReason(classification);
                hiddenMembers.Add(new InspectedMember(
                    member.Name,
                    MemberCategory.Hidden,
                    reasonText,
                    declaringFqn));
            }
            else
            {
                // Visible member — project its Mist type
                switch (member)
                {
                    case ReflectionPropertyInfo property when property.GetIndexParameters().Length == 0:
                        var propType = projector.ProjectMemberType(property.PropertyType, stdlib);
                        visibleProperties.Add(new InspectedMember(
                            property.Name,
                            MemberCategory.Property,
                            propType.DisplayName,
                            declaringFqn));
                        break;

                    case MethodInfo method when !method.IsSpecialName:
                        var returnType = method.ReturnType.IsByRef
                            ? projector.ProjectMemberType(method.ReturnType.GetElementType()!, stdlib)
                            : projector.ProjectMemberType(method.ReturnType, stdlib);
                        var paramTypes = method.GetParameters()
                            .Select(p => projector.ProjectMemberType(p.ParameterType.IsByRef
                                ? p.ParameterType.GetElementType()!
                                : p.ParameterType, stdlib).DisplayName)
                            .ToList();
                        var signature = $"({string.Join(", ", paramTypes)}) -> {returnType.DisplayName}";
                        visibleMethods.Add(new InspectedMember(
                            method.Name,
                            MemberCategory.Method,
                            signature,
                            declaringFqn));
                        break;
                }
            }
        }

        // Sort ascending by name (ordinal)
        visibleProperties.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        visibleMethods.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        hiddenMembers.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        // Check if empty
        if (visibleProperties.Count == 0 && visibleMethods.Count == 0 && hiddenMembers.Count == 0)
        {
            output.WriteLine("  no members");
            return;
        }

        // Print visible members
        if (visibleProperties.Count > 0 || visibleMethods.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  VISIBLE:");

            foreach (var prop in visibleProperties)
            {
                var inherited = prop.DeclaringBaseFqn is not null ? $" [from {prop.DeclaringBaseFqn}]" : "";
                output.WriteLine($"    property {prop.Name}: {prop.TypeInfo}{inherited}");
            }

            foreach (var method in visibleMethods)
            {
                var inherited = method.DeclaringBaseFqn is not null ? $" [from {method.DeclaringBaseFqn}]" : "";
                output.WriteLine($"    method {method.Name}{method.TypeInfo}{inherited}");
            }
        }

        // Print hidden members
        if (hiddenMembers.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("  HIDDEN:");

            foreach (var hidden in hiddenMembers)
            {
                var inherited = hidden.DeclaringBaseFqn is not null ? $" [from {hidden.DeclaringBaseFqn}]" : "";
                output.WriteLine($"    {hidden.Name}: {hidden.TypeInfo}{inherited}");
            }
        }
    }

    /// <summary>
    /// Finds the CLR <see cref="Type"/> from the provider's loaded assemblies by namespace and name.
    /// Uses the provider's internal loaded assemblies to locate the type.
    /// </summary>
    private static Type? FindClrType(AssemblyTypeProvider provider, string namespaceName, string typeName)
    {
        // Access the provider's loaded assemblies to find the CLR type.
        // The provider builds a namespace index; we search through its MetadataLoadContext assemblies.
        // Since we're internal to the same assembly, we can access the private field via
        // the provider's type resolution internals. Instead, we'll use the same approach
        // that the provider does: iterate loaded assemblies from a fresh MetadataLoadContext.
        // But that's wasteful. Instead, let's use the fact that we can call into the
        // provider's resolution mechanism by leveraging reflection.

        // Actually, since TypeInspector is in the same assembly as AssemblyTypeProvider,
        // we can access internal members. Let's add a method to find the CLR type.
        return provider.FindClrTypeForInspection(namespaceName, typeName);
    }

    /// <summary>
    /// Type reference resolver for the inspection projector — resolves through the provider.
    /// </summary>
    private static IType? ResolveProjectedTypeForInspection(AssemblyTypeProvider provider, Type clrType)
    {
        if (clrType.Namespace is null || clrType.IsGenericParameter || clrType.IsArray)
            return null;

        var typeName = TypeProjector.StripGenericAritySuffix(clrType.Name);
        var arity = clrType.IsGenericType ? clrType.GetGenericArguments().Length : 0;
        return provider.ResolveType(clrType.Namespace, typeName, arity);
    }

    /// <summary>
    /// Gets all CLR members (properties, methods) of a type, including inherited and non-public.
    /// The visibility filter classifies each one as visible or hidden.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static List<MemberInfo> GetAllPublicMembers(Type clrType)
    {
        var members = new List<MemberInfo>();

        // Get properties (including inherited, including non-public for hidden classification)
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        members.AddRange(properties);

        // Get methods (including inherited, including non-public for hidden classification)
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (var method in methods)
        {
            // Skip property accessors, event accessors, and operators (they are surfaced
            // via their property/event parent or as operators, not as standalone methods)
            if (method.IsSpecialName)
                continue;

            members.Add(method);
        }

        // Get events (they are always hidden but we include them for classification)
        var events = clrType.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        members.AddRange(events);

        return members;
    }

    /// <summary>
    /// Determines the declaring base type FQN for an inherited member.
    /// Returns <c>null</c> if the member is declared directly on the inspected type.
    /// </summary>
    private static string? GetDeclaringTypeFqn(MemberInfo member, Type inspectedType)
    {
        var declaringType = member.DeclaringType;
        if (declaringType is null || declaringType == inspectedType)
            return null;

        // Return the FQN of the declaring base type
        var name = TypeProjector.StripGenericAritySuffix(declaringType.Name);
        return declaringType.Namespace is not null
            ? $"{declaringType.Namespace}.{name}"
            : name;
    }

    /// <summary>
    /// Formats the hidden reason from a <see cref="MemberClassification"/> into a human-readable string.
    /// </summary>
    private static string FormatHiddenReason(MemberClassification classification)
    {
        return classification.Reason switch
        {
            MemberHiddenReason.NonPublic => "HIDDEN: non-public",
            MemberHiddenReason.Event => "HIDDEN: event",
            MemberHiddenReason.OutParameter => "HIDDEN: out parameter",
            MemberHiddenReason.RefReadonlyReturn => "HIDDEN: ref readonly return",
            MemberHiddenReason.Pointer => "HIDDEN: pointer in signature",
            MemberHiddenReason.RefStructType when classification.OffendingTypeNames.Length > 0 =>
                $"HIDDEN: ref struct in signature ({string.Join(", ", classification.OffendingTypeNames)})",
            MemberHiddenReason.OtherExcludedType when classification.OffendingTypeNames.Length > 0 =>
                $"HIDDEN: signature uses {string.Join(", ", classification.OffendingTypeNames)}",
            MemberHiddenReason.OtherExcludedType => "HIDDEN: excluded type in signature",
            _ => "HIDDEN: unknown reason",
        };
    }

    private enum MemberCategory
    {
        Property,
        Method,
        Hidden,
    }

    private sealed record InspectedMember(
        string Name,
        MemberCategory Category,
        string TypeInfo,
        string? DeclaringBaseFqn);
}
