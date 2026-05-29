using System.Reflection;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Determines which CLR types and members are visible to Mist.
/// Members that use unsupported CLR features (out params, ref params, ref returns,
/// pointer types, ref structs) are hidden rather than producing errors, so developers
/// can still use the supported surface of a type.
/// </summary>
/// <remarks>
/// Exclusion rules are evaluated in the following order:
/// <list type="number">
///   <item>Non-public accessibility → exclude</item>
///   <item>Events → exclude</item>
///   <item>Ref struct types → exclude entirely</item>
///   <item><c>out</c> parameters → exclude method/constructor</item>
///   <item><c>ref</c> parameters (excluding <c>in</c>) → exclude method/constructor</item>
///   <item><c>ref</c> return type → exclude method/property</item>
///   <item>Pointer types in signature → exclude member</item>
///   <item>Signature references an excluded type → exclude member</item>
/// </list>
/// <para>
/// <c>in</c> parameters are ref-readonly and are treated as regular pass-by-value
/// parameters (NOT filtered). Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7.
/// </para>
/// </remarks>
internal sealed class VisibilityFilter
{
    /// <summary>
    /// Returns <c>true</c> if the type should be excluded from projection.
    /// Excludes: non-public types, ref structs.
    /// </summary>
    // CA1822 suppressed: IsTypeExcluded is intentionally an instance method because it is
    // called from IsTypeExcludedInSignature (instance) to enable the recursive exclusion
    // check in IsMemberExcluded. The design document specifies this as a public instance API.
#pragma warning disable CA1822
    public bool IsTypeExcluded(Type clrType)
#pragma warning restore CA1822
    {
        // Non-public types are excluded.
        if (!IsTypePublic(clrType))
            return true;

        // Ref structs are excluded entirely (Requirement 6.5).
        if (IsRefStruct(clrType))
            return true;

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member should be excluded from projection.
    /// Evaluation order: accessibility → events → ref struct → out → ref → ref return → pointers → excluded type references.
    /// </summary>
    public bool IsMemberExcluded(MemberInfo member)
    {
        // 1. Non-public accessibility → exclude (Requirement 6.7)
        if (!IsMemberPublic(member))
            return true;

        // 2. Events → exclude (Requirement 5.8)
        if (member is EventInfo)
            return true;

        // 3. Ref struct declaring type → exclude entirely (Requirement 6.5)
        if (member.DeclaringType is not null && IsRefStruct(member.DeclaringType))
            return true;

        // 4. out parameters → exclude method/constructor (Requirement 6.1)
        if (HasOutParameters(member))
            return true;

        // 5. ref parameters (excluding in) → exclude method/constructor (Requirement 6.2)
        if (HasRefParameters(member))
            return true;

        // 6. ref return type → exclude method/property (Requirement 6.3)
        if (HasRefReturn(member))
            return true;

        // 7. Pointer types in signature → exclude member (Requirement 6.4)
        if (HasPointerTypes(member))
            return true;

        // 8. Signature references an excluded type → exclude member (Requirement 6.6)
        if (ReferencesExcludedType(member))
            return true;

        return false;
    }

    /// <summary>
    /// Determines whether a type is publicly accessible.
    /// Handles nested types by checking the full nesting chain.
    /// </summary>
    private static bool IsTypePublic(Type type)
    {
        if (type.IsNested)
            return type.IsNestedPublic && type.DeclaringType is not null && IsTypePublic(type.DeclaringType);

        return type.IsPublic;
    }

    /// <summary>
    /// Determines whether a type is a ref struct by checking for the
    /// <c>IsByRefLikeAttribute</c> (the CLR marker for ref structs).
    /// </summary>
    private static bool IsRefStruct(Type type)
    {
        // In MetadataLoadContext, ref structs are marked with IsByRefLikeAttribute.
        // The Type.IsByRefLike property is available on .NET 7+.
        return type.IsValueType && type.IsByRefLike;
    }

    /// <summary>
    /// Determines whether a member has public accessibility.
    /// </summary>
    private static bool IsMemberPublic(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => method.IsPublic,
            ConstructorInfo ctor => ctor.IsPublic,
            PropertyInfo property => HasPublicAccessor(property),
            FieldInfo field => field.IsPublic,
            EventInfo evt => evt.AddMethod is not null && evt.AddMethod.IsPublic,
            Type type => IsTypePublic(type),
            _ => false,
        };
    }

