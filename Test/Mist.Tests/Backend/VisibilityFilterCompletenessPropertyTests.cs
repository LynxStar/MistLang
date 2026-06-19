using System.Linq;
using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for the Visibility Filter Completeness property.
///
/// **Validates: Requirements 1.1, 1.2, 3.1, 4.1, 4.2, 4.3, 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7**
///
/// Property 3: For any CLR member, the visibility filter SHALL exclude it if and only if
/// at least one of the following holds: (a) it is not public, (b) it is an event,
/// (c) it has a ref readonly return type,
/// (d) it has pointer types in its signature, or (e) its signature references a type that is
/// itself excluded (i.e. a non-public type). Ref-struct types (those carrying
/// IsByRefLikeAttribute, e.g. Span&lt;T&gt;) are NOT excluded under csharp-interop-v2 Option A —
/// they are projected as Mist stack-only structs.
/// Ref parameters (including in), out parameters, and mutable ref returns now pass through.
/// All other public members SHALL be projected.
/// </summary>
public sealed class VisibilityFilterCompletenessPropertyTests
{
    private static readonly VisibilityFilter Filter = new();

    /// <summary>
    /// All members (public and non-public) from the InteropFixtures assembly,
    /// used as the population for random selection.
    /// </summary>
    private static readonly MemberInfo[] AllMembers = LoadAllMembers();

    private static MemberInfo[] LoadAllMembers()
    {
        var assembly = typeof(Mist.Tests.InteropFixtures.SimpleClass).Assembly;
        var members = new List<MemberInfo>();

        foreach (var type in assembly.GetTypes())
        {
            // Include all members with all binding flags to cover public, non-public, static, instance
            var typeMembers = type.GetMembers(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.DeclaredOnly);

            members.AddRange(typeMembers);
        }

        return members.ToArray();
    }

    /// <summary>
    /// Generates random indices into the AllMembers array for property-based testing.
    /// </summary>
    public static Arbitrary<int> ArbitraryInt32()
    {
        return Gen.Choose(0, AllMembers.Length - 1).ToArbitrary();
    }

    /// <summary>
    /// Property 3: Visibility Filter Completeness — for any randomly selected member from
    /// the fixture assembly, the filter's exclusion decision matches the expected exclusion
    /// rules defined in Requirements 6.1–6.7.
    ///
    /// The filter should exclude a member if and only if at least one exclusion rule applies.
    /// </summary>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(VisibilityFilterCompletenessPropertyTests) })]
    public bool VisibilityFilter_ExclusionDecision_MatchesRules(int memberIndex)
    {
        var member = AllMembers[memberIndex];

        var actualExcluded = Filter.IsMemberExcluded(member);
        var expectedExcluded = ShouldBeExcluded(member);

        return actualExcluded == expectedExcluded;
    }

    /// <summary>
    /// Reference implementation of the exclusion rules from Requirements 6.1–6.7.
    /// This is an independent re-implementation of the rules to verify the filter's behavior.
    /// </summary>
    private static bool ShouldBeExcluded(MemberInfo member)
    {
        // Rule (a): Non-public accessibility → exclude (Requirement 6.7)
        if (!IsMemberPublic(member))
            return true;

        // Rule (b): Events → exclude (Requirement 5.8 / design doc)
        if (member is EventInfo)
            return true;

        // Rule (c): out parameters — no longer excluded (projected as ref with ClrCallingConvention.Out)

        // Rule (d): ref readonly return type → exclude (Requirement 4.3, 6.3)
        // Mutable ref returns now pass through (CLR Ref Interop feature)
        if (HasRefReadonlyReturn(member))
            return true;

        // Rule (f): Pointer types in signature → exclude (Requirement 6.4)
        if (HasPointerTypes(member))
            return true;

        // Rule (g): Signature references an excluded type → exclude (Requirement 6.6)
        if (ReferencesExcludedType(member))
            return true;

        return false;
    }

    // ── Reference implementation helpers ─────────────────────────────

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

    private static bool HasPublicAccessor(PropertyInfo property)
    {
        var getter = property.GetGetMethod(nonPublic: true);
        var setter = property.GetSetMethod(nonPublic: true);
        return (getter is not null && getter.IsPublic) || (setter is not null && setter.IsPublic);
    }

    private static bool IsTypePublic(Type type)
    {
        if (type.IsNested)
            return type.IsNestedPublic && type.DeclaringType is not null && IsTypePublic(type.DeclaringType);
        return type.IsPublic;
    }

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

    private static bool IsRefReadonlyReturn(MethodInfo method)
    {
        return method.ReturnParameter
            .GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName ==
                "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

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

    private static bool ContainsPointerType(Type type)
    {
        if (type.IsPointer)
            return true;

        if (type.IsByRef)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointerType(elementType);
        }

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

    private static bool ReferencesExcludedType(MemberInfo member)
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

    private static bool IsTypeExcludedInSignature(Type type)
    {
        if (type.IsByRef)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        if (type.IsPointer)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        if (type.IsArray)
        {
            var elementType = type.GetElementType();
            if (elementType is not null)
                return IsTypeExcludedInSignature(elementType);
        }

        if (type.IsConstructedGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (IsTypeExcludedInSignature(arg))
                    return true;
            }

            var genericDef = type.GetGenericTypeDefinition();
            if (IsTypeExcludedByRules(genericDef))
                return true;

            return false;
        }

        if (type.IsGenericParameter)
            return false;

        return IsTypeExcludedByRules(type);
    }

    private static bool IsTypeExcludedByRules(Type type)
    {
        if (!IsTypePublic(type))
            return true;

        // Ref-struct types (carry IsByRefLikeAttribute, e.g. Span<T>/ReadOnlySpan<T>) are NOT
        // excluded under csharp-interop-v2 Option A — they are projected as Mist stack-only
        // structs. Only non-public types are excluded at the type level.
        return false;
    }

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
