using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

internal sealed partial class ScopeResolver
{
    private const string PositionalAccessorPrefix = "Item";

    private readonly BuilderContext context;

    private int? widestVariadicFamily;

    internal Action<TypeAlias>? AliasCompleter { get; set; }

    public TypeInterner Interner => context.ProgramModel.TypeInterner;

    /// <summary>
    /// The most slots any variadic family in the program declares, which is how far a name has to be
    /// probed before "no such declaration" is the honest answer. Derived rather than chosen: a fixed
    /// ceiling would either be too small for some program or too large for every other.
    /// </summary>
    private int WidestVariadicFamily
    {
        get
        {
            widestVariadicFamily ??= context.ProgramModel.TypesByFQN.Values
                .OfType<IHasTypeParameters>()
                .Where(generic => PackBinding.AbsorbingSlot(generic.TypeParametersList) is not null)
                .Select(generic => generic.TypeParametersList.Count)
                .DefaultIfEmpty(0)
                .Max();

            return widestVariadicFamily.Value;
        }
    }

    public ScopeResolver(BuilderContext context)
    {
        this.context = context;
    }

    public IMistType Resolve(TypeReferenceNode typeRef, Scope scope)
    {
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            return ResolveUnion(typeRef, scope);
        }

        return Resolve(typeRef.First, scope);
    }

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

        context.ProgramModel.CompositeApplications.Record(CompositeKind.Union, constituents, typeRef, scope);
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

        context.ProgramModel.CompositeApplications.Record(CompositeKind.Tuple, elementTypes, tupleRef, scope);
        var structural = context.ProgramModel.TypeInterner.GetOrCreateTupleType(elementTypes);

        if(elementNames.Exists(name => name is not null))
        {
            return new NamedTupleType(structural, elementNames);
        }

        return structural;
    }

    /// <summary>
    /// The LAST type argument is the result and the rest are the parameters, so a single argument
    /// takes nothing -- one syntax across every arity, so the grammar needs no zero-parameter form.
    /// </summary>
    internal IMistType ResolveFunction(TypeArgumentListNode arguments, Scope scope, Storm.Runtime.SyntaxNode node)
    {
        var typeArguments = ResolveTypeArguments(arguments, scope);

        if(typeArguments.Count == 0)
        {
            ReportResolutionFailure(ProgramModelDiagnostics.FunctionTypeNeedsAResult, node, null);
            return ErrorType.Instance;
        }

        return context.ProgramModel.TypeInterner.GetOrCreateFunctionType(
            [.. typeArguments.Take(typeArguments.Count - 1)], typeArguments[^1]);
    }

    private void ResolveTupleElement(TupleTypeElementNode element, Scope scope, List<IMistType> types, List<string?> names)
    {
        types.Add(Resolve(element.Type, scope));

        var name = element.Name?.Lexeme;

        if(name is not null && IsReservedTupleElementName(name))
        {
            ReportResolutionFailure(ProgramModelDiagnostics.ReservedTupleElementName, element, null, new NameValue(name));

            // Drop the reserved name so it does not collide with the positional field.
            name = null;
        }

        names.Add(name);
    }

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

    public IMistType Resolve(SingleTypeRefNode singleRef, Scope scope)
    {
        if(PackTypePositions.NarrowedElement(singleRef, scope, context.Diagnostics, MarkResolutionFailure) is { } narrowed)
        {
            return Optional(narrowed, singleRef, scope);
        }

        var inner = singleRef switch
        {
            VoidSingleTypeRefSingleTypeRefNode => LookupWellKnown(WellKnownTypes.Void),
            UnknownSingleTypeRefSingleTypeRefNode => UnknownType.Instance,
            PrimitiveSingleTypeRefSingleTypeRefNode { Pure: not null } primitiveRef => PureFunctionReference.Apply(
                ResolveReservedAlias(primitiveRef), primitiveRef, context, MarkResolutionFailure),
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolveReservedAlias(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode { Pure: not null } namedRef => PureFunctionReference.Apply(
                ResolveNamed(namedRef.Name, namedRef.TypeArgs, scope, namedRef), namedRef, context, MarkResolutionFailure),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef.Name, namedRef.TypeArgs, scope, namedRef),
            NestedGenericSingleTypeRefSingleTypeRefNode nestedRef => ResolveNestedGeneric(
                nestedRef.Outer, nestedRef.OuterArgs, nestedRef.Nested, nestedRef.TypeArgs, scope, nestedRef),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => PackTypePositions.Parenthesized(Resolve(parenRef.Inner, scope), Interner),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
            FuncSingleTypeRefSingleTypeRefNode { Pure: not null } funcRef => PureFunctionReference.Apply(
                ResolveFunction(funcRef.TypeArgs, scope, funcRef), funcRef, context, MarkResolutionFailure),
            FuncSingleTypeRefSingleTypeRefNode funcRef => ResolveFunction(funcRef.TypeArgs, scope, funcRef),
            SpreadSingleTypeRefSingleTypeRefNode spread => ResolveSpread(spread, scope),
            _ => ReportUnsupported(singleRef),
        };

        // A sized rank is left to pass: `TArgs[1]` indexes the pack, where `TArgs[]` is an array of it.
        if(GetArrayRank(singleRef) is not SizedArrayRankArrayRankSuffixNode)
        {
            inner = PackPosition.AsSingleType(inner, scope, singleRef, context.Diagnostics, MarkResolutionFailure);
        }

        if(GetArrayRank(singleRef) is { } rank)
        {
            if(PackTypePositions.Indexed(inner, rank, singleRef, context.Diagnostics, MarkResolutionFailure) is { } indexed)
            {
                inner = indexed;
            }
            else
            {
                context.ProgramModel.CompositeApplications.Record(CompositeKind.Array, [inner], singleRef, scope);
                inner = ResolveArrayType(singleRef, inner, scope);
            }
        }

        return Optional(inner, singleRef, scope);
    }

    private IMistType Optional(IMistType inner, SingleTypeRefNode singleRef, Scope scope)
    {
        if(HasOptionalToken(singleRef) && inner is not ErrorType)
        {
            context.ProgramModel.OptionalApplications.Record(inner, singleRef, scope);
            return WrapOptional(inner);
        }

        return inner;
    }

    private static bool HasOptionalToken(SingleTypeRefNode singleRef)
    {
        return singleRef switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => primitiveRef.Optional is not null,
            NamedSingleTypeRefSingleTypeRefNode namedRef => namedRef.Optional is not null,
            NestedGenericSingleTypeRefSingleTypeRefNode nestedRef => nestedRef.Optional is not null,
            FuncSingleTypeRefSingleTypeRefNode funcRef => funcRef.Optional is not null,
            ParenthesizedTypeRefSingleTypeRefNode parenRef => parenRef.Optional is not null,
            _ => false,
        };
    }

    /// <summary>
    /// Desugars the optional suffix: <c>T?</c> IS the union of T with void, one construct with
    /// no type of its own. Applied after the array wrap, so <c>int[]?</c> is an optional array
    /// rather than an array of optionals.
    /// </summary>
    private IMistType WrapOptional(IMistType inner)
    {
        return context.ProgramModel.TypeInterner.GetOrCreateUnionType(
            [inner, LookupWellKnown(WellKnownTypes.Void)]);
    }

    private IMistType ResolveReservedAlias(PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
    {
        var wellKnown = primitiveRef.PrimitiveType switch
        {
            IntTypePrimitiveTypeNode => WellKnownTypes.Int32,
            UintTypePrimitiveTypeNode => WellKnownTypes.UInt32,
            LongTypePrimitiveTypeNode => WellKnownTypes.Int64,
            UlongTypePrimitiveTypeNode => WellKnownTypes.UInt64,
            ShortTypePrimitiveTypeNode => WellKnownTypes.Int16,
            UshortTypePrimitiveTypeNode => WellKnownTypes.UInt16,
            ByteTypePrimitiveTypeNode => WellKnownTypes.Byte,
            SbyteTypePrimitiveTypeNode => WellKnownTypes.SByte,
            FloatTypePrimitiveTypeNode => WellKnownTypes.Single,
            DoubleTypePrimitiveTypeNode => WellKnownTypes.Double,
            DecimalTypePrimitiveTypeNode => WellKnownTypes.Decimal,
            BoolTypePrimitiveTypeNode => WellKnownTypes.Boolean,
            CharTypePrimitiveTypeNode => WellKnownTypes.Char,
            StringTypePrimitiveTypeNode => WellKnownTypes.String,
            RegexTypePrimitiveTypeNode => WellKnownTypes.Regex,
            _ => null,
        };

        if(wellKnown is null)
        {
            return ErrorType.Instance;
        }

        return LookupWellKnown(wellKnown);
    }

    public IMistType LookupWellKnown(WellKnownType wellKnown)
    {
        return context.ProgramModel.FindType(wellKnown.Namespace, wellKnown.Name) ?? ErrorType.Instance;
    }

    internal IMistType ResolveNamed(
        QualifiedIdentifierNode name, TypeArgumentListNode? typeArguments, Scope scope, Storm.Runtime.SyntaxNode node)
    {
        // Qualified if dotted, simple otherwise; the arity marker is the resolver's to add.
        var stem = name.IDENTIFIERs.Length > 0
            ? QualifiedNameHelper.Build(name)
            : name.IDENTIFIER.Lexeme;

        var arity = GetTypeArgumentArity(typeArguments);

        if(arity == 0 && stem.Split('.') is [var parameterName, var slotName]
            && SlotThrough(parameterName, slotName, scope) is { } slot)
        {
            return slot;
        }

        var baseType = ResolveGenericDefinition(stem, arity, scope, node);

        if(baseType is ErrorType)
        {
            return ErrorType.Instance;
        }

        if(baseType is TypeAlias alias)
        {
            return ExpandAlias(alias, typeArguments, scope, node);
        }

        // A variadic family is constructed even when NONE are written, since absorbing nothing is a
        // binding like any other -- a bare `Action` is Action<[]>, not an unconstructed definition.
        var written = typeArguments is { } typeArgList
            ? ResolveTypeArguments(typeArgList, scope, baseType)
            : [];

        if(typeArguments is not null || Absorbs(baseType))
        {
            var typeArgs = BindPack(baseType, written, node);

            if(AsFunctionType(baseType, typeArgs) is { } functionType)
            {
                return functionType;
            }

            var enclosing = InheritedEnclosing(baseType, scope);
            context.ProgramModel.ConstructorConstraintClosures.RecordType(baseType, typeArgs, node, enclosing, scope);
            return Construct(baseType, typeArgs, scope, enclosing);
        }

        return baseType;
    }

    /// <summary>
    /// <c>T.Output</c>: the associated type slot named on an abstract that constrains the type
    /// parameter <c>T</c>. Resolved to the slot itself, which is what the abstract's own members are
    /// typed with, so a value one of them returns and a local declared <c>T.Output</c> agree without a
    /// substitution. Two parameters constrained by the same abstract therefore share the slot type,
    /// which is the one distinction this reading cannot draw.
    /// </summary>
    private static IMistType? SlotThrough(string parameterName, string slotName, Scope scope)
    {
        if(TryLookupType(parameterName, scope) is not TypeParameter parameter || parameter is TypeSlot)
        {
            return null;
        }

        foreach(var constraint in parameter.ConstraintsList.OfType<TypeConstraint>())
        {
            var definition = constraint.ConstraintType is ConstructedType constructed
                ? constructed.Definition
                : constraint.ConstraintType;

            if(definition is not AbstractType abstractType)
            {
                continue;
            }

            foreach(var slot in abstractType.TypeSlotsList)
            {
                if(MistType.StripArity(slot.Name) == slotName && slot.TypeParametersList.Count == 0)
                {
                    return slot;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A nested type reached through a written instantiation: the outer instantiation resolved as any
    /// generic reference is, then each further segment looked up as a nested type on the definition
    /// reached so far and carried under the instantiation -- the same carrying a member's declared type
    /// gets, so a local written this way and one inferred from a call along the same path hold one type.
    /// A generic last segment is constructed over its own arguments, enclosed by its declaring type as the
    /// path to it closes that type -- the instantiation written, or the base it was reached through.
    /// </summary>
    internal IMistType ResolveNestedGeneric(
        QualifiedIdentifierNode outer,
        TypeArgumentListNode outerArgs,
        QualifiedIdentifierNode nested,
        TypeArgumentListNode? typeArgs,
        Scope scope,
        Storm.Runtime.SyntaxNode node)
    {
        var stem = outer.IDENTIFIERs.Length > 0 ? QualifiedNameHelper.Build(outer) : outer.IDENTIFIER.Lexeme;
        var definition = ResolveGenericDefinition(stem, GetTypeArgumentArity(outerArgs), scope, node);

        if(definition is ErrorType)
        {
            return ErrorType.Instance;
        }

        var interner = context.ProgramModel.TypeInterner;
        var arguments = BindPack(definition, ResolveTypeArguments(outerArgs, scope, definition), node);
        IMistType current;

        if(definition is TypeAlias alias)
        {
            current = ExpandAlias(alias, arguments, node, scope);
        }
        else
        {
            context.ProgramModel.ConstructorConstraintClosures.RecordType(definition, arguments, node, boundIn: scope);
            current = interner.GetOrCreateConstructedType(definition, arguments);
        }

        var segments = new List<Storm.Runtime.Token> { nested.IDENTIFIER };
        segments.AddRange(nested.IDENTIFIERs);

        for(var i = 0; i < segments.Count; i++)
        {
            var name = segments[i].Lexeme;
            var generic = i == segments.Count - 1 && typeArgs is not null;
            var lookupName = generic ? BuildArityName(name, GetTypeArgumentArity(typeArgs)) : name;

            if(current is not ConstructedType instantiation || instantiation.Definition is not MistType declaring
                || TypeMemberLookup.Find(declaring, LookupRequest.Member(lookupName, MemberVisibility.None, node))
                    is not TypeResult { Type: DeclaredType member } found)
            {
                ReportResolutionFailure(ProgramModelDiagnostics.UnresolvedType, node, null, new NameValue($"{TypeDisplay.Mist(current)}.{name}"));

                return ErrorType.Instance;
            }

            context.ProgramModel.TypeNamings.Record(member, scope, node);
            var substitution = TypeSubstitution.ForPath(instantiation, found.BaseConstructions, interner);

            if(generic)
            {
                var own = BindPack(member, ResolveTypeArguments(typeArgs!, scope, member), node);
                var enclosing = substitution.EnclosingInstantiation(member.DeclaringType, interner);
                context.ProgramModel.ConstructorConstraintClosures.RecordType(member, own, node, enclosing, scope);
                current = interner.GetOrCreateConstructedType(member, own, enclosing);
            }
            else
            {
                current = substitution.Apply(member is TypeAlias { AliasedType: { } aliased } ? aliased : member, interner);
            }
        }

        return current;
    }

    public IMistType Construct(IMistType definition, IReadOnlyList<IMistType> typeArguments, Scope scope)
    {
        return Construct(definition, typeArguments, scope, InheritedEnclosing(definition, scope));
    }

    public IMistType Construct(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, Scope scope, ConstructedType? enclosing)
    {
        var interner = context.ProgramModel.TypeInterner;

        return CloneRenaming.Rename(scope, definition, typeArguments, interner)
            ?? interner.GetOrCreateConstructedType(definition, typeArguments, enclosing);
    }

    /// <summary>
    /// The instantiation a nested generic named by its simple name is carried under when the scope
    /// found it through a constructed base: inside <c>Deriv : Outer of Score</c>, <c>Inner of U</c> is
    /// Outer of Score's, as a member of Outer read there is typed through the same base. Null where it
    /// was found in its declaration's own body, where a definition stays itself.
    /// </summary>
    public ConstructedType? InheritedEnclosing(IMistType definition, Scope scope)
    {
        if(definition is not MemberType { DeclaringType: { } declaring } nested
            || scope.Lookup(new LookupRequest(nested.Name, MemberVisibility.None, null))
                is not TypeResult { BaseConstructions: [_, ..] path } found
            || !ReferenceEquals(found.Type, nested))
        {
            return null;
        }

        var interner = context.ProgramModel.TypeInterner;

        return TypeSubstitution.ForPath(receiver: null, path, interner).EnclosingInstantiation(declaring, interner);
    }

    public IMistType? FindGenericDefinition(string stem, int arity, Scope scope)
    {
        stem = ClrCallableNames.StandardFamily(stem, arity) ?? stem;
        var lookupName = arity > 0 ? BuildArityName(stem, arity) : stem;

        return TryLookupType(lookupName, scope) ?? FindByArity(lookupName, stem, arity, scope);
    }

    /// <summary>
    /// The definition a generic reference names, whether its arity matches a declaration's slot
    /// count or is absorbed by a pack. Shared so that every place naming a generic type agrees --
    /// a type reference, an alias expansion and a `new` all reach a variadic family the same way.
    /// </summary>
    public IMistType ResolveGenericDefinition(
        string stem, int arity, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        stem = ClrCallableNames.StandardFamily(stem, arity) ?? stem;
        var lookupName = arity > 0 ? BuildArityName(stem, arity) : stem;

        if(FindByArity(lookupName, stem, arity, scope) is { } absorbing)
        {
            context.ProgramModel.TypeNamings.Record(absorbing, scope, locationNode);
            return absorbing;
        }

        return ResolveNamedType(lookupName, scope, locationNode);
    }

    /// <summary>
    /// The declaration a flat argument list is for, when the exact arity does not name one. Probes
    /// the slot counts a pack could absorb the difference into -- a family with S slots accepts any
    /// count from S-1 upward -- and accepts only a declaration that actually has a pack, so an
    /// ordinary arity mismatch still fails as one.
    /// </summary>
    private IMistType? FindByArity(string lookupName, string stem, int arity, Scope scope)
    {
        if(TryLookupType(lookupName, scope) is not null)
        {
            return null;
        }

        for(var slots = 1; slots <= Math.Max(arity + 1, WidestVariadicFamily); slots++)
        {
            if(TryLookupType(BuildArityName(stem, slots), scope) is { } candidate
                && Absorbs(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool Absorbs(IMistType type)
    {
        return type is IHasTypeParameters generic
            && PackBinding.AbsorbingSlot(generic.TypeParametersList) is not null;
    }

    private static IMistType? TryLookupType(string name, Scope scope)
    {
        return scope.Lookup(new LookupRequest(name, MemberVisibility.None, null)) is TypeResult found
            ? found.Type
            : null;
    }

    /// <summary>
    /// A construction of <c>Standard.Func</c> collapsed into the structural function type it means.
    /// Null for anything else.
    /// <para>
    /// The only place the two spellings can meet: Func is nominal and <c>func</c> is structural, so
    /// without this a lambda assigned to a Func has no parameter types to read, and a Func-typed value
    /// cannot be invoked as a function. Collapsing needs the pack already bound.
    /// </para>
    /// </summary>
    public IMistType? AsFunctionType(IMistType definition, IReadOnlyList<IMistType> typeArgs)
    {
        if(definition.FullyQualifiedName != WellKnownTypes.Func.FullyQualifiedName
            || typeArgs is not [LiftedType { Value: TypePackValue pack }, { } returnType])
        {
            return null;
        }

        return context.ProgramModel.TypeInterner.GetOrCreateFunctionType([.. pack.Elements], returnType);
    }

    public IReadOnlyList<IMistType> BindPack(
        IMistType baseType, IReadOnlyList<IMistType> typeArgs, Storm.Runtime.SyntaxNode locationNode)
    {
        if(baseType is not IHasTypeParameters generic
            || PackBinding.AbsorbingSlot(generic.TypeParametersList) is not { } absorbing)
        {
            return typeArgs;
        }

        var parameters = generic.TypeParametersList;
        var domain = parameters[absorbing].Domain
            ?? ConstructArray(LookupWellKnown(WellKnownTypes.Type), length: null);

        if(PackBinding.Bind(
            parameters, typeArgs, absorbing, context.ProgramModel.TypeInterner, domain) is { } bound)
        {
            return bound;
        }

        ReportResolutionFailure(ProgramModelDiagnostics.TooFewTypeArgumentsForPack, locationNode, null, DiagnosticSnapshot.Type(baseType, MistType.StripArity(baseType.Name)), new QuantityValue(parameters.Count - 1), new QuantityValue(typeArgs.Count));

        return typeArgs;
    }

    private IMistType ExpandAlias(
        TypeAlias alias, TypeArgumentListNode? typeArguments, Scope scope, Storm.Runtime.SyntaxNode node)
    {
        // Bound before substituting, exactly as a class's arguments are: an alias declaring a pack
        // has the same gap between its slot count and a flat use site, and substituting by ordinal
        // across that gap silently drops the surplus.
        var typeArgs = typeArguments is { } typeArgList
            ? BindPack(alias, ResolveTypeArguments(typeArgList, scope, alias), node)
            : Absorbs(alias) ? BindPack(alias, [], node) : null;

        return ExpandAlias(alias, typeArgs, node, scope);
    }

    /// <summary>
    /// A type alias is transparent: a use names the aliased type, never the alias. Expanding
    /// here, at resolution, is what makes the Optional alias over int and <c>int?</c> reach the
    /// same interned union, so every later phase sees one type. An alias whose own target is
    /// not resolved yet -- an alias naming another alias, ahead of it in the Phase 2a pass --
    /// is completed on demand through <see cref="AliasCompleter"/>, so an expansion never
    /// returns another alias: a chain bottoms out and a cycle resolves to an error type.
    /// </summary>
    internal IMistType ExpandAlias(TypeAlias alias, IReadOnlyList<IMistType>? typeArgs, Storm.Runtime.SyntaxNode? node = null)
    {
        return ExpandAlias(alias, typeArgs, node, boundIn: null);
    }

    internal IMistType ExpandAlias(TypeAlias alias, IReadOnlyList<IMistType>? typeArgs, Storm.Runtime.SyntaxNode? node, Scope? boundIn)
    {
        if(alias.AliasedType is null)
        {
            _ = WithSlotValues(null, () =>
            {
                AliasCompleter?.Invoke(alias);
                return alias;
            });
        }

        if(alias.IsFailed)
        {
            MarkResolutionFailure();
        }

        if(alias.AliasedType is not { } aliased)
        {
            return alias;
        }

        if(typeArgs is null)
        {
            return aliased;
        }

        if(node is not null)
        {
            context.ProgramModel.ConstructorConstraintClosures.RecordType(alias, typeArgs, node, boundIn: boundIn);
            context.ProgramModel.CompositeApplications.RecordClosing(aliased, alias.TypeParametersList, typeArgs, node, boundIn);
        }

        return TypeSubstitution
            .ForParameters(alias.TypeParametersList, typeArgs)
            .Apply(aliased, context.ProgramModel.TypeInterner);
    }
}