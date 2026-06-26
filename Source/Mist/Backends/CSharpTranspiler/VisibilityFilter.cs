using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// The reason a CLR member is hidden from the Mist projection, surfaced by
/// <see cref="VisibilityFilter.Classify"/> for the <c>mist inspect</c> command.
/// </summary>
/// <remarks>
/// Values mirror the exclusion rules evaluated by <see cref="VisibilityFilter.IsMemberExcluded"/>,
/// in the same order. <see cref="None"/> means the member is visible. Note that CLR <c>out</c>/<c>ref</c>
/// parameters are now supported and never trigger exclusion, so <see cref="OutParameter"/> is retained
/// only to mirror the (currently dead) <c>HasOutParameters</c> check and will not occur in practice.
/// </remarks>
internal enum MemberHiddenReason
{
    /// <summary>The member is visible (not hidden).</summary>
    None,

    /// <summary>The member is not publicly accessible.</summary>
    NonPublic,

    /// <summary>The member is an event (events are not projected).</summary>
    Event,

    /// <summary>The member has a CLR <c>out</c> parameter. Retained for parity; does not occur today.</summary>
    OutParameter,

    /// <summary>The member has a <c>ref readonly</c> return type.</summary>
    RefReadonlyReturn,

    /// <summary>The member's signature contains a pointer type.</summary>
    Pointer,

    /// <summary>
    /// Reserved. Previously flagged members whose signature referenced a ref-struct type
    /// (e.g. <c>Span&lt;T&gt;</c>). Ref structs are now projected rather than excluded, so this
    /// reason is no longer triggered by projection. Retained for compatibility with the inspect
    /// command's classification surface.
    /// </summary>
    RefStructType,

    /// <summary>The member's signature references some other excluded type (e.g. a non-public type).</summary>
    OtherExcludedType,
}

/// <summary>
/// The result of classifying why a CLR member is (or is not) hidden from the Mist projection.
/// Produced by <see cref="VisibilityFilter.Classify"/> for the inspect command.
/// </summary>
/// <param name="Hidden"><c>true</c> if the member is excluded from projection.</param>
/// <param name="Reason">The reason the member is hidden, or <see cref="MemberHiddenReason.None"/> when visible.</param>
/// <param name="OffendingTypeNames">
/// For <see cref="MemberHiddenReason.RefStructType"/> and <see cref="MemberHiddenReason.OtherExcludedType"/>,
/// the display names of each excluded type referenced by the signature (e.g. <c>Span&lt;Byte&gt;</c>).
/// Empty for all other reasons and for visible members.
/// </param>
internal sealed record MemberClassification(
    bool Hidden,
    MemberHiddenReason Reason,
    ImmutableArray<string> OffendingTypeNames);

