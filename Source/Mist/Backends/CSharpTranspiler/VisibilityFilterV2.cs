using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Determines which CLR types and members are visible to Mist for the V2 ambient type provider.
/// Produces no references to Mist.Semantics.* — operates purely on System.Reflection metadata.
/// </summary>
/// <remarks>
/// Exclusion rules for members are evaluated in the following order:
/// <list type="number">
///   <item>Non-public accessibility → exclude</item>
///   <item>Events → exclude</item>
///   <item><c>ref readonly</c> return type → exclude method/property</item>
///   <item>Pointer types in signature → exclude member</item>
///   <item>Signature references an excluded type → exclude member (cascading)</item>
/// </list>
/// <para>
/// Ref-struct types (those carrying <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>,
/// e.g. <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c>) are <b>not</b> excluded by
/// <see cref="IsTypeExcluded"/>: they are projected as Mist stack-only structs and members that
/// use them stay visible.
/// </para>
/// <para>
/// CLR <c>ref</c>, <c>in</c>, and <c>out</c> parameters are all supported and pass through
/// the filter. Only <c>ref readonly</c> returns remain excluded.
/// </para>
/// </remarks>
internal sealed class VisibilityFilterV2
{
    /// <summary>
    /// Returns <c>true</c> if the type should be excluded from projection.
    /// Excludes only non-public types (including nested types with a non-public nesting chain).
    /// Ref-struct types are projected (not excluded). IntPtr/UIntPtr are not excluded.
    /// </summary>
#pragma warning disable CA1822
    public bool IsTypeExcluded(Type clrType)
#pragma warning restore CA1822
    {
        if(!IsTypePublic(clrType))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member should be excluded from projection.
    /// Evaluation order: accessibility → events → ref readonly return → pointers → excluded type references.
    /// </summary>
    public bool IsMemberExcluded(MemberInfo member)
    {
        // 1. Non-public accessibility → exclude
        if(!IsMemberPublic(member))
        {
            return true;
        }

        // 2. Events → exclude
        if(member is EventInfo)
        {
            return true;
        }

        // 3. ref readonly return type → exclude method/property
        if(HasRefReadonlyReturn(member))
        {
            return true;
        }

        // 4. Pointer types in signature → exclude member
        if(HasPointerTypes(member))
        {
            return true;
        }

        // 5. Signature references an excluded type → exclude member (cascading)
        if(ReferencesExcludedType(member))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a type is publicly accessible.
    /// Handles nested types by checking the full nesting chain.
    /// </summary>
    private static bool IsTypePublic(Type type)
    {
        if(type.IsNested)
        {
            return type.IsNestedPublic && type.DeclaringType is not null && IsTypePublic(type.DeclaringType);
        }

        return type.IsPublic;
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
    /// Returns <c>true</c> if the member has a <c>ref readonly</c> return type.
    /// Mutable <c>ref</c> returns are supported and pass through the filter.
    /// Only <c>ref readonly</c> returns (marked with <c>IsReadOnlyAttribute</c>) remain excluded
    /// because the emitted C# would require <c>ref readonly</c> binding which Mist cannot express.
    /// </summary>
    private static bool HasRefReadonlyReturn(MemberInfo member)
    {
        return member switch
        {
            MethodInfo method => method.ReturnType.IsByRef && IsRefReadonlyReturn(method),
            PropertyInfo property => property.GetGetMethod(nonPublic: true) is { } getter
                && getter.ReturnType.IsByRef && IsRefReadonlyReturn(getter),
            _ => false,
        };
    }

    /// <summary>
    /// Determines whether a method has a <c>ref readonly</c> return by checking for
    /// <c>System.Runtime.CompilerServices.IsReadOnlyAttribute</c> on the return parameter.
    /// </summary>
    /// <remarks>
    /// If the attribute metadata cannot be loaded (e.g., older framework assemblies),
    /// this defaults to <c>false</c> (treats the return as mutable <c>ref</c>), which is
    /// the safe/permissive direction — the method becomes visible rather than hidden.
    /// </remarks>
    private static bool IsRefReadonlyReturn(MethodInfo method)
    {
        try
        {
            return method.ReturnParameter
                .GetCustomAttributesData()
                .Any(a => a.AttributeType.FullName ==
                    "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }
        catch
        {
            // If metadata cannot be loaded, default to false (permissive — treat as mutable ref).
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the member's signature contains pointer types
    /// in any position (parameters, return type, property type, field type).
    /// IntPtr and UIntPtr are NOT pointer types and are not excluded.
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
    /// itself excluded by the visibility filter (e.g., a non-public type, or a named
    /// delegate with an excluded Invoke signature).
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
        if(type.IsByRef)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
            {
                return IsTypeExcludedInSignature(elementType);
            }
        }

        // Unwrap pointer types
        if(type.IsPointer)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
            {
                return IsTypeExcludedInSignature(elementType);
            }
        }

        // Unwrap array element types
        if(type.IsArray)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
            {
                return IsTypeExcludedInSignature(elementType);
            }
        }

        // Check generic type arguments recursively
        if(type.IsConstructedGenericType)
        {
            foreach(var arg in type.GetGenericArguments())
            {
                if(IsTypeExcludedInSignature(arg))
                {
                    return true;
                }
            }

            // Also check the generic type definition itself
            var genericDef = type.GetGenericTypeDefinition();
            if(IsTypeExcluded(genericDef))
            {
                return true;
            }

            // Delegate-specific check: if a constructed generic type is a named delegate,
            // inspect its Invoke method for excluded types.
            if(IsDelegateWithExcludedInvokeSignature(genericDef))
            {
                return true;
            }

            return false;
        }

        // Skip generic type parameters (T, TKey, etc.) — they are not concrete types
        if(type.IsGenericParameter)
        {
            return false;
        }

        // Delegate-specific check: if a non-generic type is a named delegate, inspect its
        // Invoke method for pointer types, ref readonly return, or ref-struct param/return.
        if(IsDelegateWithExcludedInvokeSignature(type))
        {
            return true;
        }

        // Check the concrete type itself
        return IsTypeExcluded(type);
    }

    /// <summary>
    /// Determines whether a CLR type is a named delegate whose <c>Invoke</c> method signature
    /// references excluded types (pointer parameters, ref struct parameters/return, or a
    /// <c>ref readonly</c> return). Such delegates are treated as excluded for cascading purposes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>System.Func</c>, <c>System.Action</c>, <c>System.Delegate</c>, and
    /// <c>System.MulticastDelegate</c> are never treated as excludable named delegates:
    /// <list type="bullet">
    ///   <item><c>Func</c>/<c>Action</c> are projected directly as <c>Standard.Func&lt;...&gt;</c>.</item>
    ///   <item><c>System.Delegate</c>/<c>MulticastDelegate</c> are the delegate hierarchy root
    ///     and are always visible.</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="type">The CLR type to inspect. May be a generic type definition.</param>
    /// <returns><c>true</c> if the type is a named delegate with an excluded Invoke signature.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static bool IsDelegateWithExcludedInvokeSignature(Type type)
    {
        // Only named delegates inherit from System.MulticastDelegate.
        if(type.BaseType?.FullName != "System.MulticastDelegate")
        {
            return false;
        }

        // System.Delegate and System.MulticastDelegate themselves are never excluded.
        var fullName = type.FullName ?? "";
        if(fullName is "System.Delegate" or "System.MulticastDelegate")
        {
            return false;
        }

        // System.Func<...> and System.Action<...> families are not named delegates for this purpose;
        // they are projected directly as Standard.Func<...>.
        var name = type.Name;
        if(type.Namespace == "System" &&
            (name.StartsWith("Func`", StringComparison.Ordinal) ||
             name.StartsWith("Action`", StringComparison.Ordinal) ||
             name == "Action"))
        {
            return false;
        }

        // Get the Invoke method that defines the delegate's calling convention.
        var invokeMethod = type.GetMethod("Invoke");
        if(invokeMethod is null)
        {
            return false;
        }

        // Check for ref readonly return.
        if(invokeMethod.ReturnType.IsByRef && IsRefReadonlyReturn(invokeMethod))
        {
            return true;
        }

        // Check for pointer types in the return type.
        if(ContainsPointerType(invokeMethod.ReturnType))
        {
            return true;
        }

        // Check for ref struct types in the return type.
        var returnElementType = invokeMethod.ReturnType.IsByRef
            ? invokeMethod.ReturnType.GetElementType() ?? invokeMethod.ReturnType
            : invokeMethod.ReturnType;
        if(IsRefStruct(returnElementType.IsConstructedGenericType
            ? returnElementType.GetGenericTypeDefinition()
            : returnElementType))
        {
            return true;
        }

        // Check parameters for pointer types or ref struct types.
        foreach(var param in invokeMethod.GetParameters())
        {
            var paramType = param.ParameterType;

            // Unwrap by-ref parameter types (ref/in/out are T& in metadata).
            var unwrapped = paramType.IsByRef
                ? paramType.GetElementType() ?? paramType
                : paramType;

            if(ContainsPointerType(unwrapped))
            {
                return true;
            }

            // Check if the unwrapped type is a ref struct.
            var typeToCheck = unwrapped.IsConstructedGenericType
                ? unwrapped.GetGenericTypeDefinition()
                : unwrapped;
            if(IsRefStruct(typeToCheck))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a type is a ref struct (carries
    /// <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>), e.g. <c>Span&lt;T&gt;</c>
    /// or <c>ReadOnlySpan&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This code runs under a <c>MetadataLoadContext</c>, where <see cref="Type.IsByRefLike"/>
    /// is not guaranteed to be reliable. Detection therefore reads custom-attribute metadata
    /// by <c>FullName</c>. If metadata cannot be loaded, defaults to <c>false</c>.
    /// </remarks>
    private static bool IsRefStruct(Type type)
    {
        try
        {
            return type.GetCustomAttributesData()
                .Any(a => a.AttributeType.FullName ==
                    "System.Runtime.CompilerServices.IsByRefLikeAttribute");
        }
        catch
        {
            // If metadata cannot be loaded, default to false (do not treat as a ref struct).
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the type is or contains a pointer type.
    /// IntPtr and UIntPtr are NOT pointer types (they are regular struct types).
    /// </summary>
    private static bool ContainsPointerType(Type type)
    {
        if(type.IsPointer)
        {
            return true;
        }

        // Unwrap by-ref (ref/out/in parameter types are T& in metadata)
        if(type.IsByRef)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

        // Check array element types
        if(type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

        // Check generic type arguments
        if(type.IsConstructedGenericType)
        {
            foreach(var arg in type.GetGenericArguments())
            {
                if(ContainsPointerType(arg))
                {
                    return true;
                }
            }
        }

        return false;
    }
}