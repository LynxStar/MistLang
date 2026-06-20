using System.Collections.Immutable;
using System.Reflection;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

using ClrType = System.Type;
using NullableReflection = System.Nullable;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Converts CLR metadata into Mist <see cref="IType"/> and <see cref="ISymbol"/> instances.
/// Applies intrinsic substitution and visibility filtering during projection.
///
/// <para>
/// Projection rules:
/// <list type="bullet">
///   <item>CLR class → <see cref="ClassType"/> (with ContainingNamespace, BaseType, Interfaces, TypeParameters)</item>
///   <item>CLR struct (non-ref) → <see cref="StructType"/></item>
///   <item>CLR interface → <see cref="InterfaceType"/></item>
///   <item>CLR enum → <see cref="EnumType"/> (with Members, UnderlyingType)</item>
///   <item>Generic type → type with TypeParameters list populated</item>
///   <item>Generic arity suffix (e.g., <c>`2</c>) stripped from DisplayName</item>
/// </list>
/// </para>
///
/// <para>
/// All projected symbols use <see cref="SymbolOrigin.Backend"/>.
/// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 10.1, 10.2, 10.3, 10.4, 10.5.
/// </para>
/// </summary>
internal sealed class TypeProjector
{
    private readonly IntrinsicTypeMap _intrinsicMap;
    private readonly VisibilityFilter _filter;

    /// <summary>
    /// Tracks types currently being projected to detect and break recursive cycles
    /// in generic constraint projection (e.g., <c>IComparable&lt;T&gt;</c> where T
    /// has a constraint referencing <c>IComparable&lt;T&gt;</c>).
    /// </summary>
    [ThreadStatic]
    private static HashSet<Type>? _projectionStack;

    public TypeProjector(IntrinsicTypeMap intrinsicMap, VisibilityFilter filter)
    {
        _intrinsicMap = intrinsicMap;
        _filter = filter;
    }

    /// <summary>
    /// Projects a CLR type into a Mist <see cref="IType"/> with its member symbols.
    /// Applies intrinsic substitution and visibility filtering.
    /// </summary>
    /// <param name="clrType">The CLR type to project.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    /// <returns>
    /// A tuple of the projected Mist type and an array of member symbols.
    /// Member symbols are populated for the type shell only (type parameters, base type,
    /// interfaces); full member projection (methods, properties, fields) is handled
    /// separately by the member projection phase (Task 4.4).
    /// </returns>
    public (IType Type, ImmutableArray<ISymbol> Members) ProjectType(
        Type clrType,
        IStdlibTypeProvider stdlib)
    {
        var displayName = StripGenericAritySuffix(clrType.Name);
        var containingNamespace = clrType.Namespace;

        if (clrType.IsEnum)
        {
            return ProjectEnum(clrType, displayName, containingNamespace, stdlib);
        }

        if (clrType.IsInterface)
        {
            return ProjectInterface(clrType, displayName, containingNamespace, stdlib);
        }

        if (clrType.IsValueType)
        {
            return ProjectStruct(clrType, displayName, containingNamespace, stdlib);
        }

        // Default: class
        return ProjectClass(clrType, displayName, containingNamespace, stdlib);
    }

    /// <summary>
    /// Projects a CLR enum into a Mist <see cref="EnumType"/> with members and underlying type.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectEnum(
        Type clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var enumType = new EnumType(displayName, memberScope, containingNamespace);

        // Project underlying type
        var underlyingClrType = Enum.GetUnderlyingType(clrType);
        var underlyingMistType = ProjectTypeReference(underlyingClrType, stdlib);
        enumType.UnderlyingType = underlyingMistType;