/// <summary>
/// Determines which CLR types and members are visible to Mist.
/// Members that use unsupported CLR features (ref readonly returns,
/// pointer types) are hidden rather than producing errors, so developers
/// can still use the supported surface of a type.
/// </summary>
/// <remarks>
/// Exclusion rules are evaluated in the following order:
/// <list type="number">
///   <item>Non-public accessibility → exclude</item>
///   <item>Events → exclude</item>
///   <item><c>ref readonly</c> return type → exclude method/property</item>
///   <item>Pointer types in signature → exclude member</item>
///   <item>Signature references an excluded type → exclude member</item>
/// </list>
/// <para>
/// Ref-struct types (those carrying <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>,
/// e.g. <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c>) are <b>not</b> excluded by
/// <see cref="IsTypeExcluded"/>: they are projected as Mist stack-only structs and members that
/// use them stay visible. The downstream C# compiler remains the safety backstop for ref-struct
/// misuse; first-party escape/lifetime analysis is future work (Requirement 9).
/// </para>
/// <para>
/// CLR <c>ref</c> parameters, <c>in</c> parameters, <c>out</c> parameters, and mutable
/// <c>ref</c> returns are now supported and pass through the filter. <c>out</c> parameters
/// are projected as <c>ref</c> with <see cref="Mist.Semantics.Model.ClrCallingConvention.Out"/>.
/// Only <c>ref readonly</c> returns remain excluded.
/// Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7.
/// </para>
/// </remarks>
internal sealed class VisibilityFilter
{
    /// <summary>
    /// Returns <c>true</c> if the type should be excluded from projection.
    /// Excludes only non-public types. Ref-struct types (those carrying
    /// <c>IsByRefLikeAttribute</c>, e.g. <c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c>)
    /// are <b>projected</b> as Mist stack-only structs (<c>StructType.IsRefStruct = true</c>),
    /// not excluded — see <see cref="TypeProjector"/>. The downstream C# compiler is the
    /// safety backstop for any ref-struct misuse; first-party escape/lifetime analysis is
    /// future work (Requirement 9).
    /// </summary>
    // CA1822 suppressed: IsTypeExcluded is intentionally an instance method because it is
    // called from IsTypeExcludedInSignature (instance) to enable the recursive exclusion
    // check in IsMemberExcluded. The design document specifies this as a public instance API.
#pragma warning disable CA1822
    public bool IsTypeExcluded(Type clrType)
#pragma warning restore CA1822
    {
        // Non-public types are excluded.
        if(!IsTypePublic(clrType))
            return true;

        // Ref-struct types are NOT excluded — they are projected as stack-only structs.
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member should be excluded from projection.
    /// Evaluation order: accessibility → events → ref readonly return → pointers → excluded type references.
    /// </summary>
    public bool IsMemberExcluded(MemberInfo member)
    {
        // 1. Non-public accessibility → exclude (Requirement 6.7)
        if(!IsMemberPublic(member))
            return true;

        // 2. Events → exclude (Requirement 5.8)
        if(member is EventInfo)
            return true;

        // 3. out parameters — no longer excluded (projected as ref with ClrCallingConvention.Out)
        if(HasOutParameters(member))
            return true;

        // 4. ref parameters — no longer excluded (CLR ref is now supported)
        if(HasRefParameters(member))
            return true;

        // 5. ref readonly return type → exclude method/property (Requirement 6.3)
        if(HasRefReturn(member))
            return true;

        // 6. Pointer types in signature → exclude member (Requirement 6.4)
        if(HasPointerTypes(member))
            return true;

        // 7. Signature references an excluded type → exclude member (Requirement 6.6)
        if(ReferencesExcludedType(member))
            return true;

        return false;
    }

    /// <summary>
    /// Classifies why a member is hidden from the Mist projection, for the <c>mist inspect</c>
    /// command. This mirrors the SAME exclusion logic and order as <see cref="IsMemberExcluded"/>
    /// (accessibility → events → out params → ref readonly return → pointers → excluded-type
    /// references) but, instead of a boolean, returns the reason and — for excluded-type references —
    /// the display names of each offending type (e.g. <c>Span&lt;Byte&gt;</c>) so the inspect command
    /// can produce reason text like <c>HIDDEN: signature uses Span&lt;Byte&gt;</c>.
    /// </summary>
    /// <remarks>
    /// The hot build path uses the boolean <see cref="IsMemberExcluded"/>; this method is intended
    /// only for the (cold) inspect command, which additionally collects offending type names.
    /// For a visible member, returns <c>{ Hidden = false, Reason = None, OffendingTypeNames = empty }</c>.
    /// </remarks>
    public MemberClassification Classify(MemberInfo member)
    {
        // 1. Non-public accessibility → exclude (Requirement 6.7)
        if(!IsMemberPublic(member))
            return Hidden(MemberHiddenReason.NonPublic);

        // 2. Events → exclude (Requirement 5.8)
        if(member is EventInfo)
            return Hidden(MemberHiddenReason.Event);

        // 3. out parameters — no longer excluded; check retained to mirror IsMemberExcluded order.
        // HasOutParameters currently returns false, so this never triggers in practice.
        if(HasOutParameters(member))
            return Hidden(MemberHiddenReason.OutParameter);

        // 4. ref parameters — no longer excluded (HasRefParameters always returns false). There is
        // no corresponding MemberHiddenReason because this exclusion was removed and cannot trigger.

        // 5. ref readonly return type → exclude method/property (Requirement 6.3)
        if(HasRefReturn(member))
            return Hidden(MemberHiddenReason.RefReadonlyReturn);

        // 6. Pointer types in signature → exclude member (Requirement 6.4)
        if(HasPointerTypes(member))
            return Hidden(MemberHiddenReason.Pointer);

        // 7. Signature references an excluded type → exclude member (Requirement 6.6).
        // Gather the offending types so the inspect command can name them (Requirement 9.4).
        var offendingTypes = CollectExcludedTypesInSignature(member);
        if(offendingTypes.Count > 0)
        {
            // Prefer reporting RefStructType when any offending type is a ref struct; otherwise the
            // member is hidden because of some other excluded type (e.g. a non-public type).
            var anyRefStruct = offendingTypes.Any(IsOffendingTypeRefStruct);
            var reason = anyRefStruct ? MemberHiddenReason.RefStructType : MemberHiddenReason.OtherExcludedType;

            // De-duplicate by rendered display name, preserving first-seen order for determinism.
            var names = ImmutableArray.CreateBuilder<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach(var offending in offendingTypes)
            {
                var name = RenderTypeDisplayName(offending);
                if(seen.Add(name))
                    names.Add(name);
            }

            return new MemberClassification(true, reason, names.ToImmutable());
        }

        return new MemberClassification(false, MemberHiddenReason.None, ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// Builds a hidden classification with no offending type names (used for reasons other than
    /// excluded-type references, which do not carry offending types).
    /// </summary>
    private static MemberClassification Hidden(MemberHiddenReason reason) =>
        new(true, reason, ImmutableArray<string>.Empty);

    /// <summary>
    /// Determines whether a type is a ref struct (carries
    /// <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>), e.g. <c>Span&lt;T&gt;</c>
    /// or <c>ReadOnlySpan&lt;T&gt;</c>.
    /// </summary>
    /// <remarks>
    /// This code runs under a <c>MetadataLoadContext</c>, where <see cref="Type.IsByRefLike"/>
    /// is not guaranteed to be reliable. Detection therefore reads custom-attribute metadata
    /// by <c>FullName</c>, mirroring <see cref="IsRefReadonlyReturn"/>. For a constructed generic
    /// such as <c>Span&lt;byte&gt;</c>, the attribute lives on the generic type definition, which
    /// <see cref="IsTypeExcludedInSignature"/> passes here via <c>GetGenericTypeDefinition()</c>.
    /// If metadata cannot be loaded, defaults to <c>false</c> (the type is not treated as a ref struct).
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
    /// Determines whether a type is publicly accessible.
    /// Handles nested types by checking the full nesting chain.
    /// </summary>
    private static bool IsTypePublic(Type type)
    {
        if(type.IsNested)
            return type.IsNestedPublic && type.DeclaringType is not null && IsTypePublic(type.DeclaringType);

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
    /// Previously excluded methods with CLR <c>out</c> parameters. CLR <c>out</c> parameters
    /// are now projected as <c>ref</c> with <see cref="Mist.Semantics.Model.ClrCallingConvention.Out"/>,
    /// so this method unconditionally returns <c>false</c>.
    /// </summary>
    private static bool HasOutParameters(MemberInfo member)
    {
        // CLR out parameters are now supported — projected as ref with ClrCallingConvention.Out.
        return false;
    }

    /// <summary>
    /// Previously excluded methods with CLR <c>ref</c> parameters. CLR <c>ref</c> parameters
    /// are now fully supported, so this method unconditionally returns <c>false</c>.
    /// Only <c>out</c> parameters remain excluded (handled by <see cref="HasOutParameters"/>).
    /// </summary>
    private static bool HasRefParameters(MemberInfo member)
    {
        // CLR ref parameters are now supported — only out params are excluded.
        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the member has a <c>ref readonly</c> return type.
    /// Mutable <c>ref</c> returns are now supported and pass through the filter.
    /// Only <c>ref readonly</c> returns (marked with <c>IsReadOnlyAttribute</c>) remain excluded
    /// because the emitted C# would require <c>ref readonly</c> binding which Mist cannot express.
    /// </summary>
    private static bool HasRefReturn(MemberInfo member)
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
        if(type.IsByRef)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Unwrap pointer types
        if(type.IsPointer)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Unwrap array element types
        if(type.IsArray)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        // Check generic type arguments recursively
        if(type.IsConstructedGenericType)
        {
            foreach(var arg in type.GetGenericArguments())
            {
                if(IsTypeExcludedInSignature(arg))
                    return true;
            }

            // Also check the generic type definition itself
            var genericDef = type.GetGenericTypeDefinition();
            if(IsTypeExcluded(genericDef))
                return true;

            // Delegate-specific check: if a constructed generic type is a named delegate,
            // inspect its Invoke method for excluded types (Requirements 5.2, 5.3, 5.4).
            if(IsDelegateWithExcludedInvokeSignature(genericDef))
                return true;

            return false;
        }

        // Skip generic type parameters (T, TKey, etc.) — they are not concrete types
        if(type.IsGenericParameter)
            return false;

        // Delegate-specific check: if a non-generic type is a named delegate, inspect its
        // Invoke method for pointer types, ref structs, or ref readonly return (Requirements 5.2, 5.3, 5.4).
        if(IsDelegateWithExcludedInvokeSignature(type))
            return true;

        // Check the concrete type itself
        return IsTypeExcluded(type);
    }

    /// <summary>
    /// Determines whether a CLR type is a named delegate whose <c>Invoke</c> method signature
    /// references excluded types (pointer parameters, ref struct parameters, or a <c>ref readonly</c>
    /// return). Such delegates are hidden from Mist because they cannot be safely projected.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>System.Func</c>, <c>System.Action</c>, <c>System.Delegate</c>, and
    /// <c>System.MulticastDelegate</c> are never treated as excludable named delegates:
    /// <list type="bullet">
    ///   <item><c>Func</c>/<c>Action</c> are projected directly as <c>Standard.Func&lt;...&gt;</c>.</item>
    ///   <item><c>System.Delegate</c>/<c>MulticastDelegate</c> are the delegate hierarchy root
    ///     and are always visible (Requirement 5.5).</item>
    /// </list>
    /// </para>
    /// <para>
    /// This method runs under <see cref="System.Reflection.MetadataLoadContext"/> so uses
    /// attribute-based detection for <c>ref readonly</c> returns and <c>IsByRefLikeAttribute</c>
    /// for ref structs.
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
            return false;

        // System.Delegate and System.MulticastDelegate themselves are never excluded (Requirement 5.5).
        var fullName = type.FullName ?? "";
        if(fullName is "System.Delegate" or "System.MulticastDelegate")
            return false;

        // System.Func<...> and System.Action<...> families are not named delegates for this purpose;
        // they are projected directly as Standard.Func<...>.
        var name = type.Name;
        if(type.Namespace == "System" &&
            (name.StartsWith("Func`", StringComparison.Ordinal) ||
             name.StartsWith("Action`", StringComparison.Ordinal) ||
             name == "Action"))
            return false;

        // Get the Invoke method that defines the delegate's calling convention.
        var invokeMethod = type.GetMethod("Invoke");
        if(invokeMethod is null)
            return false;

        // Check for ref readonly return (Requirement 5.2).
        if(invokeMethod.ReturnType.IsByRef && IsRefReadonlyReturn(invokeMethod))
            return true;

        // Check for pointer types in the return type.
        if(ContainsPointerType(invokeMethod.ReturnType))
            return true;

        // Check for ref struct types in the return type.
        var returnElementType = invokeMethod.ReturnType.IsByRef
            ? invokeMethod.ReturnType.GetElementType() ?? invokeMethod.ReturnType
            : invokeMethod.ReturnType;
        if(IsRefStruct(returnElementType.IsConstructedGenericType
            ? returnElementType.GetGenericTypeDefinition()
            : returnElementType))
            return true;

        // Check parameters for pointer types or ref struct types.
        foreach(var param in invokeMethod.GetParameters())
        {
            var paramType = param.ParameterType;

            // Unwrap by-ref parameter types (ref/in/out are T& in metadata).
            var unwrapped = paramType.IsByRef
                ? paramType.GetElementType() ?? paramType
                : paramType;

            if(ContainsPointerType(unwrapped))
                return true;

            // Check if the unwrapped type is a ref struct.
            var typeToCheck = unwrapped.IsConstructedGenericType
                ? unwrapped.GetGenericTypeDefinition()
                : unwrapped;
            if(IsRefStruct(typeToCheck))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Member-signature counterpart to <see cref="ReferencesExcludedType"/> that, instead of a
    /// boolean, gathers the offending excluded types found in each signature position. Mirrors the
    /// same positions (return type, parameters, property type, field type). Used by
    /// <see cref="Classify"/> for the inspect command; the boolean build path is unaffected.
    /// </summary>
    private List<Type> CollectExcludedTypesInSignature(MemberInfo member)
    {
        var offending = new List<Type>();
        switch(member)
        {
            case MethodInfo method:
                CollectExcludedTypesInSignature(method.ReturnType, offending);
                foreach(var p in method.GetParameters())
                    CollectExcludedTypesInSignature(p.ParameterType, offending);
                break;
            case ConstructorInfo ctor:
                foreach(var p in ctor.GetParameters())
                    CollectExcludedTypesInSignature(p.ParameterType, offending);
                break;
            case PropertyInfo property:
                CollectExcludedTypesInSignature(property.PropertyType, offending);
                break;
            case FieldInfo field:
                CollectExcludedTypesInSignature(field.FieldType, offending);
                break;
        }

        return offending;
    }

    /// <summary>
    /// Gathering counterpart to <see cref="IsTypeExcludedInSignature"/>: walks a signature type in
    /// the same way (unwrapping by-ref/pointer/array wrappers, descending into generic arguments,
    /// skipping generic parameters) and appends every excluded type encountered to
    /// <paramref name="offending"/>. For a constructed generic whose definition is excluded
    /// (e.g. <c>Span&lt;byte&gt;</c>), the constructed type is recorded so it renders as
    /// <c>Span&lt;Byte&gt;</c>.
    /// </summary>
    private void CollectExcludedTypesInSignature(Type type, List<Type> offending)
    {
        // Unwrap by-ref (ref/out/in parameter types are T& in metadata)
        if(type.IsByRef)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                CollectExcludedTypesInSignature(elementType, offending);
            return;
        }

        // Unwrap pointer types
        if(type.IsPointer)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                CollectExcludedTypesInSignature(elementType, offending);
            return;
        }

        // Unwrap array element types
        if(type.IsArray)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                CollectExcludedTypesInSignature(elementType, offending);
            return;
        }

        // Descend into generic type arguments, then check the generic definition itself.
        if(type.IsConstructedGenericType)
        {
            foreach(var arg in type.GetGenericArguments())
                CollectExcludedTypesInSignature(arg, offending);

            var genericDef = type.GetGenericTypeDefinition();
            if(IsTypeExcluded(genericDef))
                offending.Add(type);

            // Delegate-specific check: if the generic definition is a named delegate with an
            // excluded Invoke signature, record the constructed type as offending (Requirements 5.2, 5.3, 5.4).
            if(IsDelegateWithExcludedInvokeSignature(genericDef))
                offending.Add(type);

            return;
        }

        // Skip generic type parameters (T, TKey, etc.) — they are not concrete types
        if(type.IsGenericParameter)
            return;

        // Delegate-specific check: if a non-generic type is a named delegate with an excluded
        // Invoke signature, record it as offending (Requirements 5.2, 5.3, 5.4).
        if(IsDelegateWithExcludedInvokeSignature(type))
            offending.Add(type);

        // Check the concrete type itself
        if(IsTypeExcluded(type))
            offending.Add(type);
    }

    /// <summary>
    /// Determines whether an offending signature type is a ref struct. For a constructed generic
    /// (e.g. <c>Span&lt;byte&gt;</c>), the <c>IsByRefLikeAttribute</c> lives on the generic type
    /// definition, so this checks the definition in that case.
    /// </summary>
    private static bool IsOffendingTypeRefStruct(Type type)
    {
        var typeToCheck = type.IsConstructedGenericType ? type.GetGenericTypeDefinition() : type;
        return IsRefStruct(typeToCheck);
    }

    /// <summary>
    /// Renders a CLR type's display name for inspect reason text, using the CLR simple name with the
    /// generic arity suffix stripped and generic arguments rendered recursively, e.g.
    /// <c>Span&lt;Byte&gt;</c> or <c>Dictionary&lt;String, Int32&gt;</c>. Array/pointer wrappers are
    /// unwrapped to their element type (the offending element is what matters for the reason).
    /// </summary>
    private static string RenderTypeDisplayName(Type type)
    {
        if(type.IsByRef || type.IsPointer || type.IsArray)
        {
            var elementType = type.GetElementType();
            if(elementType is not null)
                return RenderTypeDisplayName(elementType);
        }

        if(type.IsConstructedGenericType)
        {
            var name = TypeProjector.StripGenericAritySuffix(type.Name);
            var args = type.GetGenericArguments().Select(RenderTypeDisplayName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        return TypeProjector.StripGenericAritySuffix(type.Name);
    }

    /// <summary>
    /// Returns <c>true</c> if the type is or contains a pointer type.
    /// </summary>
    private static bool ContainsPointerType(Type type)
    {
        if(type.IsPointer)
            return true;

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