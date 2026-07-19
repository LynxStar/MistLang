using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves AST type reference nodes into <see cref="IMistType"/> by performing
/// scope-based name lookup against the program model.
/// </summary>
internal sealed class TypeReferenceResolver
{
    /// <summary>
    /// The prefix shared by every reserved positional tuple accessor
    /// (<c>Item1</c>, <c>Item2</c>, ...). The positional digits follow this prefix.
    /// </summary>
    private const string PositionalAccessorPrefix = "Item";

    /// <summary>
    /// The arity-bearing simple name of the built-in array definition
    /// (<c>Standard.Array&lt;,&gt;</c>) that backs every <c>T[]</c> surface type.
    /// </summary>
    private const string ArrayDefinitionName = "Array<,>";

    private readonly BuilderContext context;

    public TypeReferenceResolver(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to an <see cref="IMistType"/>.
    /// </summary>
    public IMistType Resolve(TypeReferenceNode typeRef, Scope scope)
    {
        return Resolve(typeRef, scope, barePackAllowed: false);
    }

    /// <summary>
    /// Resolves a value parameter's declared type. Unlike
    /// <see cref="Resolve(TypeReferenceNode, Scope)"/>, a bare pack name in the value-pack
    /// form <c>params TArgs args</c> resolves to the pack itself — so the caller
    /// (<see cref="ParameterBuilder"/>) can wrap it in the value-pack tuple <c>(TArgs...)</c>
    /// — instead of being reported as a bare-type-position error
    /// (<c>MIST_VAR_0002</c>, Requirement 6.3). Only the value-side <c>params</c> path passes
    /// <paramref name="isParams"/> = <c>true</c>; every other type position uses the ordinary
    /// resolve and so still rejects a bare pack. A homogeneous <c>params T[]</c> (an array of
    /// a non-pack element) and an indexed pack <c>TArgs[i]</c> resolve identically either way.
    /// </summary>
    public IMistType ResolveParameterType(TypeReferenceNode typeRef, Scope scope, bool isParams)
    {
        return Resolve(typeRef, scope, barePackAllowed: isParams);
    }

    /// <summary>
    /// Core type-reference resolution. <paramref name="barePackAllowed"/> permits a bare pack
    /// name at the top level to resolve to the pack itself (the value-pack parameter case);
    /// it is not propagated into nested resolutions (union constituents, array elements,
    /// tuple elements), so a pack nested anywhere else is still subject to the ordinary rules.
    /// </summary>
    private IMistType Resolve(TypeReferenceNode typeRef, Scope scope, bool barePackAllowed)
    {
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            return ResolveUnion(typeRef, scope);
        }

        return Resolve(typeRef.First, scope, barePackAllowed);
    }

    /// <summary>
    /// Resolves a union type reference (multiple pipe-separated constituents) into
    /// an interned <see cref="UnionType"/>.
    /// </summary>
    private IMistType ResolveUnion(TypeReferenceNode typeRef, Scope scope)
    {
        var constituents = new List<IMistType>(typeRef.SingleTypeRefs.Length + 1)
        {
            Resolve(typeRef.First, scope)
        };

        foreach(var singleRef in typeRef.SingleTypeRefs)
        {
            constituents.Add(Resolve(singleRef, scope));
        }

        return context.ProgramModel.TypeInterner.GetOrCreateUnionType(constituents);
    }

    /// <summary>
    /// Resolves a tuple type reference. The structural tuple is always interned by
    /// element types; when this declaration site names any element, the interned type
    /// is wrapped in a (non-interned) <see cref="NamedTupleType"/> that carries the
    /// names without affecting identity.
    /// </summary>
    private IMistType ResolveTuple(TupleSingleTypeRefSingleTypeRefNode tupleRef, Scope scope)
    {
        var elementTypes = new List<IMistType>();
        var elementNames = new List<string?>();

        ResolveTupleElement(tupleRef.First, scope, elementTypes, elementNames);
        ResolveTupleElement(tupleRef.Rest, scope, elementTypes, elementNames);

        foreach(var additional in tupleRef.TupleTypeElements)
        {
            ResolveTupleElement(additional, scope, elementTypes, elementNames);
        }

        var structural = context.ProgramModel.TypeInterner.GetOrCreateTupleType(elementTypes);

        if(elementNames.Exists(name => name is not null))
        {
            return new NamedTupleType(structural, elementNames);
        }

        return structural;
    }

    private void ResolveTupleElement(TupleTypeElementNode element, Scope scope, List<IMistType> types, List<string?> names)
    {
        types.Add(Resolve(element.Type, scope));

        var name = element.Name?.Lexeme;

        if(name is not null && IsReservedTupleElementName(name))
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ReservedTupleElementName, element, null, name);