        // Project enum members
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Static);
        var members = new List<EnumMemberInfo>(fields.Length);
        var symbols = ImmutableArray.CreateBuilder<ISymbol>();

        foreach (var field in fields)
        {
            var memberInfo = new EnumMemberInfo(field.Name);
            memberInfo.ResolvedValue = field.GetRawConstantValue();
            memberInfo.ValueType = underlyingMistType;
            members.Add(memberInfo);

            var symbol = new Symbol(
                field.Name,
                SymbolKind.Field,
                SymbolOrigin.Backend,
                memberScope,
                enumType,
                null);
            symbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
            memberScope.AddMember(symbol);
            symbols.Add(symbol);
        }

        enumType.Members = members;

        return (enumType, symbols.ToImmutable());
    }

    /// <summary>
    /// Projects a CLR interface into a Mist <see cref="InterfaceType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectInterface(
        Type clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var interfaceType = new InterfaceType(displayName, memberScope, containingNamespace);

        // Project type parameters for generic interfaces
        if (clrType.IsGenericTypeDefinition)
        {
            interfaceType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project implemented interfaces
        interfaceType.Interfaces = ProjectInterfaces(clrType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, interfaceType, stdlib);

        return (interfaceType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Projects a CLR struct (value type) into a Mist <see cref="StructType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectStruct(
        Type clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var structType = new StructType(displayName, memberScope, containingNamespace);

        if (clrType.IsByRefLike)
            structType.IsRefStruct = true;

        // Project type parameters for generic structs
        if (clrType.IsGenericTypeDefinition)
        {
            structType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project implemented interfaces
        structType.Interfaces = ProjectInterfaces(clrType, stdlib);

        // Synthesize IEnumerable<T> for types that duck-type to the enumerable pattern
        // but don't explicitly implement IEnumerable<T> (e.g., Span<T>)
        SynthesizeDuckTypedIEnumerable(clrType, structType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, structType, stdlib);

        return (structType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Projects a CLR class into a Mist <see cref="ClassType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectClass(
        Type clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var classType = new ClassType(displayName, memberScope, containingNamespace);

        // Record abstract/sealed modifiers for validation (Requirements 13.4, 13.5).
        classType.IsAbstract = clrType.IsAbstract;
        classType.IsSealed = clrType.IsSealed;

        // Project type parameters for generic classes
        if (clrType.IsGenericTypeDefinition)
        {
            classType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project base type (only if not System.Object)
        if (clrType.BaseType is not null && clrType.BaseType != typeof(object))
        {
            classType.BaseType = ProjectTypeReference(clrType.BaseType, stdlib);
        }

        // Project implemented interfaces
        classType.Interfaces = ProjectInterfaces(clrType, stdlib);

        // Synthesize IEnumerable<T> for types that duck-type to the enumerable pattern
        // but don't explicitly implement IEnumerable<T>
        SynthesizeDuckTypedIEnumerable(clrType, classType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, classType, stdlib);

        return (classType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Projects the generic type parameters of a CLR generic type definition
    /// into Mist <see cref="TypeParameterType"/> instances with constraints.
    /// </summary>
    private IReadOnlyList<IType> ProjectTypeParameters(Type clrType, IStdlibTypeProvider stdlib)
    {
        var genericArgs = clrType.GetGenericArguments();
        var typeParams = new IType[genericArgs.Length];

        for (var i = 0; i < genericArgs.Length; i++)
        {
            var clrParam = genericArgs[i];
            var typeParam = new TypeParameterType(clrParam.Name, i);
            typeParam.Constraints = ProjectConstraints(clrParam, stdlib);
            typeParams[i] = typeParam;
        }

        return typeParams;
    }

    /// <summary>
    /// Translates CLR generic parameter constraints into Mist <see cref="TypeParameterConstraints"/>.
    /// <list type="bullet">
    ///   <item><c>class</c> → HasClassConstraint</item>
    ///   <item><c>struct</c> → HasStructConstraint</item>
    ///   <item><c>new()</c> → constructor constraint (empty parameter list)</item>
    ///   <item>Base type/interface → projected Mist type</item>
    /// </list>
    /// Constraints with no Mist equivalent are omitted.
    /// </summary>
    private TypeParameterConstraints? ProjectConstraints(Type clrParam, IStdlibTypeProvider stdlib)
    {
        var attributes = clrParam.GenericParameterAttributes;

        var hasClassConstraint = (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
        var hasStructConstraint = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
        var hasNewConstraint = (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

        var constraints = clrParam.GetGenericParameterConstraints();
        IType? baseClassConstraint = null;
        var interfaceConstraints = new List<IType>();

        foreach (var constraint in constraints)
        {
            // Skip System.ValueType which is the implicit base for struct constraint
            if (constraint == typeof(ValueType))
                continue;

            var projectedConstraint = ProjectTypeReference(constraint, stdlib);

            if (constraint.IsInterface)
            {
                interfaceConstraints.Add(projectedConstraint);
            }
            else
            {
                baseClassConstraint = projectedConstraint;
            }
        }

        // Build constructor constraints: new() → empty parameter list
        var constructorConstraints = new List<IReadOnlyList<IType>>();
        if (hasNewConstraint && !hasStructConstraint)
        {
            // new() constraint (struct implicitly has new(), so only add if not struct)
            constructorConstraints.Add(Array.Empty<IType>());
        }

        // If no constraints at all, return null (unconstrained)
        if (!hasClassConstraint && !hasStructConstraint &&
            baseClassConstraint is null && interfaceConstraints.Count == 0 &&
            constructorConstraints.Count == 0)
        {
            return null;
        }

        return new TypeParameterConstraints
        {
            HasClassConstraint = hasClassConstraint,
            HasStructConstraint = hasStructConstraint,
            BaseClassConstraint = baseClassConstraint,
            InterfaceConstraints = interfaceConstraints,
            ConstructorConstraints = constructorConstraints,
        };
    }

    /// <summary>
    /// Projects the interfaces implemented by a CLR type into Mist type references.
    /// Only includes directly declared interfaces (not inherited ones).
    /// Filters out the non-generic <c>System.Collections.IEnumerable</c> interface
    /// (Requirement 18.1).
    /// </summary>
    private IReadOnlyList<IType> ProjectInterfaces(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var interfaces = clrType.GetInterfaces();
        if (interfaces.Length == 0)
            return Array.Empty<IType>();

        // Get interfaces declared by parent to exclude inherited ones
        var parentInterfaces = new HashSet<ClrType>();
        if (clrType.BaseType is not null)
        {
            foreach (var parentIface in clrType.BaseType.GetInterfaces())
            {
                parentInterfaces.Add(parentIface);
            }
        }

        var result = new List<IType>();
        foreach (var iface in interfaces)
        {
            // Skip interfaces inherited from base type
            if (parentInterfaces.Contains(iface))
                continue;

            // Skip the non-generic System.Collections.IEnumerable (Requirement 18.1)
            if (IsNonGenericIEnumerable(iface))
                continue;

            // Skip excluded interfaces
            var ifaceToCheck = iface.IsConstructedGenericType
                ? iface.GetGenericTypeDefinition()
                : iface;
            if (_filter.IsTypeExcluded(ifaceToCheck))
                continue;

            result.Add(ProjectTypeReference(iface, stdlib));
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> if the given CLR type is the non-generic
    /// <c>System.Collections.IEnumerable</c> interface.
    /// </summary>
    private static bool IsNonGenericIEnumerable(Type type)
    {
        return type.Namespace == "System.Collections"
            && type.Name == "IEnumerable"
            && !type.IsGenericType;
    }

    /// <summary>
    /// Detects all <c>IEnumerable&lt;T&gt;</c> implementations on a CLR type and
    /// synthesizes the <c>Enumerator</c> associated type slot for each one.
    /// The slot is added to the type's member scope.
    ///
    /// <para>
    /// The <c>Enumerator</c> slot is determined by inspecting the CLR type's public
    /// <c>GetEnumerator()</c> method return type:
    /// <list type="bullet">
    ///   <item>If the return type is a concrete type (e.g., <c>List&lt;T&gt;.Enumerator</c>)
    ///         → use that concrete type.</item>
    ///   <item>If the return type is <c>IEnumerator&lt;T&gt;</c> → use
    ///         <c>Standard.IEnumerator&lt;TElement&gt;</c>.</item>
    /// </list>
    /// </para>
    ///
    /// <para>Requirements: 18.1, 18.2, 18.3, 18.4, 18.6.</para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect for IEnumerable implementations.</param>
    /// <param name="projectedType">The already-projected Mist type whose member scope will receive the slot.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    internal void SynthesizeEnumeratorSlots(
        Type clrType,
        IType projectedType,
        IStdlibTypeProvider stdlib)
    {
        var enumerableImpls = GetGenericIEnumerableImplementations(clrType);
        if (enumerableImpls.Count == 0)
            return;

        // Extract the member scope and namespace from the concrete projected type
        var (memberScope, containingNamespace) = projectedType switch
        {
            ClassType ct => (ct.MemberScope as Scope, ct.ContainingNamespace),
            StructType st => (st.MemberScope as Scope, st.ContainingNamespace),
            InterfaceType it => (it.MemberScope as Scope, it.ContainingNamespace),
            _ => (null, (string?)null),
        };

        if (memberScope is null)
            return;

        foreach (var enumerableImpl in enumerableImpls)
        {
            var enumeratorType = ResolveEnumeratorType(clrType, enumerableImpl, stdlib);
            if (enumeratorType is null)
                continue;

            // Create the AssociatedTypeSlotType for the Enumerator slot
            var elementTypeArg = enumerableImpl.GetGenericArguments()[0];
            var projectedElementType = ProjectTypeReference(elementTypeArg, stdlib);
            projectedElementType = _intrinsicMap.SubstituteIntrinsics(projectedElementType, stdlib);

            // Build the constraint: Enumerator must implement IEnumerator<TElement>
            // If the stdlib doesn't have IEnumerator yet (fallback provider before
            // Pass 1 completes), skip slot synthesis — it will be synthesized when
            // the type is re-resolved after the stdlib provider is upgraded.
            IType iEnumeratorStdlib;
            try
            {
                iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
            }
            catch (InvalidOperationException)
            {
                // FallbackStdlibTypeProvider doesn't have IEnumerator.
                // Skip slot synthesis; it will be available after UpdateStdlibProvider.
                continue;
            }

            var constraint = new TypeParameterConstraints
            {
                InterfaceConstraints = new List<IType>
                {
                    new ConstructedType(iEnumeratorStdlib, new[] { projectedElementType }),
                },
            };

            var slotType = new AssociatedTypeSlotType(
                "Enumerator",
                new Scope(null),
                containingNamespace,
                projectedType,
                constraint,
                Array.Empty<IType>());

            // Register the slot symbol in the type's member scope
            var slotSymbol = new Symbol(
                "Enumerator",
                SymbolKind.TypeAlias,
                SymbolOrigin.Backend,
                memberScope,
                slotType,
                null);
            slotSymbol.TypeParameters = Array.Empty<IType>();

            // Store the resolved concrete enumerator type as metadata on the slot
            // so downstream resolution can use it
            slotType.Constraints = new TypeParameterConstraints
            {
                InterfaceConstraints = new List<IType>
                {
                    new ConstructedType(iEnumeratorStdlib, new[] { projectedElementType }),
                },
                // Use BaseClassConstraint to store the resolved concrete type
                BaseClassConstraint = enumeratorType,
            };

            memberScope.AddMember(slotSymbol);
        }
    }

    /// <summary>
    /// For ambient types that do NOT already implement <c>IEnumerable&lt;T&gt;</c>,
    /// checks whether the type duck-types to the enumerable pattern:
    /// <list type="bullet">
    ///   <item>Has a public parameterless <c>GetEnumerator()</c> method</item>
    ///   <item>The return type of <c>GetEnumerator()</c> has a public <c>bool MoveNext()</c> method</item>
    ///   <item>The return type of <c>GetEnumerator()</c> has a public <c>Current</c> property</item>
    /// </list>
    ///
    /// <para>
    /// When all conditions are met, synthesizes <c>IEnumerable&lt;TElement&gt;</c> in the
    /// type's interface list (where <c>TElement</c> is resolved from <c>Current</c>'s return type)
    /// and synthesizes the <c>Enumerator</c> associated type slot.
    /// </para>
    ///
    /// <para>Requirements: 10.1, 10.2, 10.3, 10.6.</para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect for duck-typed enumerable pattern.</param>
    /// <param name="projectedType">The already-projected Mist type whose interface list and member scope may be updated.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    internal void SynthesizeDuckTypedIEnumerable(
        Type clrType,
        IType projectedType,
        IStdlibTypeProvider stdlib)
    {
        // Only applies if the type does NOT already implement IEnumerable<T>
        var enumerableImpls = GetGenericIEnumerableImplementations(clrType);
        if (enumerableImpls.Count > 0)
            return;

        // Guard against double-synthesis: if the Mist type already has IEnumerable
        // in its interface list (from a previous call), skip.
        var existingInterfaces = projectedType switch
        {
            StructType st => st.Interfaces,
            ClassType ct => ct.Interfaces,
            _ => (IReadOnlyList<IType>)Array.Empty<IType>(),
        };
        if (existingInterfaces.Any(i => i is ConstructedType ci
            && ci.GenericDefinition is InterfaceType iface
            && iface.DisplayName == "IEnumerable"))
            return;

        // Look for a public parameterless GetEnumerator() method
        var getEnumeratorMethod = clrType.GetMethod(
            "GetEnumerator",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);

        if (getEnumeratorMethod is null)
            return;

        var enumeratorClrType = getEnumeratorMethod.ReturnType;

        // Verify the enumerator type has a public bool MoveNext() method
        var moveNextMethod = enumeratorClrType.GetMethod(
            "MoveNext",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);

        if (moveNextMethod is null || moveNextMethod.ReturnType.FullName != "System.Boolean")
            return;

        // Verify the enumerator type has a public Current property
        var currentProperty = enumeratorClrType.GetProperty(
            "Current",
            BindingFlags.Public | BindingFlags.Instance);

        if (currentProperty is null)
            return;

        // Resolve TElement from Current's return type
        // Unwrap by-ref wrapper (e.g., Span<T>.Enumerator.Current returns T&)
        var elementClrType = currentProperty.PropertyType;
        if (elementClrType.IsByRef)
            elementClrType = elementClrType.GetElementType()!;
        var projectedElementType = ProjectTypeReference(elementClrType, stdlib);
        projectedElementType = _intrinsicMap.SubstituteIntrinsics(projectedElementType, stdlib);

        // Look up Standard.IEnumerable to synthesize IEnumerable<TElement>
        IType iEnumerableStdlib;
        try
        {
            iEnumerableStdlib = stdlib.LookupStandardType("IEnumerable");
        }
        catch (InvalidOperationException)
        {
            // Stdlib not loaded yet — skip synthesis.
            return;
        }

        var synthesizedIEnumerable = new ConstructedType(iEnumerableStdlib, new[] { projectedElementType });

        // Add the synthesized IEnumerable<TElement> to the type's interface list
        switch (projectedType)
        {
            case StructType st:
                var stInterfaces = new List<IType>(st.Interfaces) { synthesizedIEnumerable };
                st.Interfaces = stInterfaces;
                break;
            case ClassType ct:
                var ctInterfaces = new List<IType>(ct.Interfaces) { synthesizedIEnumerable };
                ct.Interfaces = ctInterfaces;
                break;
            default:
                return;
        }

        // Synthesize the Enumerator associated type slot (same logic as SynthesizeEnumeratorSlots)
        var (memberScope, containingNamespace) = projectedType switch
        {
            ClassType ct => (ct.MemberScope as Scope, ct.ContainingNamespace),
            StructType st => (st.MemberScope as Scope, st.ContainingNamespace),
            _ => (null, (string?)null),
        };

        if (memberScope is null)
            return;

        // Resolve the enumerator type for the slot
        var projectedEnumeratorType = ProjectTypeReference(enumeratorClrType, stdlib);

        // Build the constraint: Enumerator must implement IEnumerator<TElement>
        IType iEnumeratorStdlib;
        try
        {
            iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var constraint = new TypeParameterConstraints
        {
            InterfaceConstraints = new List<IType>
            {
                new ConstructedType(iEnumeratorStdlib, new[] { projectedElementType }),
            },
        };

        var slotType = new AssociatedTypeSlotType(
            "Enumerator",
            new Scope(null),
            containingNamespace,
            projectedType,
            constraint,
            Array.Empty<IType>());

        var slotSymbol = new Symbol(
            "Enumerator",
            SymbolKind.TypeAlias,
            SymbolOrigin.Backend,
            memberScope,
            slotType,
            null);
        slotSymbol.TypeParameters = Array.Empty<IType>();

        // Store the resolved concrete enumerator type as metadata on the slot
        slotType.Constraints = new TypeParameterConstraints
        {
            InterfaceConstraints = new List<IType>
            {
                new ConstructedType(iEnumeratorStdlib, new[] { projectedElementType }),
            },
            BaseClassConstraint = projectedEnumeratorType,
        };

        memberScope.AddMember(slotSymbol);
    }

    /// <summary>
    /// Returns all <c>System.Collections.Generic.IEnumerable&lt;T&gt;</c> implementations
    /// on the given CLR type (handles multiple implementations per Requirement 18.6).
    /// </summary>
    private static List<Type> GetGenericIEnumerableImplementations(Type clrType)
    {
        var result = new List<Type>();
        foreach (var iface in clrType.GetInterfaces())
        {
            if (iface.IsConstructedGenericType
                && iface.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IEnumerable`1")
            {
                result.Add(iface);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the <c>Enumerator</c> type for a CLR type implementing
    /// <c>IEnumerable&lt;T&gt;</c> by inspecting the <c>GetEnumerator()</c> method.
    ///
    /// <list type="bullet">
    ///   <item>If the return type is a concrete type → projects and returns it.</item>
    ///   <item>If the return type is <c>IEnumerator&lt;T&gt;</c> → returns
    ///         <c>Standard.IEnumerator&lt;TElement&gt;</c>.</item>
    /// </list>
    /// </summary>
    private IType? ResolveEnumeratorType(
        Type clrType,
        Type enumerableImpl,
        IStdlibTypeProvider stdlib)
    {
        var elementClrType = enumerableImpl.GetGenericArguments()[0];

        // Look for a public parameterless GetEnumerator() method
        var getEnumeratorMethod = clrType.GetMethod(
            "GetEnumerator",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null);

        if (getEnumeratorMethod is null)
        {
            // Fall back: use Standard.IEnumerator<TElement>
            return BuildStandardIEnumeratorType(elementClrType, stdlib);
        }

        var returnType = getEnumeratorMethod.ReturnType;

        // Check if the return type is IEnumerator<T> (the interface itself)
        if (returnType.IsInterface
            && returnType.IsConstructedGenericType
            && returnType.GetGenericTypeDefinition().FullName == "System.Collections.Generic.IEnumerator`1")
        {
            return BuildStandardIEnumeratorType(elementClrType, stdlib);
        }

        // The return type is a concrete type (e.g., List<T>.Enumerator)
        // Project it as a Mist type
        return ProjectTypeReference(returnType, stdlib);
    }

    /// <summary>
    /// Builds a <c>Standard.IEnumerator&lt;TElement&gt;</c> constructed type
    /// for the given CLR element type. Returns null if the stdlib doesn't have
    /// IEnumerator yet (fallback provider before Pass 1 completes).
    /// </summary>
    private IType? BuildStandardIEnumeratorType(Type elementClrType, IStdlibTypeProvider stdlib)
    {
        var projectedElement = ProjectTypeReference(elementClrType, stdlib);
        projectedElement = _intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);

        IType iEnumeratorStdlib;
        try
        {
            iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        return new ConstructedType(iEnumeratorStdlib, new[] { projectedElement });
    }

    /// <summary>
    /// Projects CLR operator methods (binary, comparison, unary, and cast operators) on a type
    /// into Mist operator symbols and adds them to the type's member scope.
    ///
    /// <para>
    /// CLR operator method name mapping:
    /// <list type="bullet">
    ///   <item><c>op_Addition</c> → <c>operator+</c> (binary)</item>
    ///   <item><c>op_Subtraction</c> → <c>operator-</c> (binary)</item>
    ///   <item><c>op_Multiply</c> → <c>operator*</c> (binary)</item>
    ///   <item><c>op_Division</c> → <c>operator/</c> (binary)</item>
    ///   <item><c>op_Modulus</c> → <c>operator%</c> (binary)</item>
    ///   <item><c>op_Equality</c> → <c>operator==</c> (comparison)</item>
    ///   <item><c>op_Inequality</c> → <c>operator!=</c> (comparison)</item>
    ///   <item><c>op_LessThan</c> → <c>operator&lt;</c> (comparison)</item>
    ///   <item><c>op_GreaterThan</c> → <c>operator&gt;</c> (comparison)</item>
    ///   <item><c>op_LessThanOrEqual</c> → <c>operator&lt;=</c> (comparison)</item>
    ///   <item><c>op_GreaterThanOrEqual</c> → <c>operator&gt;=</c> (comparison)</item>
    ///   <item><c>op_UnaryNegation</c> → <c>operator-</c> (unary)</item>
    ///   <item><c>op_UnaryPlus</c> → <c>operator+</c> (unary)</item>
    ///   <item><c>op_Implicit</c> → <c>operator_implicit_{TargetType}</c> (cast)</item>
    ///   <item><c>op_Explicit</c> → <c>operator_explicit_{TargetType}</c> (cast)</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Operand types and return types are projected through intrinsic substitution.
    /// All projected operator symbols use <see cref="SymbolOrigin.Backend"/>.
    /// </para>
    ///
    /// <para>Requirements: 12.1, 12.2, 12.3, 12.4.</para>
    /// </summary>
    /// <param name="clrType">The CLR type whose operator methods to project.</param>
    /// <param name="memberScope">The Mist type's member scope to add operator symbols to.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    /// <returns>The projected operator symbols.</returns>
    internal ImmutableArray<ISymbol> ProjectOperators(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib)
    {
        var operators = ImmutableArray.CreateBuilder<ISymbol>();

        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (!method.IsSpecialName)
                continue;

            // Skip operators excluded by the visibility filter
            if (_filter.IsMemberExcluded(method))
                continue;

            var mistOpName = MapClrOperatorName(method);
            if (mistOpName is null)
                continue;

            // Project the return type through intrinsic substitution
            var returnType = ProjectTypeReference(method.ReturnType, stdlib);
            returnType = _intrinsicMap.SubstituteIntrinsics(returnType, stdlib);

            // Create the operator symbol
            var opSymbol = new Symbol(
                mistOpName,
                SymbolKind.Method,
                SymbolOrigin.Backend,
                memberScope,
                returnType,
                null);

            // Project parameters and register them in a method scope
            var parameters = method.GetParameters();
            var methodScope = new Scope(parent: memberScope);
            foreach (var param in parameters)
            {
                var paramType = ProjectTypeReference(param.ParameterType, stdlib);
                paramType = _intrinsicMap.SubstituteIntrinsics(paramType, stdlib);

                var paramSymbol = new Symbol(
                    param.Name ?? $"arg{param.Position}",
                    SymbolKind.Parameter,
                    SymbolOrigin.Backend,
                    methodScope,
                    paramType,
                    null);
                methodScope.AddMember(paramSymbol);
            }

            memberScope.AddMember(opSymbol);
            operators.Add(opSymbol);
        }

        return operators.ToImmutable();
    }

    /// <summary>
    /// Maps a CLR operator method to its Mist operator symbol name.
    /// Returns <c>null</c> if the method is not a recognized operator.
    /// </summary>
    private static string? MapClrOperatorName(MethodInfo method)
    {
        var clrName = method.Name;

        // Binary arithmetic operators (Requirement 12.1)
        // Comparison operators (Requirement 12.2)
        // Unary operators (Requirement 12.4)
        if (BinaryAndUnaryOperatorMap.TryGetValue(clrName, out var mistSymbol))
            return "operator" + mistSymbol;

        // Cast/conversion operators (Requirement 12.3)
        if (clrName is "op_Implicit" or "op_Explicit")
        {
            var kind = clrName == "op_Implicit" ? "implicit" : "explicit";
            var targetTypeName = StripGenericAritySuffix(method.ReturnType.Name);
            return $"operator_{kind}_{targetTypeName}";
        }

        return null;
    }

    /// <summary>
    /// Maps CLR operator method names to their Mist operator symbol lexemes.
    /// Covers binary arithmetic (Req 12.1), comparison (Req 12.2), and unary (Req 12.4) operators.
    /// </summary>
    private static readonly Dictionary<string, string> BinaryAndUnaryOperatorMap = new()
    {
        // Binary arithmetic operators (Requirement 12.1)
        ["op_Addition"] = "+",
        ["op_Subtraction"] = "-",
        ["op_Multiply"] = "*",
        ["op_Division"] = "/",
        ["op_Modulus"] = "%",

        // Comparison operators (Requirement 12.2)
        ["op_Equality"] = "==",
        ["op_Inequality"] = "!=",
        ["op_LessThan"] = "<",
        ["op_GreaterThan"] = ">",
        ["op_LessThanOrEqual"] = "<=",
        ["op_GreaterThanOrEqual"] = ">=",

        // Unary operators (Requirement 12.4)
        ["op_UnaryNegation"] = "-",
        ["op_UnaryPlus"] = "+",
    };

    /// <summary>
    /// Projects a CLR type reference (used in signatures, base types, interfaces, constraints)
    /// into a Mist <see cref="IType"/>. Handles generic constructed types, generic parameters,
    /// and applies intrinsic substitution.
    /// </summary>
    internal IType ProjectTypeReference(Type clrType, IStdlibTypeProvider stdlib)
    {
        // Handle generic type parameters (T, TKey, etc.)
        if (clrType.IsGenericParameter)
        {
            return new TypeParameterType(clrType.Name, clrType.GenericParameterPosition);
        }

        // Project the well-known .NET delegate families to the Mist Func type family
        // (Requirements 15.1, 15.2):
        //   System.Func<T1, ..., TN, TResult> → Standard.Func<T1, ..., TN, TResult>
        //   System.Action<T1, ..., TN>        → Standard.Func<T1, ..., TN, Void>
        //   System.Action (non-generic)       → Standard.Func<Void>
        // Done before the constructed-generic / simple-type branches so the delegate
        // types are never projected as plain CLR classes.
        if (TryProjectStandardFuncDelegate(clrType, stdlib, out var funcType))
        {
            return funcType;
        }

        // Project named delegate types (Predicate<T>, Comparison<T>, EventHandler, etc.)
        // to their equivalent Standard.Func type based on the delegate's Invoke signature
        // (Requirements 15.3, 15.6).
        if (TryProjectNamedDelegate(clrType, stdlib, out var namedDelegateType))
        {
            return namedDelegateType;
        }

        // Project inbound CLR System.Span`1 / System.ReadOnlySpan`1 to the dedicated
        // SpanType with an unknown Length, mirroring the inbound-array arm
        // (clrType.IsArray → new ArrayType(projectedElement), which projects to an
        // unknown-length array). The Mist Span<T, Length> is arity-2 while the CLR
        // System.Span`1 is arity-1 — the same mismatch Array<T, Length> ↔ T[] handles —
        // so no Length argument is fabricated; the projected SpanType is always
        // unknown-length. Done before the constructed-generic branch so the spans are
        // never projected as plain ConstructedType<StructType>. (Requirements 7.3, 9.1)
        if (TryProjectClrSpan(clrType, stdlib, out var spanType))
        {
            return spanType;
        }

        // Handle constructed generic types (e.g., List<int>, Dictionary<string, int>)
        if (clrType.IsConstructedGenericType)
        {
            return ProjectConstructedGenericType(clrType, stdlib);
        }

        // Handle generic type definitions (e.g., List<T> when used as a type reference)
        if (clrType.IsGenericTypeDefinition)
        {
            return ProjectGenericDefinitionReference(clrType, stdlib);
        }

        // Handle arrays
        if (clrType.IsArray)
        {
            var elementType = clrType.GetElementType()!;
            var projectedElement = ProjectTypeReference(elementType, stdlib);
            // Apply intrinsic substitution to the element type
            projectedElement = _intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);
            return new ArrayType(projectedElement);
        }

        // Project as a simple type and apply intrinsic substitution
        var projectedType = ProjectSimpleTypeReference(clrType, stdlib);
        return _intrinsicMap.SubstituteIntrinsics(projectedType, stdlib);
    }

    /// <summary>
    /// Projects a constructed generic type (e.g., <c>List&lt;int&gt;</c>) into a Mist
    /// <see cref="ConstructedType"/> with recursively projected type arguments.
    /// </summary>
    private IType ProjectConstructedGenericType(Type clrType, IStdlibTypeProvider stdlib)
    {
        var genericDef = clrType.GetGenericTypeDefinition();
        var projectedDef = ProjectTypeReference(genericDef, stdlib);

        var typeArgs = clrType.GetGenericArguments();
        var projectedArgs = new IType[typeArgs.Length];
        for (var i = 0; i < typeArgs.Length; i++)
        {
            var projectedArg = ProjectTypeReference(typeArgs[i], stdlib);
            projectedArgs[i] = _intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
        }

        // Apply intrinsic substitution to the definition itself
        projectedDef = _intrinsicMap.SubstituteIntrinsics(projectedDef, stdlib);

        return new ConstructedType(projectedDef, projectedArgs);
    }

    /// <summary>
    /// Attempts to project an inbound CLR <c>System.Span&lt;T&gt;</c> (<c>System.Span`1</c>)
    /// or <c>System.ReadOnlySpan&lt;T&gt;</c> (<c>System.ReadOnlySpan`1</c>) into the dedicated
    /// Mist <see cref="SpanType"/> with an unknown <c>Length</c>.
    ///
    /// <para>
    /// This mirrors the inbound-array arm (<c>clrType.IsArray → new ArrayType(projectedElement)</c>),
    /// which projects a CLR array to an unknown-length <see cref="ArrayType"/>. The Mist
    /// <c>Span&lt;T, Length&gt;</c> is arity-2 while the CLR <c>System.Span`1</c> is arity-1 — the
    /// same arity mismatch <c>Array&lt;T, Length&gt;</c> ↔ <c>T[]</c> already handles — so the
    /// single CLR generic argument is the element type and no <c>Length</c> is fabricated;
    /// the projected <see cref="SpanType"/> is always unknown-length (Requirement 7.3).
    /// </para>
    ///
    /// <para>
    /// All other CLR ref structs (<c>IsByRefLike</c>) are left untouched and continue to flow
    /// through <see cref="ProjectStruct"/>'s <c>IsByRefLike → IsRefStruct</c> path (Requirement 9.1).
    /// </para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect.</param>
    /// <param name="stdlib">The stdlib type provider used to project the element type.</param>
    /// <param name="spanType">The projected unknown-length <see cref="SpanType"/>, when matched.</param>
    /// <returns><c>true</c> when <paramref name="clrType"/> is a CLR span; otherwise <c>false</c>.</returns>
    private bool TryProjectClrSpan(Type clrType, IStdlibTypeProvider stdlib, out IType spanType)
    {
        spanType = null!;

        if (!clrType.IsConstructedGenericType)
            return false;

        var genericDefName = clrType.GetGenericTypeDefinition().FullName;
        bool isReadOnly;
        if (genericDefName == "System.Span`1")
        {
            isReadOnly = false;
        }
        else if (genericDefName == "System.ReadOnlySpan`1")
        {
            isReadOnly = true;
        }
        else
        {
            return false;
        }

        var elementClrType = clrType.GetGenericArguments()[0];
        var projectedElement = ProjectTypeReference(elementClrType, stdlib);
        // Apply intrinsic substitution to the element type, mirroring the array arm.
        projectedElement = _intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);

        // Always unknown-length: no Length argument is fabricated (Requirement 7.3).
        spanType = new SpanType(projectedElement, isReadOnly);
        return true;
    }

    /// <summary>
    /// Attempts to project a well-known .NET delegate type from the <c>System</c>
    /// namespace into the Mist <c>Standard.Func</c> type family (Requirements 15.1, 15.2).
    ///
    /// <list type="bullet">
    ///   <item><c>System.Func&lt;T1, ..., TN, TResult&gt;</c> → <c>Standard.Func&lt;T1, ..., TN, TResult&gt;</c></item>
    ///   <item><c>System.Action&lt;T1, ..., TN&gt;</c> → <c>Standard.Func&lt;T1, ..., TN, Void&gt;</c></item>
    ///   <item><c>System.Action</c> (non-generic) → <c>Standard.Func&lt;Void&gt;</c></item>
    /// </list>
    ///
    /// <para>
    /// The resulting <see cref="ConstructedType"/> wraps the arity-matched
    /// <c>Standard.Func</c> definition looked up from the stdlib so that it is
    /// reference-equal to a <c>func&lt;...&gt;</c> / <c>Func&lt;...&gt;</c> type resolved
    /// from Mist source (structural equality of constructed types compares the
    /// generic definition by reference). When the stdlib has not yet loaded the
    /// <c>Func</c> family (e.g., the fallback provider before Pass 1 completes),
    /// projection is skipped and the caller falls back to the default CLR-class
    /// projection — the type is re-resolved after <c>UpdateStdlibProvider</c>.
    /// </para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect.</param>
    /// <param name="stdlib">The stdlib type provider used to resolve <c>Standard.Func</c>/<c>Void</c>.</param>
    /// <param name="funcType">The projected <c>Standard.Func</c> constructed type, when matched.</param>
    /// <returns><c>true</c> when <paramref name="clrType"/> is a projectable delegate; otherwise <c>false</c>.</returns>
    private bool TryProjectStandardFuncDelegate(
        Type clrType,
        IStdlibTypeProvider stdlib,
        out IType funcType)
    {
        funcType = null!;

        if (clrType.Namespace != "System")
            return false;

        // The Func type arguments, in Mist order (parameter types followed by the
        // return type). For Action, the synthesized return type is Void.
        IType[] funcTypeArguments;

        if (clrType.IsConstructedGenericType)
        {
            var genericName = clrType.GetGenericTypeDefinition().Name;
            var clrArgs = clrType.GetGenericArguments();

            if (genericName.StartsWith("Func`", StringComparison.Ordinal))
            {
                // System.Func<T1, ..., TN, TResult> → Standard.Func<T1, ..., TN, TResult>.
                // CLR Func already places the return type last, matching Mist's convention.
                funcTypeArguments = new IType[clrArgs.Length];
                for (var i = 0; i < clrArgs.Length; i++)
                {
                    var projectedArg = ProjectTypeReference(clrArgs[i], stdlib);
                    funcTypeArguments[i] = _intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
                }
            }
            else if (genericName.StartsWith("Action`", StringComparison.Ordinal))
            {
                // System.Action<T1, ..., TN> → Standard.Func<T1, ..., TN, Void>.
                // Append Void as the trailing return type.
                if (!TryLookupVoid(stdlib, out var voidType))
                    return false;

                funcTypeArguments = new IType[clrArgs.Length + 1];
                for (var i = 0; i < clrArgs.Length; i++)
                {
                    var projectedArg = ProjectTypeReference(clrArgs[i], stdlib);
                    funcTypeArguments[i] = _intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
                }
                funcTypeArguments[clrArgs.Length] = voidType;
            }
            else
            {
                return false;
            }
        }
        else if (clrType.Name == "Action" && !clrType.IsGenericType)
        {
            // System.Action (non-generic) → Standard.Func<Void>.
            if (!TryLookupVoid(stdlib, out var voidType))
                return false;

            funcTypeArguments = new[] { voidType };
        }
        else
        {
            return false;
        }

        // Look up the arity-matched Standard.Func definition. The arity is the number
        // of type arguments on the Mist Func (parameters + return type).
        IType funcDefinition;
        try
        {
            funcDefinition = stdlib.LookupStandardType("Func", funcTypeArguments.Length);
        }
        catch (InvalidOperationException)
        {
            // Stdlib doesn't have the Func family yet (fallback provider before Pass 1).
            // Skip projection; the type is re-resolved after UpdateStdlibProvider.
            return false;
        }

        // Guard: ensure we resolved the real Standard.Func family and not a fallback
        // (e.g., the fallback provider has no Func and would not reach here, but a
        // wrong-arity fallback would produce a structurally incorrect type).
        if (funcDefinition is not ClassType { DisplayName: "Func", ContainingNamespace: "Standard" })
            return false;

        funcType = new ConstructedType(funcDefinition, funcTypeArguments);
        return true;
    }

    /// <summary>
    /// Looks up the <c>Standard.Void</c> type from the stdlib provider, returning
    /// <c>false</c> when the stdlib has not been loaded (fallback provider).
    /// </summary>
    private static bool TryLookupVoid(IStdlibTypeProvider stdlib, out IType voidType)
    {
        try
        {
            voidType = stdlib.LookupStandardType("Void");
            return true;
        }
        catch (InvalidOperationException)
        {
            voidType = null!;
            return false;
        }
    }

    /// <summary>
    /// Attempts to project a named CLR delegate type (e.g., <c>Predicate&lt;T&gt;</c>,
    /// <c>Comparison&lt;T&gt;</c>, <c>EventHandler</c>, <c>Converter&lt;TInput, TOutput&gt;</c>)
    /// into the equivalent <c>Standard.Func</c> constructed type based on the delegate's
    /// <c>Invoke</c> method signature (Requirements 15.3, 15.6).
    ///
    /// <para>
    /// This handles all delegate types that are NOT <c>System.Func</c> or <c>System.Action</c>
    /// (those are handled by <see cref="TryProjectStandardFuncDelegate"/>). The projection
    /// inspects the delegate's <c>Invoke</c> method to determine parameter types and return
    /// type, then constructs the equivalent <c>Standard.Func</c> type. For void-returning
    /// delegates, the trailing type argument is <c>Standard.Void</c>.
    /// </para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect.</param>
    /// <param name="stdlib">The stdlib type provider used to resolve <c>Standard.Func</c>/<c>Void</c>.</param>
    /// <param name="funcType">The projected <c>Standard.Func</c> constructed type, when matched.</param>
    /// <returns><c>true</c> when <paramref name="clrType"/> is a projectable named delegate; otherwise <c>false</c>.</returns>
    private bool TryProjectNamedDelegate(
        Type clrType,
        IStdlibTypeProvider stdlib,
        out IType funcType)
    {
        funcType = null!;

        // All delegates inherit from System.MulticastDelegate.
        if (clrType.BaseType?.FullName != "System.MulticastDelegate")
            return false;

        // Get the Invoke method that defines the delegate's signature.
        var invokeMethod = clrType.GetMethod("Invoke");
        if (invokeMethod is null)
            return false;

        var parameters = invokeMethod.GetParameters();
        var returnType = invokeMethod.ReturnType;
        var isVoidReturn = returnType == typeof(void);

        // Arity = parameter count + 1 (for the return type or Void).
        var arity = parameters.Length + 1;

        // Look up the arity-matched Standard.Func definition.
        IType funcDefinition;
        try
        {
            funcDefinition = stdlib.LookupStandardType("Func", arity);
        }
        catch (InvalidOperationException)
        {
            // Stdlib doesn't have the Func family yet (fallback provider before Pass 1).
            return false;
        }

        // Guard: ensure we resolved the real Standard.Func family.
        if (funcDefinition is not ClassType { DisplayName: "Func", ContainingNamespace: "Standard" })
            return false;

        // Build the type arguments: parameter types followed by return type (or Void).
        var funcTypeArguments = new IType[arity];
        for (var i = 0; i < parameters.Length; i++)
        {
            var projectedArg = ProjectTypeReference(parameters[i].ParameterType, stdlib);
            funcTypeArguments[i] = _intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
        }

        if (isVoidReturn)
        {
            if (!TryLookupVoid(stdlib, out var voidType))
                return false;
            funcTypeArguments[parameters.Length] = voidType;
        }
        else
        {
            var projectedReturn = ProjectTypeReference(returnType, stdlib);
            funcTypeArguments[parameters.Length] = _intrinsicMap.SubstituteIntrinsics(projectedReturn, stdlib);
        }

        funcType = new ConstructedType(funcDefinition, funcTypeArguments);
        return true;
    }

    /// <summary>
    /// Projects a generic type definition used as a type reference (not a full projection).
    /// Creates a minimal type shell with the correct display name and namespace.
    /// </summary>
    private IType ProjectGenericDefinitionReference(Type clrType, IStdlibTypeProvider stdlib)
    {
        var displayName = StripGenericAritySuffix(clrType.Name);
        var containingNamespace = clrType.Namespace;

        // Cycle detection: if we're already projecting this type's constraints,
        // return a minimal shell without type parameters to break the recursion.
        var stack = _projectionStack ??= new HashSet<Type>(ReferenceEqualityComparer.Instance);
        if (!stack.Add(clrType))
        {
            // Already projecting this type — return a minimal shell to break the cycle.
            if (clrType.IsInterface)
                return new InterfaceType(displayName, new Scope(null), containingNamespace);
            if (clrType.IsValueType)
                return new StructType(displayName, new Scope(null), containingNamespace);
            return new ClassType(displayName, new Scope(null), containingNamespace);
        }

        try
        {
            if (clrType.IsInterface)
            {
                var iface = new InterfaceType(displayName, new Scope(null), containingNamespace);
                iface.TypeParameters = ProjectTypeParameters(clrType, stdlib);
                return iface;
            }

            if (clrType.IsValueType)
            {
                var structType = new StructType(displayName, new Scope(null), containingNamespace);
                structType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
                return structType;
            }

            var classType = new ClassType(displayName, new Scope(null), containingNamespace);
            classType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
            return classType;
        }
        finally
        {
            stack.Remove(clrType);
        }
    }

    /// <summary>
    /// Projects a simple (non-generic, non-array) CLR type reference into a Mist type.
    /// Creates a minimal type shell with the correct display name and namespace.
    /// </summary>
    private static IType ProjectSimpleTypeReference(Type clrType, IStdlibTypeProvider stdlib)
    {
        var displayName = clrType.Name;
        var containingNamespace = clrType.Namespace;

        if (clrType.IsInterface)
        {
            return new InterfaceType(displayName, new Scope(null), containingNamespace);
        }

        if (clrType.IsEnum)
        {
            return new EnumType(displayName, new Scope(null), containingNamespace);
        }

        if (clrType.IsValueType)
        {
            return new StructType(displayName, new Scope(null), containingNamespace);
        }

        return new ClassType(displayName, new Scope(null), containingNamespace);
    }

    /// <summary>
    /// Projects a CLR type appearing in a member signature (parameter type, return type,
    /// property type, field type) with nullable/reference-type wrapping applied.
    /// <list type="bullet">
    ///   <item><c>Nullable&lt;T&gt;</c> → <c>UnionType([T_mist, VoidType])</c> with <see cref="NullableOrigin.NullableValueType"/></item>
    ///   <item>Reference types (class, interface, string, array, delegate) → <c>UnionType([T_mist, VoidType])</c> with <see cref="NullableOrigin.ReferenceType"/></item>
    ///   <item>Non-nullable value types → plain Mist type (no union)</item>
    /// </list>
    /// <para>
    /// NRT annotations (<c>NullableAttribute</c>, <c>NullableContextAttribute</c>) are ignored —
    /// conservative projection ensures null safety at the Mist level.
    /// </para>
    /// <para>Requirements: 14.1, 14.2, 14.5, 14.6.</para>
    /// </summary>
    /// <param name="clrType">The CLR type from a member signature.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types (including Void).</param>
    /// <returns>
    /// The projected Mist type, wrapped in a <c>T | void</c> union for nullable value types
    /// and reference types, or the plain projected type for non-nullable value types.
    /// </returns>
    internal IType ProjectNullableType(Type clrType, IStdlibTypeProvider stdlib)
    {
        // Case 1: Nullable<T> (e.g., int?, bool?)
        var underlyingNullable = NullableReflection.GetUnderlyingType(clrType);
        if (underlyingNullable is not null)
        {
            var innerMistType = ProjectTypeReference(underlyingNullable, stdlib);
            innerMistType = _intrinsicMap.SubstituteIntrinsics(innerMistType, stdlib);
            return CreateNullableUnion(innerMistType, NullableOrigin.NullableValueType, stdlib);
        }

        // Case 2: Reference types (class, interface, string, array, delegate)
        if (IsReferenceType(clrType))
        {
            var projectedType = ProjectTypeReference(clrType, stdlib);
            return CreateNullableUnion(projectedType, NullableOrigin.ReferenceType, stdlib);
        }

        // Case 3: Non-nullable value type — plain projection, no union
        return ProjectTypeReference(clrType, stdlib);
    }

    /// <summary>
    /// Determines whether a CLR type is a reference type (class, interface, string, array, delegate).
    /// Generic type parameters are NOT treated as reference types here (they have their own
    /// constraint-based handling).
    /// </summary>
    private static bool IsReferenceType(Type clrType)
    {
        // Generic type parameters are handled separately — don't wrap them
        if (clrType.IsGenericParameter)
            return false;

        // Arrays are always reference types
        if (clrType.IsArray)
            return true;

        // Classes (including string, delegates) and interfaces are reference types.
        // Value types (structs, enums) are not.
        return clrType.IsClass || clrType.IsInterface;
    }

    /// <summary>
    /// Creates a <c>UnionType([innerType, VoidType])</c> with the specified nullable origin,
    /// attaching <see cref="NullableProjectionInfo"/> for emitter use.
    /// </summary>
    private static UnionType CreateNullableUnion(
        IType innerType,
        NullableOrigin origin,
        IStdlibTypeProvider stdlib)
    {
        var voidType = stdlib.LookupStandardType("Void");
        var union = new UnionType(new List<IType> { innerType, voidType })
        {
            NullableProjection = new NullableProjectionInfo(origin, innerType),
        };
        return union;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Member Projection (Task 4.4)
    // Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 5.7, 5.8, 5.9
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Projects the public members of a CLR type into Mist <see cref="ISymbol"/> instances.
    /// Applies <see cref="VisibilityFilter"/> to each member before projection and
    /// <see cref="IntrinsicTypeMap.SubstituteIntrinsics"/> to all types in signatures.
    /// </summary>
    /// <param name="clrType">The CLR type whose members to project.</param>
    /// <param name="projectedMistType">The already-projected Mist type (used as the declaring type for constructors).</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    /// <returns>An array of projected member symbols.</returns>
    public ImmutableArray<ISymbol> ProjectMembers(
        Type clrType,
        IType projectedMistType,
        IStdlibTypeProvider stdlib)
    {
        var symbols = ImmutableArray.CreateBuilder<ISymbol>();

        var memberScope = GetMemberScope(projectedMistType);
        if (memberScope is null)
            return symbols.ToImmutable();

        // Project constructors
        ProjectConstructors(clrType, projectedMistType, memberScope, stdlib, symbols);

        // Project methods (instance and static, excluding property accessors and operators)
        ProjectMethods(clrType, memberScope, stdlib, symbols);

        // Project properties (including distinguishing read-only vs read-write)
        ProjectProperties(clrType, memberScope, stdlib, symbols);

        // Project fields (readonly/const → read-only)
        ProjectFields(clrType, memberScope, stdlib, symbols);

        // Project indexers
        ProjectIndexers(clrType, memberScope, stdlib, symbols);

        return symbols.ToImmutable();
    }

    /// <summary>
    /// Projects public constructors of a CLR type as Mist constructor symbols.
    /// </summary>
    private void ProjectConstructors(
        Type clrType,
        IType declaringType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var constructors = clrType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach (var ctor in constructors)
        {
            if (_filter.IsMemberExcluded(ctor))
                continue;

            var ctorScope = new Scope(memberScope);
            var ctorSymbol = new Symbol(
                ".ctor",
                SymbolKind.Constructor,
                SymbolOrigin.Backend,
                memberScope,
                declaringType,
                null);

            // Project parameters into the constructor's scope and store on the symbol
            // for nullable emission wrapping in the emitter (Task 8.2).
            var parameterSymbols = ProjectParametersWithResult(ctor.GetParameters(), ctorScope, stdlib);
            ctorSymbol.Parameters = parameterSymbols;

            memberScope.AddMember(ctorSymbol);
            symbols.Add(ctorSymbol);
        }
    }

    /// <summary>
    /// Projects public instance and static methods of a CLR type as Mist method symbols.
    /// Excludes property accessors, event accessors, and operator methods (handled separately).
    /// </summary>
    private void ProjectMethods(
        Type clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (var method in methods)
        {
            // Skip property accessors, event accessors, and operators
            if (method.IsSpecialName)
                continue;

            if (_filter.IsMemberExcluded(method))
                continue;

            // Project return type — unwrap by-ref returns to get the element type.
            IType returnType;
            var isRefReturn = method.ReturnType.IsByRef;
            if (isRefReturn)
            {
                var elementType = method.ReturnType.GetElementType()!;
                returnType = ProjectMemberType(elementType, stdlib);
            }
            else
            {
                returnType = ProjectMemberType(method.ReturnType, stdlib);
            }

            var methodScope = new Scope(memberScope);

            var methodSymbol = new Symbol(
                method.Name,
                SymbolKind.Method,
                SymbolOrigin.Backend,
                memberScope,
                returnType,
                null);

            // Track whether this is a static method so instance member lookup
            // can skip it (static methods are only reachable via the type name).
            methodSymbol.IsStatic = method.IsStatic;

            // Mark mutable ref returns (Requirements 5.1, 5.2).
            if (isRefReturn)
            {
                methodSymbol.ReturnRefKind = RefKind.Ref;
            }

            // Project generic method type parameters
            if (method.IsGenericMethodDefinition)
            {
                methodSymbol.TypeParameters = ProjectMethodTypeParameters(method, stdlib);
            }

            // Project parameters into the method's scope and store on the symbol
            // for nullable emission wrapping in the emitter (Task 8.2).
            var parameterSymbols = ProjectParametersWithResult(method.GetParameters(), methodScope, stdlib);
            methodSymbol.Parameters = parameterSymbols;

            memberScope.AddMember(methodSymbol);
            symbols.Add(methodSymbol);
        }
    }

    /// <summary>
    /// Projects public properties of a CLR type as Mist property symbols.
    /// <list type="bullet">
    ///   <item>Get-only → read-only</item>
    ///   <item>Get + public set → read-write</item>
    ///   <item>Get + non-public set → read-only</item>
    /// </list>
    /// Indexers (parameterized properties) are handled separately by <see cref="ProjectIndexers"/>.
    /// </summary>
    private void ProjectProperties(
        Type clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (var property in properties)
        {
            // Skip indexers — they are projected separately
            if (property.GetIndexParameters().Length > 0)
                continue;

            if (_filter.IsMemberExcluded(property))
                continue;

            var propertyType = ProjectMemberType(property.PropertyType, stdlib);

            var propertySymbol = new Symbol(
                property.Name,
                SymbolKind.Property,
                SymbolOrigin.Backend,
                memberScope,
                propertyType,
                null);

            // Determine read-only vs read-write
            var hasPublicGetter = property.GetGetMethod() is not null;
            var setMethod = property.GetSetMethod();
            var hasPublicSetter = setMethod is not null && setMethod.IsPublic;

            propertySymbol.PropertyMetadata = new Semantics.Contracts.PropertyInfo
            {
                HasGet = hasPublicGetter,
                HasSet = hasPublicSetter,
                IsStatic = property.GetGetMethod()?.IsStatic ?? property.GetSetMethod()?.IsStatic ?? false,
                ReadonlyStatus = hasPublicSetter ? ReadonlyStatus.Mutable : ReadonlyStatus.ExplicitReadonly,
            };

            propertySymbol.ReadonlyStatus = hasPublicSetter
                ? ReadonlyStatus.Mutable
                : ReadonlyStatus.ExplicitReadonly;

            memberScope.AddMember(propertySymbol);
            symbols.Add(propertySymbol);
        }
    }

    /// <summary>
    /// Projects public fields of a CLR type as Mist field symbols.
    /// Fields marked <c>readonly</c> or <c>const</c> are projected as read-only.
    /// </summary>
    private void ProjectFields(
        Type clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (var field in fields)
        {
            // Skip enum backing fields (already handled in ProjectEnum)
            if (clrType.IsEnum)
                continue;

            if (_filter.IsMemberExcluded(field))
                continue;

            var fieldType = ProjectMemberType(field.FieldType, stdlib);

            var fieldSymbol = new Symbol(
                field.Name,
                SymbolKind.Field,
                SymbolOrigin.Backend,
                memberScope,
                fieldType,
                null);

            fieldSymbol.ReadonlyStatus = (field.IsInitOnly || field.IsLiteral)
                ? ReadonlyStatus.ExplicitReadonly
                : ReadonlyStatus.Mutable;

            memberScope.AddMember(fieldSymbol);
            symbols.Add(fieldSymbol);
        }
    }

    /// <summary>
    /// Projects public indexers (parameterized <c>Item</c> properties) of a CLR type
    /// as Mist indexer symbols.
    /// </summary>
    private void ProjectIndexers(
        Type clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (var property in properties)
        {
            var indexParams = property.GetIndexParameters();
            if (indexParams.Length == 0)
                continue;

            if (_filter.IsMemberExcluded(property))
                continue;

            // Unwrap by-ref property types (e.g., Span<T>.this[int] returns T&).
            var isRefReturn = property.PropertyType.IsByRef;
            IType elementType;
            if (isRefReturn)
            {
                var unwrappedType = property.PropertyType.GetElementType()!;
                elementType = ProjectMemberType(unwrappedType, stdlib);
            }
            else
            {
                elementType = ProjectMemberType(property.PropertyType, stdlib);
            }

            var indexerScope = new Scope(memberScope);

            var indexerSymbol = new Symbol(
                "this[]",
                SymbolKind.Indexer,
                SymbolOrigin.Backend,
                memberScope,
                elementType,
                null);

            // Mark mutable ref returns on indexers (Requirements 5.1, 5.2).
            if (isRefReturn)
            {
                indexerSymbol.ReturnRefKind = RefKind.Ref;
            }

            // Determine read-only vs read-write for the indexer.
            // Ref-returning indexers are inherently writable (the caller can assign
            // through the returned reference), so treat them as having a setter.
            var hasPublicGetter = property.GetGetMethod() is not null;
            var setMethod = property.GetSetMethod();
            var hasPublicSetter = (setMethod is not null && setMethod.IsPublic) || isRefReturn;

            indexerSymbol.PropertyMetadata = new Semantics.Contracts.PropertyInfo
            {
                HasGet = hasPublicGetter,
                HasSet = hasPublicSetter,
                ReadonlyStatus = hasPublicSetter ? ReadonlyStatus.Mutable : ReadonlyStatus.ExplicitReadonly,
            };

            indexerSymbol.ReadonlyStatus = hasPublicSetter
                ? ReadonlyStatus.Mutable
                : ReadonlyStatus.ExplicitReadonly;

            // Project index parameters
            var indexerParams = ProjectParametersWithResult(indexParams, indexerScope, stdlib);
            indexerSymbol.Parameters = indexerParams;

            memberScope.AddMember(indexerSymbol);
            symbols.Add(indexerSymbol);
        }
    }

    /// <summary>
    /// Projects CLR method parameters into Mist parameter symbols within the given scope.
    /// Handles optional parameters with default values.
    /// </summary>
    private void ProjectParameters(
        ParameterInfo[] parameters,
        Scope targetScope,
        IStdlibTypeProvider stdlib)
    {
        foreach (var param in parameters)
        {
            var paramType = ProjectMemberType(param.ParameterType, stdlib);

            var paramSymbol = new Symbol(
                param.Name ?? $"arg{param.Position}",
                SymbolKind.Parameter,
                SymbolOrigin.Backend,
                targetScope,
                paramType,
                null);

            // Mark optional parameters (those with default values)
            if (param.HasDefaultValue)
            {
                paramSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
            }

            targetScope.AddMember(paramSymbol);
        }
    }

    /// <summary>
    /// Projects CLR method parameters into Mist parameter symbols within the given scope
    /// and returns the projected parameter symbols as a list. Used by <see cref="ProjectMethods"/>
    /// and <see cref="ProjectConstructors"/> to populate <see cref="Symbol.Parameters"/>
    /// for nullable emission wrapping in the emitter (Task 8.2, Requirements 14.3, 14.4).
    /// <para>
    /// Parameters added to the scope use <see cref="ProjectMemberType"/> (no nullable wrapping)
    /// for type-checking compatibility. The returned symbols use <see cref="ProjectNullableType"/>
    /// which includes <see cref="NullableProjectionInfo"/> metadata for the emitter.
    /// </para>
    /// </summary>
    internal IReadOnlyList<ISymbol> ProjectParametersWithResult(
        ParameterInfo[] parameters,
        Scope targetScope,
        IStdlibTypeProvider stdlib)
    {
        var result = new List<ISymbol>(parameters.Length);
        foreach (var param in parameters)
        {
            // Detect CLR ref/in/out parameters (IsByRef) and unwrap the by-ref type.
            var isByRef = param.ParameterType.IsByRef;
            var typeToProject = isByRef
                ? param.ParameterType.GetElementType()!
                : param.ParameterType;

            // Use ProjectMemberType for the scope (type-checking uses these).
            var scopeParamType = ProjectMemberType(typeToProject, stdlib);

            var scopeParamSymbol = new Symbol(
                param.Name ?? $"arg{param.Position}",
                SymbolKind.Parameter,
                SymbolOrigin.Backend,
                targetScope,
                scopeParamType,
                null);

            // Set RefKind and ClrCallingConvention for ref/in/out parameters.
            if (isByRef)
            {
                scopeParamSymbol.RefKind = RefKind.Ref;
                scopeParamSymbol.ClrCallingConvention = param.IsOut
                    ? ClrCallingConvention.Out
                    : param.IsIn
                        ? ClrCallingConvention.In
                        : ClrCallingConvention.Ref;
            }

            // Mark optional parameters (those with default values)
            if (param.HasDefaultValue)
            {
                scopeParamSymbol.ReadonlyStatus = ReadonlyStatus.ExplicitReadonly;
            }

            targetScope.AddMember(scopeParamSymbol);

            // Use ProjectNullableType for the emitter metadata (includes NullableProjectionInfo).
            var emitterParamType = ProjectNullableType(typeToProject, stdlib);

            var emitterParamSymbol = new Symbol(
                param.Name ?? $"arg{param.Position}",
                SymbolKind.Parameter,
                SymbolOrigin.Backend,
                targetScope,
                emitterParamType,
                null);

            // Propagate RefKind and ClrCallingConvention to the emitter symbol as well.
            if (isByRef)
            {
                emitterParamSymbol.RefKind = RefKind.Ref;
                emitterParamSymbol.ClrCallingConvention = param.IsOut
                    ? ClrCallingConvention.Out
                    : param.IsIn
                        ? ClrCallingConvention.In
                        : ClrCallingConvention.Ref;
            }

            result.Add(emitterParamSymbol);
        }
        return result;
    }

    /// <summary>
    /// Projects a CLR type appearing in a member signature (parameter type, return type,
    /// property type, field type) with intrinsic substitution applied.
    /// This is the entry point for all member signature type projection.
    /// </summary>
    internal IType ProjectMemberType(Type clrType, IStdlibTypeProvider stdlib)
    {
        var projected = ProjectTypeReference(clrType, stdlib);
        return _intrinsicMap.SubstituteIntrinsics(projected, stdlib);
    }

    /// <summary>
    /// Projects the generic type parameters of a CLR generic method definition
    /// into Mist <see cref="TypeParameterType"/> instances with constraints.
    /// </summary>
    internal IReadOnlyList<IType> ProjectMethodTypeParameters(MethodInfo method, IStdlibTypeProvider stdlib)
    {
        var genericArgs = method.GetGenericArguments();
        var typeParams = new IType[genericArgs.Length];

        for (var i = 0; i < genericArgs.Length; i++)
        {
            var clrParam = genericArgs[i];
            var typeParam = new TypeParameterType(clrParam.Name, i);
            typeParam.Constraints = ProjectConstraints(clrParam, stdlib);
            typeParams[i] = typeParam;
        }

        return typeParams;
    }

    /// <summary>
    /// Extracts the <see cref="Scope"/> from a projected Mist type.
    /// Returns <c>null</c> if the type does not have a member scope.
    /// </summary>
    private static Scope? GetMemberScope(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope as Scope,
            StructType st => st.MemberScope as Scope,
            InterfaceType it => it.MemberScope as Scope,
            EnumType et => et.MemberScope as Scope,
            _ => null,
        };
    }

    /// <summary>
    /// Strips the generic arity suffix (e.g., <c>`2</c>) from a CLR type name.
    /// For example, <c>"Dictionary`2"</c> becomes <c>"Dictionary"</c>.
    /// Non-generic type names are returned unchanged.
    /// </summary>
    internal static string StripGenericAritySuffix(string typeName)
    {
        var backtickIndex = typeName.IndexOf('`');
        return backtickIndex >= 0 ? typeName[..backtickIndex] : typeName;
    }
}
