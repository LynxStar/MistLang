using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
///   <item>CLR interface → <see cref="AbstractType"/></item>
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
    private readonly IntrinsicTypeMap intrinsicMap;
    private readonly VisibilityFilter filter;

    /// <summary>
    /// Optional type-reference resolver injected by <see cref="AssemblyTypeProvider"/>.
    /// Maps a CLR <see cref="ClrType"/> to its cached, fully-projected Mist <see cref="IType"/>
    /// via the provider's <c>(namespace, name, arity)</c> cache (the same path
    /// <c>AssemblyTypeProvider.ResolveType</c> uses). When set, base-type and interface
    /// references resolve to the cached instances so cross-assembly inheritance connects
    /// to the real projected type rather than a disconnected shell (Requirements 6.1, 6.2, 6.3).
    /// <para>
    /// When <c>null</c> (e.g. unit tests that construct a bare <see cref="TypeProjector"/>),
    /// projection falls back to the shell-building behavior, identical to before this resolver
    /// existed — so those call sites are unaffected.
    /// </para>
    /// </summary>
    private readonly Func<ClrType, IType?>? typeReferenceResolver;

    /// <summary>
    /// Cache-only type-reference resolver: returns the cached type ONLY if it has already been
    /// projected and inserted into the provider's cache. Returns null for types not yet cached.
    /// Used by <see cref="ProjectTypeReference"/> for member return/parameter types to avoid
    /// infinite recursion (unlike <see cref="typeReferenceResolver"/> which triggers full projection).
    /// </summary>
    private readonly Func<ClrType, IType?>? typeReferenceResolverCacheOnly;

    /// <summary>
    /// Collected indications that a base-type or implemented-interface relationship was
    /// omitted during projection because the referenced CLR type could not be resolved from
    /// any assembly available to the provider (Requirement 6.6). The projector does not hold a
    /// <c>DiagnosticBag</c>, so it accumulates these structured records here and the
    /// <see cref="AssemblyTypeProvider"/> surfaces them. A <see cref="ConcurrentBag{T}"/> keeps
    /// the recording thread-safe because projection runs under parallel compilations.
    /// </summary>
    private readonly ConcurrentBag<UnresolvedRelationship> unresolvedRelationships = new();

    /// <summary>
    /// The relationships that were omitted during projection because their referenced CLR type
    /// could not be resolved (Requirement 6.6). Each entry identifies the relationship kind
    /// (base type vs interface), the derived type, and the unresolved type's name.
    /// </summary>
    internal IReadOnlyCollection<UnresolvedRelationship> UnresolvedRelationships
        => unresolvedRelationships;

    /// <summary>
    /// Tracks types currently being projected to detect and break recursive cycles
    /// in generic constraint projection (e.g., <c>IComparable&lt;T&gt;</c> where T
    /// has a constraint referencing <c>IComparable&lt;T&gt;</c>).
    /// </summary>
    [ThreadStatic]
    private static HashSet<ClrType>? projectionStack;

    /// <summary>
    /// Creates a new <see cref="TypeProjector"/>.
    /// </summary>
    /// <param name="intrinsicMap">Map used to substitute CLR types with Mist intrinsics.</param>
    /// <param name="filter">Visibility filter applied during projection.</param>
    /// <param name="typeReferenceResolver">
    /// Optional resolver mapping a CLR type to its cached, fully-projected Mist type. Supplied by
    /// <see cref="AssemblyTypeProvider"/> after it constructs itself (so the delegate can route
    /// through the provider's own type cache). Defaults to <c>null</c>, preserving the legacy
    /// shell-building behavior for callers that do not wire a provider (the additive contract
    /// keeps existing <c>new TypeProjector(intrinsicMap, filter)</c> call sites working).
    /// </param>
    public TypeProjector(
        IntrinsicTypeMap intrinsicMap,
        VisibilityFilter filter,
        Func<ClrType, IType?>? typeReferenceResolver = null,
        Func<ClrType, IType?>? typeReferenceResolverCacheOnly = null)
    {
        this.intrinsicMap = intrinsicMap;
        this.filter = filter;
        this.typeReferenceResolver = typeReferenceResolver;
        this.typeReferenceResolverCacheOnly = typeReferenceResolverCacheOnly;
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
        ClrType clrType,
        IStdlibTypeProvider stdlib)
    {
        var displayName = StripGenericAritySuffix(clrType.Name);
        var containingNamespace = clrType.Namespace;

        if(clrType.IsEnum)
        {
            return ProjectEnum(clrType, displayName, containingNamespace, stdlib);
        }

        if(clrType.IsInterface)
        {
            return ProjectAbstract(clrType, displayName, containingNamespace, stdlib);
        }

        if(clrType.IsValueType)
        {
            return ProjectStruct(clrType, displayName, containingNamespace, stdlib);
        }

        // Project System.Delegate / System.MulticastDelegate as BaseDelegateType singleton
        // (Requirement 3.1). Must appear before the named delegate and generic class checks.
        if(clrType.FullName is "System.Delegate" or "System.MulticastDelegate")
        {
            // Initialize the MemberScope if this is the first time we project System.Delegate.
            // The scope will be populated by the member projection phase in AssemblyTypeProvider.
            if(BaseDelegateType.Instance.MemberScope is null)
            {
                BaseDelegateType.Instance.MemberScope = new Scope(parent: null);
            }

            return (BaseDelegateType.Instance, ImmutableArray<ISymbol>.Empty);
        }

        // Project named delegates (Predicate<T>, Comparison<T>, EventHandler, etc.) as
        // DelegateType preserving CLR identity (Requirements 1.1, 1.5). The standard
        // Func/Action families are excluded (handled via TryProjectStandardFuncDelegate
        // in ProjectTypeReference). This check must come before the generic class fallback
        // so that delegate type definitions resolve to DelegateType, not ClassType.
        if(TryProjectNamedDelegate(clrType, stdlib, out var namedDelegateType))
        {
            return (namedDelegateType, ImmutableArray<ISymbol>.Empty);
        }

        // Default: class
        return ProjectClass(clrType, displayName, containingNamespace, stdlib);
    }

    /// <summary>
    /// Projects a CLR enum into a Mist <see cref="EnumType"/> with members and underlying type.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectEnum(
        ClrType clrType,
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

        foreach(var field in fields)
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
    /// Projects a CLR interface into a Mist <see cref="AbstractType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectAbstract(
        ClrType clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var abstractType = new AbstractType(displayName, memberScope, containingNamespace);

        // Project type parameters for generic interfaces
        if(clrType.IsGenericTypeDefinition)
        {
            abstractType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project implemented interfaces
        abstractType.Abstracts = ProjectInterfaces(clrType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, abstractType, stdlib);

        return (abstractType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Projects a CLR struct (value type) into a Mist <see cref="StructType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectStruct(
        ClrType clrType,
        string displayName,
        string? containingNamespace,
        IStdlibTypeProvider stdlib)
    {
        var memberScope = new Scope(null);
        var structType = new StructType(displayName, memberScope, containingNamespace);

        // Project ref-struct (IsByRefLike) CLR types as Mist stack-only structs so that
        // Span<T>, ReadOnlySpan<T>, and any other ref struct remain usable (Requirement 9).
        // Detection reads custom-attribute metadata rather than Type.IsByRefLike because this
        // runs under a MetadataLoadContext where IsByRefLike is not guaranteed reliable. Mist
        // does not yet perform ref-struct escape/lifetime analysis; the downstream C# compiler
        // is the safety backstop for any misuse of the projected type.
        if(IsRefStruct(clrType))
        {
            structType.IsRefStruct = true;
        }

        // Project type parameters for generic structs
        if(clrType.IsGenericTypeDefinition)
        {
            structType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project implemented interfaces
        structType.Abstracts = ProjectInterfaces(clrType, stdlib);

        // Synthesize IEnumerable<T> for types that duck-type to the enumerable pattern
        // but don't explicitly implement IEnumerable<T> (e.g., Span<T>)
        SynthesizeDuckTypedIEnumerable(clrType, structType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, structType, stdlib);

        // IDisposable detection: recognize CLR types implementing System.IDisposable as droppable.
        // An explicit Drop() method takes precedence over IDisposable-based synthetic drop.
        if(ImplementsIDisposable(clrType))
        {
            if(HasExplicitDropMethod(clrType))
            {
                structType.HasDrop = true;
                structType.IsDisposableDrop = false;
            }
            else
            {
                structType.HasDrop = true;
                structType.IsDisposableDrop = true;
            }
        }

        return (structType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Determines whether a CLR type is a ref struct (carries
    /// <c>System.Runtime.CompilerServices.IsByRefLikeAttribute</c>), e.g. <c>Span&lt;T&gt;</c>
    /// or <c>ReadOnlySpan&lt;T&gt;</c>. Mirrors <c>VisibilityFilter.IsRefStruct</c>: detection
    /// reads custom-attribute metadata by <c>FullName</c> rather than relying on
    /// <see cref="ClrType.IsByRefLike"/>, which is not guaranteed reliable under a
    /// <c>MetadataLoadContext</c>. Defaults to <c>false</c> if metadata cannot be loaded.
    /// </summary>
    private static bool IsRefStruct(ClrType type)
    {
        try
        {
            return type.GetCustomAttributesData()
                .Any(a => a.AttributeType.FullName ==
                    "System.Runtime.CompilerServices.IsByRefLikeAttribute");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the given CLR type implements <see cref="IDisposable"/>.
    /// Uses <see cref="ClrType.GetInterfaces()"/> which returns all transitively implemented
    /// interfaces, covering both direct implementation and inheritance through intermediate interfaces.
    /// </summary>
    private static bool ImplementsIDisposable(ClrType clrType)
    {
        try
        {
            return clrType.GetInterfaces()
                .Any(i => i.FullName == "System.IDisposable");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether the CLR type declares a public parameterless instance method
    /// named "Drop". Used to establish precedence: an explicit Drop method wins over
    /// IDisposable-based synthetic drop recognition.
    /// </summary>
    private static bool HasExplicitDropMethod(ClrType clrType)
    {
        try
        {
            return clrType
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.Name == "Drop" && m.GetParameters().Length == 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Projects a CLR class into a Mist <see cref="ClassType"/>.
    /// </summary>
    private (IType Type, ImmutableArray<ISymbol> Members) ProjectClass(
        ClrType clrType,
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
        if(clrType.IsGenericTypeDefinition)
        {
            classType.TypeParameters = ProjectTypeParameters(clrType, stdlib);
        }

        // Project base type (only if not System.Object).
        // Routed through the type-reference resolver so a cross-assembly base type connects
        // to the cached, fully-projected instance rather than a disconnected shell
        // (Requirements 6.1, 6.3). When the base type is a named, non-generic CLR type that the
        // provider cannot project (e.g. it lives in an assembly that was not supplied), the
        // relationship is omitted entirely — BaseType is left unset rather than connected to a
        // disconnected shell — and a diagnostic indication is recorded (Requirement 6.6).
        if(clrType.BaseType is not null && clrType.BaseType != typeof(object))
        {
            var resolvedBase = ResolveInheritanceReference(
                clrType.BaseType, stdlib, InheritanceRelationshipKind.BaseType, displayName);
            if(resolvedBase is not null)
            {
                classType.BaseType = resolvedBase;
            }
            // else: unresolvable base type — leave BaseType unset (Requirement 6.6).
        }

        // Project implemented interfaces
        classType.Abstracts = ProjectInterfaces(clrType, stdlib);

        // Synthesize IEnumerable<T> for types that duck-type to the enumerable pattern
        // but don't explicitly implement IEnumerable<T>
        SynthesizeDuckTypedIEnumerable(clrType, classType, stdlib);

        // Synthesize Enumerator associated type slots for IEnumerable<T> implementations
        SynthesizeEnumeratorSlots(clrType, classType, stdlib);

        // IDisposable detection: recognize CLR types implementing System.IDisposable as droppable.
        // An explicit Drop() method takes precedence over IDisposable-based synthetic drop.
        if(ImplementsIDisposable(clrType))
        {
            if(HasExplicitDropMethod(clrType))
            {
                classType.HasDrop = true;
                classType.IsDisposableDrop = false;
            }
            else
            {
                classType.HasDrop = true;
                classType.IsDisposableDrop = true;
            }
        }

        return (classType, ImmutableArray<ISymbol>.Empty);
    }

    /// <summary>
    /// Projects the generic type parameters of a CLR generic type definition
    /// into Mist <see cref="TypeParameterType"/> instances with constraints.
    /// </summary>
    private IReadOnlyList<IType> ProjectTypeParameters(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var genericArgs = clrType.GetGenericArguments();
        var typeParams = new IType[genericArgs.Length];

        for(var i = 0; i < genericArgs.Length; i++)
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
    private TypeParameterConstraints? ProjectConstraints(ClrType clrParam, IStdlibTypeProvider stdlib)
    {
        var attributes = clrParam.GenericParameterAttributes;

        var hasClassConstraint = (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
        var hasStructConstraint = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
        var hasNewConstraint = (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

        var constraints = clrParam.GetGenericParameterConstraints();
        IType? baseClassConstraint = null;
        var abstractConstraints = new List<IType>();

        foreach(var constraint in constraints)
        {
            // Skip System.ValueType which is the implicit base for struct constraint
            if(constraint == typeof(ValueType))
            {
                continue;
            }

            var projectedConstraint = ProjectTypeReference(constraint, stdlib);

            if(constraint.IsInterface)
            {
                abstractConstraints.Add(projectedConstraint);
            }
            else
            {
                baseClassConstraint = projectedConstraint;
            }
        }

        // Build constructor constraints: new() → empty parameter list
        var constructorConstraints = new List<IReadOnlyList<IType>>();
        if(hasNewConstraint && !hasStructConstraint)
        {
            // new() constraint (struct implicitly has new(), so only add if not struct)
            constructorConstraints.Add(Array.Empty<IType>());
        }

        // If no constraints at all, return null (unconstrained)
        if(!hasClassConstraint && !hasStructConstraint &&
            baseClassConstraint is null && abstractConstraints.Count == 0 &&
            constructorConstraints.Count == 0)
        {
            return null;
        }

        return new TypeParameterConstraints
        {
            HasClassConstraint = hasClassConstraint,
            HasStructConstraint = hasStructConstraint,
            BaseClassConstraint = baseClassConstraint,
            AbstractConstraints = abstractConstraints,
            ConstructorConstraints = constructorConstraints,
        };
    }

    /// <summary>
    /// Projects the interfaces implemented by a CLR type into Mist type references.
    /// Only includes directly declared interfaces (not inherited ones).
    /// Filters out the non-generic <c>System.Collections.IEnumerable</c> interface
    /// (Requirement 18.1).
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private IReadOnlyList<IType> ProjectInterfaces(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var interfaces = clrType.GetInterfaces();
        if(interfaces.Length == 0)
        {
            return Array.Empty<IType>();
        }

        // Get interfaces declared by parent to exclude inherited ones
        var parentInterfaces = new HashSet<ClrType>();
        if(clrType.BaseType is not null)
        {
            foreach(var parentIface in clrType.BaseType.GetInterfaces())
            {
                _ = parentInterfaces.Add(parentIface);
            }
        }

        var result = new List<IType>();

        // Dedupe by resolved interface identity so each resolved interface appears exactly
        // once (Requirement 6.2). Two distinct CLR interface Types can resolve to the same
        // projected Mist type — most notably across assemblies, where type forwarding makes
        // the same logical interface reachable as more than one metadata Type yet the
        // resolver maps them to a single cached instance. Reference equality catches that
        // canonical-instance case; a structural key catches structurally-identical
        // constructed/shell projections that are distinct instances.
        var seenIdentity = new HashSet<IType>(ReferenceEqualityComparer.Instance);
        var seenStructural = new HashSet<string>(StringComparer.Ordinal);

        foreach(var iface in interfaces)
        {
            // Skip interfaces inherited from base type
            if(parentInterfaces.Contains(iface))
            {
                continue;
            }

            // Skip the non-generic System.Collections.IEnumerable (Requirement 18.1)
            if(IsNonGenericIEnumerable(iface))
            {
                continue;
            }

            // Skip excluded interfaces
            var ifaceToCheck = iface.IsConstructedGenericType
                ? iface.GetGenericTypeDefinition()
                : iface;
            if(filter.IsTypeExcluded(ifaceToCheck))
            {
                continue;
            }

            // Resolve the interface through the type-reference resolver so a cross-assembly
            // interface connects to the cached, fully-projected instance rather than a shell
            // (Requirement 6.2). Falls back to shell-building when no resolver is wired. When
            // the interface is a named, non-generic CLR type that the provider cannot project,
            // the relationship is omitted (the interface is dropped from the list) and a
            // diagnostic indication is recorded (Requirement 6.6).
            var resolved = ResolveInheritanceReference(
                iface, stdlib, InheritanceRelationshipKind.Abstract, StripGenericAritySuffix(clrType.Name));
            if(resolved is null)
            {
                continue;
            }

            // Each resolved interface exactly once (Requirement 6.2).
            if(!seenIdentity.Add(resolved))
            {
                continue;
            }

            if(!seenStructural.Add(BuildInterfaceIdentityKey(resolved)))
            {
                continue;
            }

            result.Add(resolved);
        }

        return result;
    }

    /// <summary>
    /// Builds a stable structural identity key for a resolved interface <see cref="IType"/>,
    /// used to dedupe the projected interface list (Requirement 6.2). Named types key on their
    /// containing namespace and display name; constructed generic types key recursively on the
    /// generic definition plus type arguments so <c>IEnumerable&lt;int&gt;</c> and
    /// <c>IEnumerable&lt;string&gt;</c> remain distinct while two structurally identical
    /// projections of the same constructed interface collapse to one entry.
    /// </summary>
    private static string BuildInterfaceIdentityKey(IType type)
    {
        return type switch
        {
            ConstructedType ct =>
                BuildInterfaceIdentityKey(ct.GenericDefinition)
                + "<"
                + string.Join(",", ct.TypeArguments.Select(BuildInterfaceIdentityKey))
                + ">",
            ClassType c => (c.ContainingNamespace ?? string.Empty) + "." + c.DisplayName,
            StructType s => (s.ContainingNamespace ?? string.Empty) + "." + s.DisplayName,
            AbstractType it => (it.ContainingNamespace ?? string.Empty) + "." + it.DisplayName,
            _ => type.Kind + ":" + type.DisplayName,
        };
    }

    /// <summary>
    /// Resolves a base-type or implemented-interface CLR reference to its cached,
    /// fully-projected Mist <see cref="IType"/> via the injected type-reference resolver,
    /// so cross-assembly inheritance connects to the real projected instance the provider
    /// returns elsewhere (Requirements 6.1, 6.2, 6.3).
    ///
    /// <para>
    /// Only non-generic named types (class/struct/interface/enum) are routed through the
    /// resolver — generic parameters and arrays are never inheritance references, and
    /// constructed/open generic references keep their existing
    /// <see cref="ProjectTypeReference"/> handling (the resolver maps a generic definition,
    /// not a constructed type).
    /// </para>
    ///
    /// <para>
    /// Return-value contract:
    /// <list type="bullet">
    ///   <item>A resolved cached instance, when the resolver maps the named type.</item>
    ///   <item>A shell from <see cref="ProjectTypeReference"/> (never <c>null</c>) for the
    ///         cases that are <em>not</em> "unresolvable" in the Requirement 6.6 sense:
    ///         generic parameters, arrays, constructed/open generics, and any reference when no
    ///         resolver is wired (bare-projector unit tests). This preserves the pre-6.6
    ///         behavior for those call sites.</item>
    ///   <item><c>null</c> when a <em>named, non-generic</em> base/interface type cannot be
    ///         projected by the wired resolver (e.g. it lives in an assembly that was not
    ///         supplied). This signals the caller to omit the relationship entirely rather than
    ///         attach a disconnected shell, and a diagnostic indication is recorded
    ///         (Requirement 6.6).</item>
    /// </list>
    /// </para>
    /// </summary>
    private IType? ResolveInheritanceReference(
        ClrType clrType,
        IStdlibTypeProvider stdlib,
        InheritanceRelationshipKind kind,
        string derivedTypeName)
    {
        var isNamedNonGeneric =
            !clrType.IsGenericParameter
            && !clrType.IsArray
            && !clrType.IsConstructedGenericType
            && !clrType.IsGenericTypeDefinition;

        if(typeReferenceResolver is not null && isNamedNonGeneric)
        {
            var resolved = typeReferenceResolver(clrType);
            if(resolved is not null)
            {
                // Return the cached instance directly: the whole point is reference identity
                // with the provider's projected type. Base types and interfaces are never
                // intrinsic-mapped types, so intrinsic substitution would be a no-op here.
                return resolved;
            }

            // Genuinely unresolvable named base/interface (Requirement 6.6): the type exists in
            // metadata terms but the provider cannot project it (not in any loaded assembly, or
            // non-public). Record the indication and signal omission by returning null — do NOT
            // fall back to a disconnected shell here.
            unresolvedRelationships.Add(new UnresolvedRelationship(
                kind, derivedTypeName, clrType.FullName ?? clrType.Name));
            return null;
        }

        // Generic parameters/arrays/constructed generics, or no resolver wired: keep the legacy
        // shell-building behavior (Requirement 6.6 does not apply to these — constructed generic
        // bases/interfaces retain their existing handling, and bare-projector call sites are
        // unaffected).
        return ProjectTypeReference(clrType, stdlib);
    }

    /// <summary>
    /// Returns <c>true</c> if the given CLR type is the non-generic
    /// <c>System.Collections.IEnumerable</c> interface.
    /// </summary>
    private static bool IsNonGenericIEnumerable(ClrType type)
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
        ClrType clrType,
        IType projectedType,
        IStdlibTypeProvider stdlib)
    {
        var enumerableImpls = GetGenericIEnumerableImplementations(clrType);
        if(enumerableImpls.Count == 0)
        {
            return;
        }

        // Extract the member scope and namespace from the concrete projected type
        var (memberScope, containingNamespace) = projectedType switch
        {
            ClassType ct => (ct.MemberScope as Scope, ct.ContainingNamespace),
            StructType st => (st.MemberScope as Scope, st.ContainingNamespace),
            AbstractType it => (it.MemberScope as Scope, it.ContainingNamespace),
            _ => (null, null),
        };

        if(memberScope is null)
        {
            return;
        }

        foreach(var enumerableImpl in enumerableImpls)
        {
            var enumeratorType = ResolveEnumeratorType(clrType, enumerableImpl, stdlib);
            if(enumeratorType is null)
            {
                continue;
            }

            // Create the AssociatedTypeSlotType for the Enumerator slot
            var elementTypeArg = enumerableImpl.GetGenericArguments()[0];
            var projectedElementType = ProjectTypeReference(elementTypeArg, stdlib);
            projectedElementType = intrinsicMap.SubstituteIntrinsics(projectedElementType, stdlib);

            // Build the constraint: Enumerator must implement IEnumerator<TElement>
            // If the stdlib doesn't have IEnumerator yet (fallback provider before
            // Pass 1 completes), skip slot synthesis — it will be synthesized when
            // the type is re-resolved after the stdlib provider is upgraded.
            IType iEnumeratorStdlib;
            try
            {
                iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
            }
            catch(InvalidOperationException)
            {
                // FallbackStdlibTypeProvider doesn't have IEnumerator.
                // Skip slot synthesis; it will be available after UpdateStdlibProvider.
                continue;
            }

            var constraint = new TypeParameterConstraints
            {
                AbstractConstraints = new List<IType>
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
                AbstractConstraints = new List<IType>
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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    internal void SynthesizeDuckTypedIEnumerable(
        ClrType clrType,
        IType projectedType,
        IStdlibTypeProvider stdlib)
    {
        // Only applies if the type does NOT already implement IEnumerable<T>
        var enumerableImpls = GetGenericIEnumerableImplementations(clrType);
        if(enumerableImpls.Count > 0)
        {
            return;
        }

        // Guard against double-synthesis: if the Mist type already has IEnumerable
        // in its interface list (from a previous call), skip.
        var existingInterfaces = projectedType switch
        {
            StructType st => st.Abstracts,
            ClassType ct => ct.Abstracts,
            _ => Array.Empty<IType>(),
        };
        if(existingInterfaces.Any(i => i is ConstructedType ci
            && ci.GenericDefinition is AbstractType iface
            && iface.DisplayName == "IEnumerable"))
        {
            return;
        }

        // Look for a public parameterless GetEnumerator() method
        var getEnumeratorMethod = clrType.GetMethod(
            "GetEnumerator",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            ClrType.EmptyTypes,
            null);

        if(getEnumeratorMethod is null)
        {
            return;
        }

        var enumeratorClrType = getEnumeratorMethod.ReturnType;

        // Verify the enumerator type has a public bool MoveNext() method
        var moveNextMethod = enumeratorClrType.GetMethod(
            "MoveNext",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            ClrType.EmptyTypes,
            null);

        if(moveNextMethod is null || moveNextMethod.ReturnType.FullName != "System.Boolean")
        {
            return;
        }

        // Verify the enumerator type has a public Current property
        var currentProperty = enumeratorClrType.GetProperty(
            "Current",
            BindingFlags.Public | BindingFlags.Instance);

        if(currentProperty is null)
        {
            return;
        }

        // Resolve TElement from Current's return type
        // Unwrap by-ref wrapper (e.g., Span<T>.Enumerator.Current returns T&)
        var elementClrType = currentProperty.PropertyType;
        if(elementClrType.IsByRef)
        {
            elementClrType = elementClrType.GetElementType()!;
        }

        var projectedElementType = ProjectTypeReference(elementClrType, stdlib);
        projectedElementType = intrinsicMap.SubstituteIntrinsics(projectedElementType, stdlib);

        // Look up Standard.IEnumerable to synthesize IEnumerable<TElement>
        IType iEnumerableStdlib;
        try
        {
            iEnumerableStdlib = stdlib.LookupStandardType("IEnumerable");
        }
        catch(InvalidOperationException)
        {
            // Stdlib not loaded yet — skip synthesis.
            return;
        }

        var synthesizedIEnumerable = new ConstructedType(iEnumerableStdlib, new[] { projectedElementType });

        // Add the synthesized IEnumerable<TElement> to the type's interface list
        switch(projectedType)
        {
            case StructType st:
                var stInterfaces = new List<IType>(st.Abstracts) { synthesizedIEnumerable };
                st.Abstracts = stInterfaces;
                break;
            case ClassType ct:
                var ctInterfaces = new List<IType>(ct.Abstracts) { synthesizedIEnumerable };
                ct.Abstracts = ctInterfaces;
                break;
            default:
                return;
        }

        // Synthesize the Enumerator associated type slot (same logic as SynthesizeEnumeratorSlots)
        var (memberScope, containingNamespace) = projectedType switch
        {
            ClassType ct => (ct.MemberScope as Scope, ct.ContainingNamespace),
            StructType st => (st.MemberScope as Scope, st.ContainingNamespace),
            _ => (null, null),
        };

        if(memberScope is null)
        {
            return;
        }

        // Resolve the enumerator type for the slot
        var projectedEnumeratorType = ProjectTypeReference(enumeratorClrType, stdlib);

        // Build the constraint: Enumerator must implement IEnumerator<TElement>
        IType iEnumeratorStdlib;
        try
        {
            iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
        }
        catch(InvalidOperationException)
        {
            return;
        }

        var constraint = new TypeParameterConstraints
        {
            AbstractConstraints = new List<IType>
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
            AbstractConstraints = new List<IType>
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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private static List<ClrType> GetGenericIEnumerableImplementations(ClrType clrType)
    {
        var result = new List<ClrType>();
        foreach(var iface in clrType.GetInterfaces())
        {
            if(iface.IsConstructedGenericType
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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private IType? ResolveEnumeratorType(
        ClrType clrType,
        ClrType enumerableImpl,
        IStdlibTypeProvider stdlib)
    {
        var elementClrType = enumerableImpl.GetGenericArguments()[0];

        // Look for a public parameterless GetEnumerator() method
        var getEnumeratorMethod = clrType.GetMethod(
            "GetEnumerator",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            ClrType.EmptyTypes,
            null);

        if(getEnumeratorMethod is null)
        {
            // Fall back: use Standard.IEnumerator<TElement>
            return BuildStandardIEnumeratorType(elementClrType, stdlib);
        }

        var returnType = getEnumeratorMethod.ReturnType;

        // Check if the return type is IEnumerator<T> (the interface itself)
        if(returnType.IsInterface
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
    private IType? BuildStandardIEnumeratorType(ClrType elementClrType, IStdlibTypeProvider stdlib)
    {
        var projectedElement = ProjectTypeReference(elementClrType, stdlib);
        projectedElement = intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);

        IType iEnumeratorStdlib;
        try
        {
            iEnumeratorStdlib = stdlib.LookupStandardType("IEnumerator");
        }
        catch(InvalidOperationException)
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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    internal ImmutableArray<ISymbol> ProjectOperators(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib)
    {
        var operators = ImmutableArray.CreateBuilder<ISymbol>();

        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach(var method in methods)
        {
            if(!method.IsSpecialName)
            {
                continue;
            }

            // Skip operators excluded by the visibility filter
            if(filter.IsMemberExcluded(method))
            {
                continue;
            }

            var mistOpName = MapClrOperatorName(method);
            if(mistOpName is null)
            {
                continue;
            }

            // Project the return type through intrinsic substitution
            var returnType = ProjectTypeReference(method.ReturnType, stdlib);
            returnType = intrinsicMap.SubstituteIntrinsics(returnType, stdlib);

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
            foreach(var param in parameters)
            {
                var paramType = ProjectTypeReference(param.ParameterType, stdlib);
                paramType = intrinsicMap.SubstituteIntrinsics(paramType, stdlib);

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
        if(BinaryAndUnaryOperatorMap.TryGetValue(clrName, out var mistSymbol))
        {
            return "operator" + mistSymbol;
        }

        // Cast/conversion operators (Requirement 12.3)
        if(clrName is "op_Implicit" or "op_Explicit")
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
    internal IType ProjectTypeReference(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        // Handle generic type parameters (T, TKey, etc.)
        if(clrType.IsGenericParameter)
        {
            return new TypeParameterType(clrType.Name, clrType.GenericParameterPosition);
        }

        // System.Object → Mist UnknownType (the top type). Every non-void value is
        // assignable to `unknown`, mirroring how C# treats `object` as the universal base.
        if(clrType.Namespace == "System" && clrType.Name == "Object" && !clrType.IsGenericType)
        {
            return UnknownType.Instance;
        }

        // Project System.Delegate / System.MulticastDelegate as BaseDelegateType (Requirement 3.1).
        // This must appear before the standard func/named delegate dispatch so that the
        // hierarchy root types are caught early and returned as the singleton.
        if(clrType.FullName is "System.Delegate" or "System.MulticastDelegate")
        {
            return BaseDelegateType.Instance;
        }

        // Project the well-known .NET delegate families to the Mist Func type family
        // (Requirements 15.1, 15.2):
        //   System.Func<T1, ..., TN, TResult> → Standard.Func<T1, ..., TN, TResult>
        //   System.Action<T1, ..., TN>        → Standard.Func<T1, ..., TN, Void>
        //   System.Action (non-generic)       → Standard.Func<Void>
        // Done before the constructed-generic / simple-type branches so the delegate
        // types are never projected as plain CLR classes.
        if(TryProjectStandardFuncDelegate(clrType, stdlib, out var funcType))
        {
            return funcType;
        }

        // Project named delegate types (Predicate<T>, Comparison<T>, EventHandler, etc.)
        // as DelegateType instances preserving CLR identity with equivalent func<...> signature
        // (Requirements 1.1, 1.2, 1.5).
        if(TryProjectNamedDelegate(clrType, stdlib, out var namedDelegateType))
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
        if(TryProjectClrSpan(clrType, stdlib, out var spanType))
        {
            return spanType;
        }

        // Handle constructed generic types (e.g., List<int>, Dictionary<string, int>)
        if(clrType.IsConstructedGenericType)
        {
            return ProjectConstructedGenericType(clrType, stdlib);
        }

        // Handle generic type definitions (e.g., List<T> when used as a type reference).
        // First try the cache-only resolver to get the fully-projected type instance with
        // populated member scopes. This ensures that return types like List<T> on methods
        // (projected as ConstructedType(List, [T])) use the same ClassType instance that the
        // provider returns from ResolveType, so downstream member lookup (e.g., .Count) works
        // correctly when the result is stored in a var-inferred variable.
        if(clrType.IsGenericTypeDefinition)
        {
            if(typeReferenceResolverCacheOnly is not null && clrType.Namespace is not null)
            {
                var cached = typeReferenceResolverCacheOnly(clrType);
                if(cached is not null)
                {
                    return cached;
                }
            }

            return ProjectGenericDefinitionReference(clrType, stdlib);
        }

        // Handle arrays
        if(clrType.IsArray)
        {
            var elementType = clrType.GetElementType()!;
            var projectedElement = ProjectTypeReference(elementType, stdlib);
            // Apply intrinsic substitution to the element type
            projectedElement = intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);
            return new ArrayType(projectedElement);
        }

        // Try to resolve to the cached, fully-projected type via the provider's resolver.
        // This ensures member return types (e.g., Encoding.UTF8 returning Encoding) reference
        // the same instance the provider hands out from ResolveType, so their member scopes are
        // populated and downstream member lookup succeeds (C5 cross-assembly fix).
        // IMPORTANT: Only resolve types already in the cache to avoid infinite recursion during
        // type projection. Types not yet cached will get a disconnected shell here; the
        // ProjectMemberType method will attempt a full resolve as a second pass for member
        // signatures where a populated scope is critical for var-inference.
        if(typeReferenceResolver is not null
            && !clrType.IsGenericParameter
            && !clrType.IsArray
            && clrType.Namespace is not null
            && !clrType.IsConstructedGenericType
            && !clrType.IsGenericTypeDefinition)
        {
            var resolved = typeReferenceResolverCacheOnly?.Invoke(clrType);
            if(resolved is not null)
            {
                return resolved;
            }
        }

        // Project as a simple type and apply intrinsic substitution
        var projectedType = ProjectSimpleTypeReference(clrType, stdlib);
        return intrinsicMap.SubstituteIntrinsics(projectedType, stdlib);
    }

    /// <summary>
    /// Projects a constructed generic type (e.g., <c>List&lt;int&gt;</c>) into a Mist
    /// <see cref="ConstructedType"/> with recursively projected type arguments.
    /// </summary>
    private IType ProjectConstructedGenericType(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var genericDef = clrType.GetGenericTypeDefinition();
        var projectedDef = ProjectTypeReference(genericDef, stdlib);

        var typeArgs = clrType.GetGenericArguments();
        var projectedArgs = new IType[typeArgs.Length];
        for(var i = 0; i < typeArgs.Length; i++)
        {
            var projectedArg = ProjectTypeReference(typeArgs[i], stdlib);
            projectedArgs[i] = intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
        }

        // Apply intrinsic substitution to the definition itself
        projectedDef = intrinsicMap.SubstituteIntrinsics(projectedDef, stdlib);

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
    private bool TryProjectClrSpan(ClrType clrType, IStdlibTypeProvider stdlib, out IType spanType)
    {
        spanType = null!;

        if(!clrType.IsConstructedGenericType)
        {
            return false;
        }

        var genericDefName = clrType.GetGenericTypeDefinition().FullName;
        bool isReadOnly;
        if(genericDefName == "System.Span`1")
        {
            isReadOnly = false;
        }
        else if(genericDefName == "System.ReadOnlySpan`1")
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
        projectedElement = intrinsicMap.SubstituteIntrinsics(projectedElement, stdlib);

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
        ClrType clrType,
        IStdlibTypeProvider stdlib,
        out IType funcType)
    {
        funcType = null!;

        if(clrType.Namespace != "System")
        {
            return false;
        }

        // The Func type arguments, in Mist order (parameter types followed by the
        // return type). For Action, the synthesized return type is Void.
        IType[] funcTypeArguments;

        if(clrType.IsConstructedGenericType)
        {
            var genericName = clrType.GetGenericTypeDefinition().Name;
            var clrArgs = clrType.GetGenericArguments();

            if(genericName.StartsWith("Func`", StringComparison.Ordinal))
            {
                // System.Func<T1, ..., TN, TResult> → Standard.Func<T1, ..., TN, TResult>.
                // CLR Func already places the return type last, matching Mist's convention.
                funcTypeArguments = new IType[clrArgs.Length];
                for(var i = 0; i < clrArgs.Length; i++)
                {
                    var projectedArg = ProjectTypeReference(clrArgs[i], stdlib);
                    funcTypeArguments[i] = intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
                }
            }
            else if(genericName.StartsWith("Action`", StringComparison.Ordinal))
            {
                // System.Action<T1, ..., TN> → Standard.Func<T1, ..., TN, Void>.
                // Append Void as the trailing return type.
                if(!TryLookupVoid(stdlib, out var voidType))
                {
                    return false;
                }

                funcTypeArguments = new IType[clrArgs.Length + 1];
                for(var i = 0; i < clrArgs.Length; i++)
                {
                    var projectedArg = ProjectTypeReference(clrArgs[i], stdlib);
                    funcTypeArguments[i] = intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
                }

                funcTypeArguments[clrArgs.Length] = voidType;
            }
            else
            {
                return false;
            }
        }
        else if(clrType.Name == "Action" && !clrType.IsGenericType)
        {
            // System.Action (non-generic) → Standard.Func<Void>.
            if(!TryLookupVoid(stdlib, out var voidType))
            {
                return false;
            }

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
        catch(InvalidOperationException)
        {
            // Stdlib doesn't have the Func family yet (fallback provider before Pass 1).
            // Skip projection; the type is re-resolved after UpdateStdlibProvider.
            return false;
        }

        // Guard: ensure we resolved the real Standard.Func family and not a fallback
        // (e.g., the fallback provider has no Func and would not reach here, but a
        // wrong-arity fallback would produce a structurally incorrect type).
        if(funcDefinition is not ClassType { DisplayName: "Func", ContainingNamespace: "Standard" })
        {
            return false;
        }

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
        catch(InvalidOperationException)
        {
            voidType = null!;
            return false;
        }
    }

    /// <summary>
    /// Attempts to project a named CLR delegate type (e.g., <c>Predicate&lt;T&gt;</c>,
    /// <c>Comparison&lt;T&gt;</c>, <c>EventHandler</c>, <c>Converter&lt;TInput, TOutput&gt;</c>)
    /// into a <see cref="DelegateType"/> that preserves the delegate's CLR identity while
    /// carrying the equivalent <c>Standard.Func</c> signature (Requirements 1.1, 1.2, 1.5).
    ///
    /// <para>
    /// This handles all delegate types that are NOT <c>System.Func</c> or <c>System.Action</c>
    /// (those are handled by <see cref="TryProjectStandardFuncDelegate"/>). The projection
    /// inspects the delegate's <c>Invoke</c> method to determine parameter types and return
    /// type, then constructs the equivalent <c>Standard.Func</c> type and wraps it in a
    /// <see cref="DelegateType"/> that also preserves the CLR fully qualified name, namespace,
    /// simple name, type parameters, and type arguments.
    /// </para>
    /// </summary>
    /// <param name="clrType">The CLR type to inspect.</param>
    /// <param name="stdlib">The stdlib type provider used to resolve <c>Standard.Func</c>/<c>Void</c>.</param>
    /// <param name="delegateType">The projected <see cref="DelegateType"/>, when matched.</param>
    /// <returns><c>true</c> when <paramref name="clrType"/> is a projectable named delegate; otherwise <c>false</c>.</returns>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private bool TryProjectNamedDelegate(
        ClrType clrType,
        IStdlibTypeProvider stdlib,
        out IType delegateType)
    {
        delegateType = null!;

        // All delegates inherit from System.MulticastDelegate.
        if(clrType.BaseType?.FullName != "System.MulticastDelegate")
        {
            return false;
        }

        // Get the Invoke method that defines the delegate's signature.
        var invokeMethod = clrType.GetMethod("Invoke");
        if(invokeMethod is null)
        {
            return false;
        }

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
        catch(InvalidOperationException)
        {
            // Stdlib doesn't have the Func family yet (fallback provider before Pass 1).
            return false;
        }

        // Guard: ensure we resolved the real Standard.Func family.
        if(funcDefinition is not ClassType { DisplayName: "Func", ContainingNamespace: "Standard" })
        {
            return false;
        }

        // Build the type arguments: parameter types followed by return type (or Void).
        var funcTypeArguments = new IType[arity];
        for(var i = 0; i < parameters.Length; i++)
        {
            var projectedArg = ProjectTypeReference(parameters[i].ParameterType, stdlib);
            funcTypeArguments[i] = intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
        }

        if(isVoidReturn)
        {
            if(!TryLookupVoid(stdlib, out var voidType))
            {
                return false;
            }

            funcTypeArguments[parameters.Length] = voidType;
        }
        else
        {
            var projectedReturn = ProjectTypeReference(returnType, stdlib);
            funcTypeArguments[parameters.Length] = intrinsicMap.SubstituteIntrinsics(projectedReturn, stdlib);
        }

        var funcSignature = new ConstructedType(funcDefinition, funcTypeArguments);

        // Determine CLR identity components.
        var clrFullName = clrType.FullName ?? clrType.Name;
        var clrNamespace = clrType.Namespace ?? "";
        var clrSimpleName = StripGenericAritySuffix(clrType.Name);

        // Determine type parameters (for open generic definitions) and type arguments
        // (for constructed generic instances).
        IReadOnlyList<IType> typeParameters;
        IReadOnlyList<IType> typeArguments;

        if(clrType.IsGenericTypeDefinition)
        {
            // Open generic delegate definition (e.g., Predicate<T>, Comparison<T>).
            // TypeParameters carry the open type parameter placeholders.
            typeParameters = ProjectTypeParameters(clrType, stdlib);
            typeArguments = Array.Empty<IType>();
        }
        else if(clrType.IsConstructedGenericType)
        {
            // Constructed generic delegate (e.g., Predicate<int>).
            // TypeParameters are empty; TypeArguments carry the concrete types.
            typeParameters = Array.Empty<IType>();
            var clrArgs = clrType.GetGenericArguments();
            var projectedArgs = new IType[clrArgs.Length];
            for(var i = 0; i < clrArgs.Length; i++)
            {
                var projectedArg = ProjectTypeReference(clrArgs[i], stdlib);
                projectedArgs[i] = intrinsicMap.SubstituteIntrinsics(projectedArg, stdlib);
            }

            typeArguments = projectedArgs;
        }
        else
        {
            // Non-generic delegate (e.g., RequestDelegate, EventHandler without generics).
            typeParameters = Array.Empty<IType>();
            typeArguments = Array.Empty<IType>();
        }

        // Create an empty member scope for the delegate. Members inherited from
        // System.Delegate (GetType, DynamicInvoke, Method, Target) will be populated
        // by the member projection phase when the delegate type is fully projected.
        var memberScope = new Scope(parent: null);

        delegateType = new DelegateType(
            clrFullName: clrFullName,
            clrNamespace: clrNamespace,
            clrSimpleName: clrSimpleName,
            funcSignature: funcSignature,
            typeParameters: typeParameters,
            typeArguments: typeArguments,
            memberScope: memberScope);

        return true;
    }

    /// <summary>
    /// Projects a generic type definition used as a type reference (not a full projection).
    /// Creates a minimal type shell with the correct display name and namespace.
    /// </summary>
    private IType ProjectGenericDefinitionReference(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var displayName = StripGenericAritySuffix(clrType.Name);
        var containingNamespace = clrType.Namespace;

        // Cycle detection: if we're already projecting this type's constraints,
        // return a minimal shell without type parameters to break the recursion.
        var stack = projectionStack ??= new HashSet<ClrType>(ReferenceEqualityComparer.Instance);
        if(!stack.Add(clrType))
        {
            // Already projecting this type — return a minimal shell to break the cycle.
            if(clrType.IsInterface)
            {
                return new AbstractType(displayName, new Scope(null), containingNamespace);
            }

            if(clrType.IsValueType)
            {
                return new StructType(displayName, new Scope(null), containingNamespace);
            }

            return new ClassType(displayName, new Scope(null), containingNamespace);
        }

        try
        {
            if(clrType.IsInterface)
            {
                var iface = new AbstractType(displayName, new Scope(null), containingNamespace);
                iface.TypeParameters = ProjectTypeParameters(clrType, stdlib);
                return iface;
            }

            if(clrType.IsValueType)
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
            _ = stack.Remove(clrType);
        }
    }

    /// <summary>
    /// Projects a simple (non-generic, non-array) CLR type reference into a Mist type.
    /// Creates a minimal type shell with the correct display name and namespace.
    /// </summary>
    private static IType ProjectSimpleTypeReference(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var displayName = clrType.Name;
        var containingNamespace = clrType.Namespace;

        if(clrType.IsInterface)
        {
            return new AbstractType(displayName, new Scope(null), containingNamespace);
        }

        if(clrType.IsEnum)
        {
            return new EnumType(displayName, new Scope(null), containingNamespace);
        }

        if(clrType.IsValueType)
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
    internal IType ProjectNullableType(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        // Case 1: Nullable<T> (e.g., int?, bool?)
        var underlyingNullable = NullableReflection.GetUnderlyingType(clrType);
        if(underlyingNullable is not null)
        {
            var innerMistType = ProjectTypeReference(underlyingNullable, stdlib);
            innerMistType = intrinsicMap.SubstituteIntrinsics(innerMistType, stdlib);
            return CreateNullableUnion(innerMistType, NullableOrigin.NullableValueType, stdlib);
        }

        // Case 2: Reference types (class, interface, string, array, delegate)
        if(IsReferenceType(clrType))
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
    private static bool IsReferenceType(ClrType clrType)
    {
        // Generic type parameters are handled separately — don't wrap them
        if(clrType.IsGenericParameter)
        {
            return false;
        }

        // Arrays are always reference types
        if(clrType.IsArray)
        {
            return true;
        }

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
    /// <remarks>
    /// <para><b>Inherited-member projection (Requirements 7.1, 7.3, 7.4, 7.5, 7.8):</b></para>
    /// <para>
    /// The reflection calls below use <c>BindingFlags.Public | Instance | Static</c> WITHOUT
    /// <c>DeclaredOnly</c>, producing a flattened enumeration that includes members inherited
    /// from all ancestor types. For <c>virtual</c>/<c>override</c> chains, reflection returns
    /// only the most-derived signature — base definitions that were overridden do not appear.
    /// This satisfies Req 7.3 (most-derived visible signature exposed) without any explicit
    /// de-duplication at this layer.
    /// </para>
    /// <para>
    /// For <c>new</c>-hidden (shadow) members, both the derived and base declarations appear in
    /// the flattened enumeration. However, under MetadataLoadContext the derived <c>new</c> method
    /// hides the base slot: only the most-derived declaring type's version appears in the returned
    /// array for any given method slot, so no extra signature-based de-duplication is needed here.
    /// </para>
    /// <para>
    /// <b>Req 7.4 fallback (nearest visible ancestor):</b> When the most-derived definition of
    /// an inherited member is hidden by <see cref="VisibilityFilter.IsMemberExcluded"/> (e.g. it
    /// added a pointer parameter in its override), it is simply not projected into this type's
    /// scope. However, each ancestor type is projected independently via its own flattened
    /// enumeration — where that ancestor IS the most-derived level. The ancestor's visible copy
    /// therefore exists in the ancestor's member scope. The semantic model's
    /// <c>CollectMethodOverloads</c> walks the connected base chain (Requirements 6.1/6.2) and
    /// finds the nearest visible ancestor's copy, satisfying Req 7.4.
    /// </para>
    /// <para>
    /// <b>Inherited members callable (Requirements 7.1, 7.5, 7.8):</b> With the base chain
    /// connected to cached fully-projected ancestors (Task 6.1/6.2) and members projected into
    /// each level's scope, the semantic model resolves inherited members either from the leaf
    /// type's own member scope (for scope.Lookup-based resolution) or via <c>CollectMethodOverloads</c>
    /// which de-duplicates across the chain using signature keys, keeping only the first
    /// (most-derived) occurrence. This makes inherited members callable on derived types.
    /// </para>
    /// </remarks>
    /// <param name="clrType">The CLR type whose members to project.</param>
    /// <param name="projectedMistType">The already-projected Mist type (used as the declaring type for constructors).</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    /// <returns>An array of projected member symbols.</returns>
    public ImmutableArray<ISymbol> ProjectMembers(
        ClrType clrType,
        IType projectedMistType,
        IStdlibTypeProvider stdlib)
    {
        var symbols = ImmutableArray.CreateBuilder<ISymbol>();

        var memberScope = GetMemberScope(projectedMistType);
        if(memberScope is null)
        {
            return symbols.ToImmutable();
        }

        // Project constructors
        ProjectConstructors(clrType, projectedMistType, memberScope, stdlib, symbols);

        // Project methods (instance and static, excluding property accessors and operators)
        // Reflection's flattened enumeration (no DeclaredOnly) returns the most-derived
        // signature for virtual/override chains. IsMemberExcluded filters per-overload,
        // so only genuinely-offending methods are hidden (Req 7.2, 7.3, 8.1).
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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private void ProjectConstructors(
        ClrType clrType,
        IType declaringType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var constructors = clrType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        foreach(var ctor in constructors)
        {
            if(filter.IsMemberExcluded(ctor))
            {
                continue;
            }

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
    /// Also projects protected virtual/abstract methods as override slot candidates for
    /// implicit override matching in the C# emitter.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private void ProjectMethods(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var methods = clrType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach(var method in methods)
        {
            // Skip property accessors, event accessors, and operators
            if(method.IsSpecialName)
            {
                continue;
            }

            if(filter.IsMemberExcluded(method))
            {
                continue;
            }

            // Project return type — unwrap by-ref returns to get the element type.
            IType returnType;
            var isRefReturn = method.ReturnType.IsByRef;
            if(isRefReturn)
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

            // Mark virtual/abstract public methods for implicit override matching.
            if(method.IsVirtual || method.IsAbstract)
            {
                methodSymbol.IsVirtualOrAbstract = true;
                methodSymbol.ClrAccessModifier = "public";
            }

            // Mark mutable ref returns (Requirements 5.1, 5.2).
            if(isRefReturn)
            {
                methodSymbol.ReturnRefKind = RefKind.Ref;
            }

            // Project generic method type parameters
            if(method.IsGenericMethodDefinition)
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

        // Project protected virtual/abstract methods as override slot candidates.
        // These are not callable from Mist code directly, but are needed by the emitter
        // to detect implicit override matches when a Mist-derived class defines a method
        // with the same name and signature as a CLR base's protected virtual/abstract slot.
        var protectedMethods = clrType.GetMethods(
            BindingFlags.NonPublic | BindingFlags.Instance);

        foreach(var method in protectedMethods)
        {
            // Only project virtual/abstract methods (override slots).
            if(!method.IsVirtual && !method.IsAbstract)
            {
                continue;
            }

            // Only project protected and protected internal methods.
            if(!method.IsFamily && !method.IsFamilyOrAssembly)
            {
                continue;
            }

            // Skip property accessors, event accessors, and operators.
            if(method.IsSpecialName)
            {
                continue;
            }

            // Note: We do NOT call _filter.IsMemberExcluded() here because that filter
            // rejects non-public members. We've already verified accessibility above.
            // If the method signature contains pointer types or other unsupported elements,
            // the C# compiler will catch any issues downstream.

            // Skip if a public method with the same name already exists (the public
            // projection already covers it).
            if(memberScope.Lookup(method.Name) is not null)
            {
                continue;
            }

            // Project return type.
            IType returnType;
            var isRefReturn = method.ReturnType.IsByRef;
            if(isRefReturn)
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

            methodSymbol.IsVirtualOrAbstract = true;
            methodSymbol.ClrAccessModifier = method.IsFamilyOrAssembly
                ? "protected"  // protected internal from another assembly is overridden as just "protected"
                : "protected";

            if(isRefReturn)
            {
                methodSymbol.ReturnRefKind = RefKind.Ref;
            }

            if(method.IsGenericMethodDefinition)
            {
                methodSymbol.TypeParameters = ProjectMethodTypeParameters(method, stdlib);
            }

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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private void ProjectProperties(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach(var property in properties)
        {
            // Skip indexers — they are projected separately
            if(property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if(filter.IsMemberExcluded(property))
            {
                continue;
            }

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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private void ProjectFields(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach(var field in fields)
        {
            // Skip enum backing fields (already handled in ProjectEnum)
            if(clrType.IsEnum)
            {
                continue;
            }

            if(filter.IsMemberExcluded(field))
            {
                continue;
            }

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
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Types are loaded via MetadataLoadContext for metadata inspection only; trimming cannot affect them.")]
    private void ProjectIndexers(
        ClrType clrType,
        Scope memberScope,
        IStdlibTypeProvider stdlib,
        ImmutableArray<ISymbol>.Builder symbols)
    {
        var properties = clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach(var property in properties)
        {
            var indexParams = property.GetIndexParameters();
            if(indexParams.Length == 0)
            {
                continue;
            }

            if(filter.IsMemberExcluded(property))
            {
                continue;
            }

            // Unwrap by-ref property types (e.g., Span<T>.this[int] returns T&).
            var isRefReturn = property.PropertyType.IsByRef;
            IType elementType;
            if(isRefReturn)
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
            if(isRefReturn)
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
        foreach(var param in parameters)
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
            if(isByRef)
            {
                scopeParamSymbol.RefKind = RefKind.Ref;
                scopeParamSymbol.ClrCallingConvention = param.IsOut
                    ? ClrCallingConvention.Out
                    : param.IsIn
                        ? ClrCallingConvention.In
                        : ClrCallingConvention.Ref;
            }

            // Mark optional parameters (those with default values)
            if(param.HasDefaultValue)
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
            if(isByRef)
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
    internal IType ProjectMemberType(ClrType clrType, IStdlibTypeProvider stdlib)
    {
        var projected = ProjectTypeReference(clrType, stdlib);
        projected = intrinsicMap.SubstituteIntrinsics(projected, stdlib);

        // If ProjectTypeReference returned a disconnected shell (empty member scope) for a
        // non-generic, non-constructed class/struct/interface, attempt a full resolve via the
        // provider. This is safe here because ProjectMemberType is only called during member
        // projection (ProjectMembers), which executes AFTER the owning type is already cached
        // in ResolveTypeCore. Any back-reference to the owning type will find it in the cache,
        // preventing infinite recursion.
        // Without this, var-inferred variables whose type comes from a method return type
        // (e.g., `var builder = WebApplication.CreateBuilder()` returning WebApplicationBuilder)
        // get a disconnected shell with no members, causing MIST_UNDEFINED_MEMBER errors.
        if(typeReferenceResolver is not null
            && IsDisconnectedShell(projected)
            && !clrType.IsGenericParameter
            && !clrType.IsArray
            && clrType.Namespace is not null
            && !clrType.IsConstructedGenericType
            && !clrType.IsGenericTypeDefinition)
        {
            var resolved = typeReferenceResolver(clrType);
            if(resolved is not null)
            {
                return resolved;
            }
        }

        // For constructed generic types (e.g., EntityTypeBuilder<TEntity>), check if
        // the generic definition inside the ConstructedType is a disconnected shell.
        // If so, attempt a full resolve of the generic definition to get its members.
        if(typeReferenceResolver is not null
            && projected is ConstructedType projectedCt
            && IsDisconnectedShell(projectedCt.GenericDefinition)
            && clrType.IsConstructedGenericType)
        {
            var genericDef = clrType.GetGenericTypeDefinition();
            if(genericDef.Namespace is not null)
            {
                var resolvedDef = typeReferenceResolver(genericDef);
                if(resolvedDef is not null)
                {
                    // Reconstruct the ConstructedType with the fully-populated definition.
                    projected = new ConstructedType(resolvedDef, projectedCt.TypeArguments);
                }
            }
        }

        return projected;
    }

    /// <summary>
    /// Determines whether a projected type is a "disconnected shell" — a type with an empty
    /// member scope produced by <see cref="ProjectSimpleTypeReference"/> when the type was not
    /// yet in the provider's cache during projection.
    /// </summary>
    private static bool IsDisconnectedShell(IType type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope.Members.Count == 0,
            StructType st => st.MemberScope.Members.Count == 0,
            AbstractType it => it.MemberScope.Members.Count == 0,
            _ => false
        };
    }

    /// <summary>
    /// Projects the generic type parameters of a CLR generic method definition
    /// into Mist <see cref="TypeParameterType"/> instances with constraints.
    /// </summary>
    internal IReadOnlyList<IType> ProjectMethodTypeParameters(MethodInfo method, IStdlibTypeProvider stdlib)
    {
        var genericArgs = method.GetGenericArguments();
        var typeParams = new IType[genericArgs.Length];

        for(var i = 0; i < genericArgs.Length; i++)
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
            AbstractType it => it.MemberScope as Scope,
            EnumType et => et.MemberScope as Scope,
            DelegateType dt => dt.MemberScope as Scope,
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