    /// <summary>
    /// A property is considered public if it has at least one public accessor (getter or setter).
    /// </summary>
    private static bool HasPublicAccessor(PropertyInfo property)
    {
        var getter = property.GetGetMethod(nonPublic: true);
        var setter = property.GetSetMethod(nonPublic: true);
        return (getter is not null && getter.IsPublic) || (setter is not null && setter.IsPublic);
    }

    /// <summary>
    /// Returns <c>true</c> if the member is a method or constructor with <c>out</c> parameters.
    /// </summary>
    private static bool HasOutParameters(MemberInfo member)
    {
        var parameters = GetParameters(member);
        if (parameters is null)
            return false;

        foreach (var param in parameters)
        {
            if (param.IsOut)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member is a method or constructor with <c>ref</c> parameters
    /// (excluding <c>in</c> parameters, which are ref-readonly and treated as pass-by-value).
    /// </summary>
    private static bool HasRefParameters(MemberInfo member)
    {
        var parameters = GetParameters(member);
        if (parameters is null)
            return false;

        foreach (var param in parameters)
        {
            if (param.ParameterType.IsByRef && !param.IsOut && !param.IsIn)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member has a <c>ref</c> return type.
    /// Applies to methods and properties.
    /// </summary>
    private static bool HasRefReturn(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => method.ReturnType.IsByRef,
            PropertyInfo property => property.GetGetMethod(nonPublic: true) is { } getter && getter.ReturnType.IsByRef,
            _ => false,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the member's signature contains pointer types
    /// in any position (parameters, return type, property type, field type).
    /// </summary>
    private static bool HasPointerTypes(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => ContainsPointerType(method.ReturnType)
                || method.GetParameters().Any(p => ContainsPointerType(p.ParameterType)),
            ConstructorInfo ctor => ctor.GetParameters().Any(p => ContainsPointerType(p.ParameterType)),
            PropertyInfo property => ContainsPointerType(property.PropertyType),
            FieldInfo field => ContainsPointerType(field.FieldType),
            _ => false,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the member's signature references a type that is
    /// itself excluded by the visibility filter (e.g., a ref struct, a non-public type).
    /// </summary>
    private bool ReferencesExcludedType(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => IsTypeExcludedInSignature(method.ReturnType)
                || method.GetParameters().Any(p => IsTypeExcludedInSignature(p.ParameterType)),
            ConstructorInfo ctor => ctor.GetParameters().Any(p => IsTypeExcludedInSignature(p.ParameterType)),
            PropertyInfo property => IsTypeExcludedInSignature(property.PropertyType),
            FieldInfo field => IsTypeExcludedInSignature(field.FieldType),
            _ => false,
        };
    }

    /// <summary>
    /// Checks whether a type reference in a signature points to an excluded type.
    /// Handles by-ref wrappers, pointer wrappers, array element types, and
    /// generic type arguments recursively.
    /// </summary>
    private bool IsTypeExcludedInSignature(Type type)
    {
        // Unwrap by-ref (ref/out/in parameter types are T& in metadata)
        if (type.IsByRef)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Unwrap pointer types
        if (type.IsPointer)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Unwrap array element types
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Check generic type arguments recursively
        if (type.IsConstructedGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (IsTypeExcludedInSignature(arg))
                    return true;
            }

            // Also check the generic type definition itself
            var genericDef = type.GetGenericTypeDefinition();
            if (IsTypeExcluded(genericDef))
                return true;

            return false;
        }

        // Skip generic type parameters (T, TKey, etc.) — they are not concrete types
        if (type.IsGenericParameter)
            return false;

        // Check the concrete type itself
        return IsTypeExcluded(type);
    }

    /// <summary>
    /// Returns <c>true</c> if the type is or contains a pointer type.
    /// </summary>
    private static bool ContainsPointerType(Type type)
    {
        if (type.IsPointer)
            return true;

        // Unwrap by-ref (ref/out/in parameter types are T& in metadata)
        if (type.IsByRef)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

        // Check array element types
        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

        // Check generic type arguments
        if (type.IsConstructedGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (ContainsPointerType(arg))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the parameters for a method or constructor member.
    /// Returns <c>null</c> for members that don't have parameters (fields, properties, events).
    /// </summary>
    private static ParameterInfo[]? GetParameters(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => method.GetParameters(),
            ConstructorInfo ctor => ctor.GetParameters(),
            _ => null,
        };
    }
}