            // Drop the reserved name so it does not collide with the positional field.
            name = null;
        }

        names.Add(name);
    }

    /// <summary>
    /// Determines whether a tuple element name collides with the reserved positional
    /// accessors (<c>Item1</c>, <c>Item2</c>, ...). Such names are rejected because the
    /// structural tuple already exposes every element positionally under these names.
    /// </summary>
    private static bool IsReservedTupleElementName(string name)
    {
        if(!name.StartsWith(PositionalAccessorPrefix, StringComparison.Ordinal)
            || name.Length == PositionalAccessorPrefix.Length)
        {
            return false;
        }

        for(var i = PositionalAccessorPrefix.Length; i < name.Length; i++)
        {
            if(!char.IsAsciiDigit(name[i]))
            {
                return false;
            }
        }

        // Reject a leading zero (e.g., "Item01"), which is not a canonical positional name.
        return name[PositionalAccessorPrefix.Length] != '0';
    }

    /// <summary>
    /// Resolves a parenthesized type reference. The lone pack-spread form
    /// <c>(TArgs...)</c> is the value-pack tuple type: it resolves to the tuple
    /// <c>(TArgs...)</c> whose elements come from spreading the pack (see
    /// <see cref="BuildValuePackTuple"/>). Any other parenthesized inner type resolves
    /// to the inner type as before.
    /// </summary>
    private IMistType ResolveParenthesized(ParenthesizedTypeRefSingleTypeRefNode parenRef, Scope scope)
    {
        if(TryGetLoneSpread(parenRef.Inner, out var spread))
        {
            return ResolveSpreadTuple(spread, scope);
        }

        return Resolve(parenRef.Inner, scope);
    }

    /// <summary>
    /// True when <paramref name="typeRef"/> is a single (non-union) pack-spread operand
    /// — the <c>TArgs...</c> inside <c>(TArgs...)</c>.
    /// </summary>
    private static bool TryGetLoneSpread(TypeReferenceNode typeRef, out SpreadSingleTypeRefSingleTypeRefNode spread)
    {
        if(typeRef.SingleTypeRefs.Length == 0 && typeRef.First is SpreadSingleTypeRefSingleTypeRefNode loneSpread)
        {
            spread = loneSpread;
            return true;
        }

        spread = null!;
        return false;
    }

    /// <summary>
    /// Resolves the value-pack tuple <c>(TArgs...)</c> from a lone spread operand. The
    /// operand must denote a type-parameter pack; the resulting tuple's element list is
    /// the pack itself, which "spreads" into a single symbolic element while the pack is
    /// unbound (a later stage expands it into one tuple element per bound type once the
    /// pack binds a concrete <c>Type[]</c>).
    /// </summary>
    private IMistType ResolveSpreadTuple(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var pack = ResolveSpreadOperandPack(spread, scope);
        return pack is null ? ErrorType.Instance : BuildValuePackTuple(pack);
    }

    /// <summary>
    /// Resolves a spread operand (the <c>TArgs</c> in <c>TArgs...</c>) to its
    /// type-parameter pack. Returns <c>null</c> when the operand does not denote a pack:
    /// an unresolved name (already diagnosed by name lookup) yields <c>null</c> silently,
    /// while a resolved-but-non-pack operand is reported here before returning <c>null</c>.
    /// Shared by the value-pack tuple form <c>(TArgs...)</c> (see
    /// <see cref="ResolveSpreadTuple"/>) and member-declaring spreads (see
    /// <see cref="SpreadMemberBuilder"/>), which additionally need the pack itself to
    /// record the spread-generated member family.
    /// </summary>
    public TypeParameter? ResolveSpreadOperandPack(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var operand = ResolveNamedType(QualifiedNameHelper.Build(spread.Name), scope, spread);
        if(operand is ErrorType)
        {
            return null;
        }

        if(operand is TypeParameter { IsPack: true } pack)
        {
            return pack;
        }

        // A spread operand must be a pack. The dedicated pack-operand diagnostics are a
        // later type-checker task; reporting the generic unsupported-node diagnostic here
        // keeps an invalid `(NonPack...)` from silently resolving to a well-formed type.
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, spread, null, spread.GetType().Name);
        return null;
    }

    /// <summary>
    /// Builds the value-pack tuple type <c>(TArgs...)</c> for the type-parameter pack
    /// <paramref name="pack"/>. Both the sugared value pack <c>params TArgs args</c> and
    /// the explicit plain tuple parameter <c>(TArgs...) args</c> resolve to this same
    /// interned tuple; only the <c>params</c> marker recorded on the
    /// <see cref="Parameter"/> distinguishes the flattened-call form. While the
    /// pack is symbolic the tuple carries a single pack element; binding later
    /// substitutes the concrete <c>Type[]</c> and expands it into one element per type.
    /// </summary>
    public TupleType BuildValuePackTuple(TypeParameter pack)
    {
        return context.ProgramModel.TypeInterner.GetOrCreateTupleType(new IMistType[] { pack });
    }

    /// <summary>
    /// Resolves a single (non-union) type reference node.
    /// </summary>
    public IMistType Resolve(SingleTypeRefNode singleRef, Scope scope)
    {
        return Resolve(singleRef, scope, barePackAllowed: false);
    }

    /// <summary>
    /// Resolves a single (non-union) type reference node. <paramref name="barePackAllowed"/>
    /// controls the bare-pack case of the pack position semantics (see
    /// <see cref="ResolvePackInTypePosition"/>).
    /// </summary>
    private IMistType Resolve(SingleTypeRefNode singleRef, Scope scope, bool barePackAllowed)
    {
        var inner = singleRef switch
        {
            VoidSingleTypeRefSingleTypeRefNode => ResolveReservedAlias("Void"),
            UnknownSingleTypeRefSingleTypeRefNode => UnknownType.Instance,
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolveReservedAlias(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef, scope),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => ResolveParenthesized(parenRef, scope),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
            _ => ReportUnsupported(singleRef),
        };

        // Variadics position semantics (Requirement 6.3-6.6). A named reference that resolved
        // to a type-parameter pack is not an ordinary type: `TArgs[i]` is pack indexing gated
        // by the capability ladder, and any other bare use is the type-position error — except
        // the value-pack form `params TArgs args`, which names the pack to build its value
        // tuple (TArgs...). This runs before the array-rank branch so a pack index is never
        // misread as a `Standard.Array<TArgs, i>` construction.
        if(singleRef is NamedSingleTypeRefSingleTypeRefNode named && inner is TypeParameter { IsPack: true } pack)
        {
            return ResolvePackInTypePosition(pack, named, barePackAllowed);
        }

        // If the type reference has an array rank suffix, wrap the inner type
        // in a constructed Standard.Array<TValue, Length>.
        if(HasArrayRank(singleRef))
        {
            return ResolveArrayType(singleRef, inner);
        }

        return inner;
    }

    /// <summary>
    /// Applies pack position semantics (Requirement 6) to a named reference whose operand
    /// resolved to the type-parameter <paramref name="pack"/>:
    /// <list type="bullet">
    ///   <item><c>TArgs[i]</c> (a sized rank) is pack indexing, gated by the capability ladder
    ///     (see <see cref="ResolvePackIndex"/>).</item>
    ///   <item>A bare pack (no rank, no <c>?</c>) in the value-pack form
    ///     <c>params TArgs args</c> (<paramref name="barePackAllowed"/>) resolves to the pack
    ///     itself so the caller can build the value-pack tuple.</item>
    ///   <item>Every other bare use — a field/return/ordinary-parameter type, a scalar type
    ///     argument, or a <c>TArgs[]</c> / <c>TArgs?</c> form — is the bare-type-position error
    ///     <c>MIST_VAR_0002</c> (Requirement 6.3): a pack is a value, not a type.</item>
    /// </list>
    /// </summary>
    private IMistType ResolvePackInTypePosition(
        TypeParameter pack,
        NamedSingleTypeRefSingleTypeRefNode named,
        bool barePackAllowed)
    {
        // `TArgs[i]` — pack indexing by a comptime integer literal, under the capability ladder.
        if(named.ArrayRank is SizedArrayRankArrayRankSuffixNode sized)
        {
            return ResolvePackIndex(pack, sized, named);
        }

        // The value-pack form `params TArgs args` names the pack to form its value tuple; return
        // the pack itself (the caller wraps it in (TArgs...)). Only a plain bare name qualifies —
        // a `TArgs[]` (unsized rank) or `TArgs?` still falls through to the type-position error.
        if(barePackAllowed && named.ArrayRank is null && named.Optional is null)
        {
            return pack;
        }

        // Requirement 6.3: a pack in bare type position is an error (a pack is a value, not a
        // type). Recover as an error type so downstream analysis continues.
        context.Diagnostics.ReportAtNode(VariadicsDiagnostics.PackInTypePosition, named, null, pack.Name);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Resolves pack indexing <c>TArgs[i]</c> in type position under the capability ladder
    /// (Requirement 6.4-6.6). The comptime index <c>i</c> is the sized rank's integer literal.
    /// <list type="bullet">
    ///   <item>An unknown-length <c>Type[]</c> pack cannot prove its length is at least
    ///     <c>i + 1</c>, so indexing is rejected with <c>MIST_VAR_0003</c> (Requirement 6.4).</item>
    ///   <item>A fixed-size <c>Type[N]</c> pack permits <c>0 &lt;= i &lt; N</c>, yielding the
    ///     element type at that index (Requirement 6.5); a comptime index outside that range is
    ///     <c>MIST_VAR_0004</c> (Requirement 6.6).</item>
    /// </list>
    /// <para>
    /// While the pack is symbolic (unbound) at declaration/resolution time the concrete element
    /// type is not yet known; the accepted in-range case therefore yields the <c>unknown</c>
    /// placeholder so the valid indexing neither emits a diagnostic nor cascades an error. The
    /// concrete element type is substituted when the pack binds a concrete <c>Type[]</c> during
    /// backend monomorphization (task 14) — nothing in the program model consumes a symbolic
    /// pack element yet, matching the existing deferral for symbolic spread-generated members.
    /// </para>
    /// </summary>
    private IMistType ResolvePackIndex(
        TypeParameter pack,
        SizedArrayRankArrayRankSuffixNode sized,
        Storm.Runtime.SyntaxNode locationNode)
    {
        var index = long.TryParse(sized.Size.Lexeme, out var parsed) ? parsed : 0L;

        // Requirement 6.4: an unknown-length `Type[]:TArgs` pack proves no lower bound on its
        // length, so no comptime index is provably in range.
        if(pack.FixedPackLength is not { } length)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.UnprovenPackIndex, locationNode, null, pack.Name, index + 1);
            return ErrorType.Instance;
        }

        // Requirement 6.6: a fixed-size `Type[N]:TArgs` pack rejects a comptime index outside
        // 0 .. N-1 (for example `TArgs[N]` or a negative index).
        if(index < 0 || index >= length)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.PackIndexOutOfBounds, locationNode, null, index, pack.Name, length);
            return ErrorType.Instance;
        }

        // Requirement 6.5: in range. The symbolic element is represented by `unknown` here; the
        // concrete element type is substituted at monomorphization (see the remarks above).
        return UnknownType.Instance;
    }

    /// <summary>
    /// Resolves a reserved keyword to its corresponding type in the Standard namespace.
    /// All reserved type aliases (primitives, void) funnel through here.
    /// </summary>
    private IMistType ResolveReservedAlias(PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
    {
        var typeName = primitiveRef.PrimitiveType switch
        {
            IntTypePrimitiveTypeNode => "Int32",
            UintTypePrimitiveTypeNode => "UInt32",
            LongTypePrimitiveTypeNode => "Int64",
            UlongTypePrimitiveTypeNode => "UInt64",
            ShortTypePrimitiveTypeNode => "Int16",
            UshortTypePrimitiveTypeNode => "UInt16",
            ByteTypePrimitiveTypeNode => "Byte",
            SbyteTypePrimitiveTypeNode => "SByte",
            FloatTypePrimitiveTypeNode => "Single",
            DoubleTypePrimitiveTypeNode => "Double",
            DecimalTypePrimitiveTypeNode => "Decimal",
            BoolTypePrimitiveTypeNode => "Boolean",
            CharTypePrimitiveTypeNode => "Char",
            StringTypePrimitiveTypeNode => "String",
            RegexTypePrimitiveTypeNode => "Regex",
            _ => null,
        };

        return ResolveReservedAlias(typeName);
    }

    /// <summary>
    /// Looks up a reserved alias by simple name in the Standard namespace.
    /// </summary>
    private IMistType ResolveReservedAlias(string? typeName)
    {
        if(typeName is null)
        {
            return ErrorType.Instance;
        }

        return LookupInNamespace("Standard", typeName) ?? ErrorType.Instance;
    }

    /// <summary>
    /// Resolves a named type reference by looking up the name in the scope.
    /// </summary>
    private IMistType ResolveNamed(NamedSingleTypeRefSingleTypeRefNode namedRef, Scope scope)
    {
        var baseName = BuildBaseName(namedRef);

        // A non-generic reference resolves directly through the unified three-tier lookup.
        if(namedRef.TypeArgs is not { } typeArgList)
        {
            return ResolveNamedType(baseName, scope, namedRef);
        }

        // Generic reference. The definition is looked up first under the flat use-site
        // argument count. A variadic family, however, is registered under its (smaller)
        // declared slot count -- `Func<params TArgs, TResult>` registers as `Func<,>` yet is
        // constructed at `Func<int, string, bool>` -- so an exact-arity miss falls back to
        // searching for a variadic family of the same base name (the binding rule then folds
        // the flat arguments into the pack). The exact lookup is silent so the expected miss
        // does not surface a spurious unresolved-type diagnostic.
        var arity = GetTypeArgumentArity(typeArgList);
        var exactName = BuildArityName(baseName, arity);

        var definition = ResolveNamedTypeCore(exactName, scope, namedRef, report: false);
        if(definition is ErrorType)
        {
            // Ambiguous across imports — already reported by the core.
            return ErrorType.Instance;
        }

        definition ??= TryResolveVariadicFamily(baseName, scope);
        if(definition is null)
        {
            // Genuine miss: report the unresolved exact-arity name, as the direct lookup would.
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, namedRef, null, exactName);
            return ErrorType.Instance;
        }

        // Route through the binding rule when the construction is variadic — either the
        // definition declares a pack slot, or the use site supplies a bracketed pack group.
        var slots = GetDeclaredSlots(definition);
        if(slots is not null && (HasPackSlot(slots) || HasBracketedPackArgument(typeArgList)))
        {
            return ResolveVariadicConstructedType(definition, slots, typeArgList, scope, namedRef);
        }

        // Ordinary (non-variadic) construction: one argument per slot, interned as-is.
        var typeArgs = ResolveTypeArguments(typeArgList, scope);
        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, typeArgs);
    }

    /// <summary>
    /// Builds the base (non-arity-bearing) lookup name for a named type reference:
    /// the qualified dotted name when present, otherwise the simple identifier.
    /// </summary>
    private static string BuildBaseName(NamedSingleTypeRefSingleTypeRefNode namedRef)
    {
        return namedRef.Name.IDENTIFIERs.Length > 0
            ? QualifiedNameHelper.Build(namedRef.Name)
            : namedRef.Name.IDENTIFIER.Lexeme;
    }

    /// <summary>
    /// Searches for the variadic family named <paramref name="baseName"/> (a definition with
    /// a single bracket-free <c>params</c> pack). Because such a family is registered under
    /// its declared slot count -- not the flat use-site argument count -- it cannot be found
    /// by an exact-arity lookup once the flat count differs, so this locates it by simple name
    /// across the visible namespaces instead.
    /// <para>
    /// Crucially, the search does <em>not</em> depend on the flat argument count: a family is
    /// returned even when the use site supplies too few arguments to fill its fixed and
    /// bracketed slots, so the binding rule can then diagnose that shortfall as
    /// <c>MIST_VAR_0005</c> rather than the construction being misreported as an unresolved
    /// type. Visibility mirrors the tiered name lookup -- local/nested types first, then own
    /// namespaces, then imports -- and a dotted base name is searched only in its explicit
    /// namespace. Returns <c>null</c> when no variadic family of that name is visible.
    /// </para>
    /// </summary>
    private IMistType? TryResolveVariadicFamily(string baseName, Scope scope)
    {
        var (namespacePrefix, simpleName) = SplitQualifiedName(baseName);

        // Tier 1: a variadic family visible by simple name as a local or nested type.
        if(namespacePrefix is null)
        {
            for(var current = scope; current is not null; current = current.Parent)
            {
                foreach(var entry in current.LocalTypes)
                {
                    if(SimpleName(entry.Key) == simpleName && IsUnbracketedVariadicFamily(entry.Value))
                    {
                        return entry.Value;
                    }
                }
            }
        }

        // Collect every visible variadic family of this simple name, keyed by namespace. A
        // family is a single declaration, so at most one matches per namespace.
        var candidatesByNamespace = new Dictionary<string, IMistType>(StringComparer.Ordinal);
        foreach(var type in context.ProgramModel.TypesByFQN.Values)
        {
            if(SimpleName(type.Name) == simpleName && IsUnbracketedVariadicFamily(type))
            {
                _ = candidatesByNamespace.TryAdd(type.NamespaceName, type);
            }
        }

        if(candidatesByNamespace.Count == 0)
        {
            return null;
        }

        // Choose by visibility priority (own namespaces before imports, or the explicit
        // namespace prefix for a dotted base name).
        foreach(var ns in EnumerateVisibleNamespaces(scope, namespacePrefix))
        {
            if(candidatesByNamespace.TryGetValue(ns, out var family))
            {
                return family;
            }
        }

        return null;
    }

    /// <summary>
    /// Splits a base name into its namespace prefix (everything before the last dot, or
    /// <c>null</c> when unqualified) and its simple name (the final segment).
    /// </summary>
    private static (string? NamespacePrefix, string SimpleName) SplitQualifiedName(string baseName)
    {
        var lastDot = baseName.LastIndexOf('.');
        return lastDot < 0
            ? (null, baseName)
            : (baseName[..lastDot], baseName[(lastDot + 1)..]);
    }

    /// <summary>
    /// Strips the arity-bearing angle-bracket suffix from a registered type name, so
    /// <c>Foo&lt;,,&gt;</c> becomes <c>Foo</c>.
    /// </summary>
    private static string SimpleName(string name)
    {
        var angle = name.IndexOf('<', StringComparison.Ordinal);
        return angle < 0 ? name : name[..angle];
    }

    /// <summary>
    /// Enumerates the namespaces visible from <paramref name="scope"/> in lookup priority
    /// order: own namespaces up the scope chain first, then imported namespaces. A dotted
    /// base name resolves only against its explicit <paramref name="namespacePrefix"/>.
    /// Duplicates may be yielded; the first match wins, so they are harmless.
    /// </summary>
    private static IEnumerable<string> EnumerateVisibleNamespaces(Scope scope, string? namespacePrefix)
    {
        if(namespacePrefix is not null)
        {
            yield return namespacePrefix;
            yield break;
        }

        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current.Namespace is not null)
            {
                yield return current.Namespace;
            }
        }

        for(var current = scope; current is not null; current = current.Parent)
        {
            foreach(var imported in current.ImportedNamespaces)
            {
                yield return imported;
            }
        }
    }

    /// <summary>
    /// True when <paramref name="type"/> is a variadic family with a single bracket-free
    /// <c>params</c> pack -- the only kind whose flat use-site arity can differ from its
    /// declared slot count and therefore needs the family lookup.
    /// </summary>
    private static bool IsUnbracketedVariadicFamily(IMistType type)
    {
        var slots = GetDeclaredSlots(type);
        return slots is not null && HasUnbracketedPackSlot(slots);
    }

    /// <summary>
    /// Binds a flat (or explicitly bracketed) use-site argument list to a variadic
    /// definition's declared slots via the shared <see cref="PackBinder"/>, then interns the
    /// resulting constructed type. Each pack slot's ordered element list is lifted into a
    /// comptime <c>Type[]</c> (<see cref="TypeInterner.GetOrCreateLiftedTypeArray"/>) so that
    /// the flat form and the bracketed canonical form of the same pack denote the very same
    /// interned type (Requirement 4.4). Too few arguments to fill the fixed and bracketed
    /// slots is reported as <c>MIST_VAR_0005</c> (Requirement 4.6).
    /// </summary>
    private IMistType ResolveVariadicConstructedType(
        IMistType definition,
        IReadOnlyList<TypeParameter> slots,
        TypeArgumentListNode typeArgList,
        Scope scope,
        Storm.Runtime.SyntaxNode locationNode)
    {
        var slotKinds = new List<BindingSlotKind>(slots.Count);
        foreach(var slot in slots)
        {
            slotKinds.Add(ToBindingSlotKind(slot));
        }

        var arguments = BuildUseSiteArguments(typeArgList, scope);
        var result = PackBinder.Bind(slotKinds, arguments);

        if(result.Status == PackBindingStatus.TooFewArguments)
        {
            context.Diagnostics.ReportAtNode(
                VariadicsDiagnostics.TooFewArgumentsForBinding, locationNode, null, definition.Name);
            return ErrorType.Instance;
        }

        var canonicalArguments = new List<IMistType>(result.Slots.Count);
        foreach(var binding in result.Slots)
        {
            if(binding.IsPack)
            {
                canonicalArguments.Add(
                    context.ProgramModel.TypeInterner.GetOrCreateLiftedTypeArray(binding.Elements));
            }
            else
            {
                // A well-formed fixed slot binds exactly one scalar type; anything else is a
                // use-site shape error, recovered as an error type so analysis continues.
                canonicalArguments.Add(binding.Elements.Count == 1 ? binding.Elements[0] : ErrorType.Instance);
            }
        }

        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, canonicalArguments);
    }

    /// <summary>
    /// Maps a declared type parameter to its <see cref="BindingSlotKind"/>: the single
    /// unbracketed <c>params</c> pack, a bracketed <c>Type[]:</c> pack, or an ordinary fixed
    /// scalar slot.
    /// </summary>
    private static BindingSlotKind ToBindingSlotKind(TypeParameter slot)
    {
        return slot.PackKind switch
        {
            TypeParameterPackKind.Unbracketed => BindingSlotKind.UnbracketedPack,
            TypeParameterPackKind.Bracketed => BindingSlotKind.BracketedPack,
            _ => BindingSlotKind.Fixed,
        };
    }

    /// <summary>
    /// Returns the declared type-parameter slot list for a definition that can carry a pack
    /// (a class/struct/abstract member type, a type alias, or a newtype), or <c>null</c> for
    /// a definition kind that declares no type parameters.
    /// </summary>
    private static IReadOnlyList<TypeParameter>? GetDeclaredSlots(IMistType definition)
    {
        return definition switch
        {
            MemberType member => member.TypeParametersList,
            TypeAlias alias => alias.TypeParametersList,
            Newtype newtype => newtype.TypeParametersList,
            _ => null,
        };
    }

    private static bool HasPackSlot(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Any(s => s.IsPack);
    }

    private static bool HasUnbracketedPackSlot(IReadOnlyList<TypeParameter> slots)
    {
        return slots.Any(s => s.IsUnbracketedPack);
    }

    /// <summary>
    /// True when any use-site type argument is a bracketed pack group (<c>[...]</c>, including
    /// the empty group <c>[]</c>), which marks the construction as variadic even for a
    /// definition whose pack slot classification is not otherwise in view.
    /// </summary>
    private static bool HasBracketedPackArgument(TypeArgumentListNode typeArgList)
    {
        if(typeArgList.TypeArgument is BracketedPackTypeArgumentTypeArgumentNode)
        {
            return true;
        }

        foreach(var arg in typeArgList.TypeArguments)
        {
            if(arg is BracketedPackTypeArgumentTypeArgumentNode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves each use-site type argument into a <see cref="UseSiteArgument"/> — a scalar
    /// type or a bracketed group — preserving the order and the flat-versus-bracketed
    /// distinction the binding rule needs.
    /// </summary>
    private List<UseSiteArgument> BuildUseSiteArguments(TypeArgumentListNode typeArgList, Scope scope)
    {
        var arguments = new List<UseSiteArgument>(1 + typeArgList.TypeArguments.Length);

        AddUseSiteArgument(typeArgList.TypeArgument, scope, arguments);

        foreach(var arg in typeArgList.TypeArguments)
        {
            AddUseSiteArgument(arg, scope, arguments);
        }

        return arguments;
    }

    private void AddUseSiteArgument(TypeArgumentNode arg, Scope scope, List<UseSiteArgument> arguments)
    {
        if(arg is BracketedPackTypeArgumentTypeArgumentNode bracketed)
        {
            arguments.Add(UseSiteArgument.Group(ResolveBracketedGroup(bracketed, scope)));
        }
        else
        {
            arguments.Add(UseSiteArgument.Scalar(ResolveScalarTypeArgument(arg, scope)));
        }
    }

    /// <summary>
    /// Resolves the element types of a bracketed pack group <c>[T1, T2, ...]</c>. The empty
    /// group <c>[]</c> yields zero elements (the empty pack).
    /// </summary>
    private List<IMistType> ResolveBracketedGroup(BracketedPackTypeArgumentTypeArgumentNode bracketed, Scope scope)
    {
        var elements = new List<IMistType>();

        if(bracketed.PackElements is { } list)
        {
            elements.Add(Resolve(list.TypeReference, scope));

            foreach(var element in list.TypeReferences)
            {
                elements.Add(Resolve(element, scope));
            }
        }

        return elements;
    }

    /// <summary>
    /// Returns the number of type arguments in a type argument list, or 0 if absent.
    /// </summary>
    private static int GetTypeArgumentArity(TypeArgumentListNode? typeArgList)
    {
        if(typeArgList is null)
        {
            return 0;
        }

        // First argument + trailing arguments.
        return 1 + typeArgList.TypeArguments.Length;
    }

    /// <summary>
    /// Builds the arity-bearing lookup name for a generic type reference.
    /// E.g., ("List", 1) → "List&lt;&gt;", ("Dict", 2) → "Dict&lt;,&gt;".
    /// </summary>
    private static string BuildArityName(string baseName, int arity)
    {
        var commas = new string(',', arity - 1);
        return $"{baseName}<{commas}>";
    }

    /// <summary>
    /// Resolves a TypeArgumentList into a list of resolved types.
    /// Non-type arguments (integer literals, etc.) are reported as unsupported
    /// and replaced with ErrorType.
    /// </summary>
    public IReadOnlyList<IMistType> ResolveTypeArguments(TypeArgumentListNode typeArgList, Scope scope)
    {
        var args = new List<IMistType>(1 + typeArgList.TypeArguments.Length)
        {
            ResolveScalarTypeArgument(typeArgList.TypeArgument, scope),
        };

        foreach(var arg in typeArgList.TypeArguments)
        {
            args.Add(ResolveScalarTypeArgument(arg, scope));
        }

        return args;
    }

    /// <summary>
    /// Resolves a single non-bracketed type argument into a type: a type reference, or a
    /// lifted comptime literal (integer, hex, binary, bool, char) for a dependent slot. A
    /// bracketed pack group is not a scalar and is only meaningful in the variadic binding
    /// path (see <see cref="ResolveVariadicConstructedType"/>); reaching one here means it was
    /// supplied to a non-variadic definition, which is reported as an unsupported argument.
    /// </summary>
    private IMistType ResolveScalarTypeArgument(TypeArgumentNode arg, Scope scope)
    {
        switch(arg)
        {
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
                return Resolve(typeRefArg.Type, scope);

            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
                return LiftIntegerLiteral(intLit.Literal.Lexeme);

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                return LiftNegatedIntegerLiteral(negLit.Literal.Lexeme);

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                return LiftHexLiteral(hexLit.Literal.Lexeme);

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                return LiftBinaryLiteral(binLit.Literal.Lexeme);

            case TrueLiteralTypeArgumentTypeArgumentNode:
                return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(true, "true");

            case FalseLiteralTypeArgumentTypeArgumentNode:
                return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(false, "false");

            case CharLiteralTypeArgumentTypeArgumentNode charLit:
                return LiftCharLiteral(charLit.Literal.Lexeme);

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, arg, null, arg.GetType().Name);
                return ErrorType.Instance;
        }
    }

    private IMistType LiftIntegerLiteral(string lexeme)
    {
        var value = long.TryParse(lexeme, out var parsed) ? parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    private IMistType LiftNegatedIntegerLiteral(string lexeme)
    {
        var value = long.TryParse(lexeme, out var parsed) ? -parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, $"-{lexeme}");
    }

    private IMistType LiftHexLiteral(string lexeme)
    {
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        var value = long.TryParse(hexStr, NumberStyles.HexNumber, null, out var parsed)
            ? parsed : 0L;
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    private IMistType LiftBinaryLiteral(string lexeme)
    {
        var binStr = lexeme.StartsWith("0b", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        long value;
        try
        {
            value = Convert.ToInt64(binStr, 2);
        }
        catch
        {
            value = 0L;
        }

        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(value, lexeme);
    }

    private IMistType LiftCharLiteral(string lexeme)
    {
        // Strip surrounding quotes to get the char value.
        var charValue = lexeme.Length >= 3 ? lexeme[1] : '\0';
        return context.ProgramModel.TypeInterner.GetOrCreateLiftedType(charValue, lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name Lookup
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Looks up a type name by walking the scope chain, reporting an unresolved-type
    /// diagnostic when nothing matches. See <see cref="ResolveNamedTypeCore"/> for the
    /// resolution order.
    /// </summary>
    public IMistType ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        return ResolveNamedTypeCore(name, scope, locationNode, report: true) ?? ErrorType.Instance;
    }

    /// <summary>
    /// Core name lookup. Resolution follows C# semantics with a priority order:
    /// 1. Local types (type parameters, nested types) — cascade with shadowing.
    /// 2. Own namespace — if found in the scope's own namespace, shadows imports.
    /// 3. Imports — accumulate from ALL scope levels into a flat pool; ambiguity
    ///    is reported if multiple distinct types match across the combined imports.
    /// 4. FQN fallback — if the name contains a dot and nothing above matched,
    ///    try splitting at the last dot into namespace + type for a direct lookup.
    /// <para>
    /// Ambiguity is always reported (it is a definite error) and yields an
    /// <see cref="ErrorType"/>. When nothing matches, an unresolved-type diagnostic is
    /// reported only when <paramref name="report"/> is true (returning an
    /// <see cref="ErrorType"/>); otherwise the miss is silent and returns <c>null</c>, which
    /// lets a caller probe speculatively — for example the variadic-family fallback in
    /// <see cref="ResolveNamed"/>, where an exact-arity miss is expected before the family is
    /// found under its smaller declared slot count.
    /// </para>
    /// </summary>
    private IMistType? ResolveNamedTypeCore(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode, bool report)
    {
        // Tier 1: Walk up looking for a local type (shadowing applies).
        var current = scope;
        while(current is not null)
        {
            if(current.LocalTypes.TryGetValue(name, out var localType))
            {
                return localType;
            }

            current = current.Parent;
        }

        // Tier 2: Check own namespace at each scope level (shadows imports).
        current = scope;
        while(current is not null)
        {
            if(current.Namespace is not null)
            {
                var found = LookupInNamespace(current.Namespace, name);
                if(found is not null)
                {
                    return found;
                }
            }

            current = current.Parent;
        }

        // Tier 3: Collect all imported namespaces from the entire scope chain
        // into a flat pool. No shadowing between imports — report ambiguity if
        // multiple distinct types match.
        IMistType? match = null;
        string? matchedNamespace = null;
        string? conflictNamespace = null;

        current = scope;
        while(current is not null)
        {
            foreach(var imported in current.ImportedNamespaces)
            {
                var found = LookupInNamespace(imported, name);
                if(found is not null)
                {
                    if(match is null)
                    {
                        match = found;
                        matchedNamespace = imported;
                    }
                    else if(!ReferenceEquals(match, found))
                    {
                        conflictNamespace = imported;
                    }
                }
            }

            current = current.Parent;
        }

        if(conflictNamespace is not null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.AmbiguousType, locationNode, null,
                name, matchedNamespace!, conflictNamespace);
            return ErrorType.Instance;
        }

        if(match is not null)
        {
            return match;
        }

        // Tier 4: FQN fallback — if the name has a dot, try it directly as a
        // fully-qualified name in the flat dictionary.
        if(name.Contains('.'))
        {
            if(context.ProgramModel.TypesByFQN.TryGetValue(name, out var fqnResult))
            {
                return fqnResult;
            }
        }

        if(report)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, locationNode, null, name);
            return ErrorType.Instance;
        }

        return null;
    }

    /// <summary>
    /// Looks up a type by constructing a candidate FQN from namespace + type name
    /// and checking the flat FQN dictionary.
    /// </summary>
    private IMistType? LookupInNamespace(string namespaceName, string typeName)
    {
        var fqn = string.IsNullOrEmpty(namespaceName)
            ? typeName
            : $"{namespaceName}.{typeName}";

        if(context.ProgramModel.TypesByFQN.TryGetValue(fqn, out var typeDef))
        {
            return typeDef;
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static bool HasArrayRank(SingleTypeRefNode singleRef)
    {
        return GetArrayRank(singleRef) is not null;
    }

    private static ArrayRankSuffixNode? GetArrayRank(SingleTypeRefNode singleRef)
    {
        return singleRef switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode p => p.ArrayRank,
            NamedSingleTypeRefSingleTypeRefNode n => n.ArrayRank,
            ParenthesizedTypeRefSingleTypeRefNode pr => pr.ArrayRank,
            _ => null,
        };
    }

    /// <summary>
    /// Wraps an element type in a <c>Standard.Array&lt;,&gt;</c> constructed type.
    /// Sized arrays (<c>int[5]</c>) use a <see cref="LiftedType"/> for the length;
    /// unsized arrays (<c>int[]</c>) use <see cref="UnknownType"/>.
    /// </summary>
    private IMistType ResolveArrayType(SingleTypeRefNode singleRef, IMistType elementType)
    {
        var arrayDef = LookupInNamespace("Standard", ArrayDefinitionName);
        if(arrayDef is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, singleRef, null, "Standard.Array<,>");
            return ErrorType.Instance;
        }

        var rank = GetArrayRank(singleRef)!;
        IMistType lengthArg = rank switch
        {
            SizedArrayRankArrayRankSuffixNode sized =>
                context.ProgramModel.TypeInterner.GetOrCreateLiftedType(
                    long.TryParse(sized.Size.Lexeme, out var len) ? len : 0L,
                    sized.Size.Lexeme),
            _ => UnknownType.Instance,
        };

        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(
            arrayDef, new IMistType[] { elementType, lengthArg });
    }

    /// <summary>
    /// True when <paramref name="type"/> is a homogeneous array type <c>T[]</c> — a
    /// constructed <c>Standard.Array&lt;,&gt;</c>, the same shape <see cref="ResolveArrayType"/>
    /// produces. Used by <see cref="ParameterBuilder"/> to distinguish a Value_Level_Params
    /// (<c>params T[] xs</c>, whose declared type is a real array) from a Value_Pack
    /// (<c>params TArgs args</c>, whose declared type is a pack type parameter and becomes the
    /// tuple <c>(TArgs...)</c>). The array definition reference is preserved verbatim through
    /// interning, so a reference comparison against the canonical <c>Standard.Array&lt;,&gt;</c>
    /// definition is exact; when that definition is not registered the lookup yields
    /// <c>null</c> and this returns <c>false</c>.
    /// </summary>
    public bool IsArrayType(IMistType type)
    {
        return type is ConstructedType constructed
            && ReferenceEquals(constructed.Definition, LookupInNamespace("Standard", ArrayDefinitionName));
    }

    private IMistType ReportUnsupported(SingleTypeRefNode node)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, node.GetType().Name);
        return ErrorType.Instance;
    }
}