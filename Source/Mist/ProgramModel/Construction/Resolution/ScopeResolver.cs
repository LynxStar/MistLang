using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Resolves AST type reference nodes into <see cref="IMistType"/> by performing
/// scope-based name lookup against the program model.
/// </summary>
internal sealed class ScopeResolver
{
    /// <summary>
    /// The prefix shared by every reserved positional tuple accessor
    /// (<c>Item1</c>, <c>Item2</c>, ...). The positional digits follow this prefix.
    /// </summary>
    private const string PositionalAccessorPrefix = "Item";

    private readonly BuilderContext context;

    /// <summary>
    /// Builds an alias whose target is not resolved yet, so <see cref="ExpandAlias(TypeAlias,
    /// IReadOnlyList{IMistType}?, Storm.Runtime.SyntaxNode?)"/> can expand chains regardless of declaration order. Set by
    /// <see cref="ProgramModelBuilder"/> to the Phase 2a alias builder; cycle detection lives
    /// with the builder, which is why this completes rather than merely tolerates a null target.
    /// </summary>
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

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to an <see cref="IMistType"/>.
    /// </summary>
    public IMistType Resolve(TypeReferenceNode typeRef, Scope scope)
    {
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            return ResolveUnion(typeRef, scope);
        }

        return Resolve(typeRef.First, scope);
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

    /// <summary>
    /// Resolves a function type reference. The LAST type argument is the result and the rest are the
    /// parameters, so <c>func&lt;bool&gt;</c> takes nothing -- one syntax across every arity, which is
    /// what makes the grammar need no separate zero-parameter form.
    /// </summary>
    internal IMistType ResolveFunction(TypeArgumentListNode arguments, Scope scope, Storm.Runtime.SyntaxNode node)
    {
        var typeArguments = ResolveTypeArguments(arguments, scope);

        if(typeArguments.Count == 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.FunctionTypeNeedsAResult, node, null);
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
    /// Resolves a single (non-union) type reference node.
    /// </summary>
    public IMistType Resolve(SingleTypeRefNode singleRef, Scope scope)
    {
        var inner = singleRef switch
        {
            VoidSingleTypeRefSingleTypeRefNode => LookupWellKnown(WellKnownTypes.Void),
            UnknownSingleTypeRefSingleTypeRefNode => UnknownType.Instance,
            PrimitiveSingleTypeRefSingleTypeRefNode { Pure: not null } primitiveRef => PureFunctionReference.Apply(
                ResolveReservedAlias(primitiveRef), primitiveRef, context),
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolveReservedAlias(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode { Pure: not null } namedRef => PureFunctionReference.Apply(
                ResolveNamed(namedRef.Name, namedRef.TypeArgs, scope, namedRef), namedRef, context),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef.Name, namedRef.TypeArgs, scope, namedRef),
            NestedGenericSingleTypeRefSingleTypeRefNode nestedRef => ResolveNestedGeneric(
                nestedRef.Outer, nestedRef.OuterArgs, nestedRef.Nested, nestedRef.TypeArgs, scope, nestedRef),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => PackTypePositions.Parenthesized(Resolve(parenRef.Inner, scope), Interner),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
            FuncSingleTypeRefSingleTypeRefNode { Pure: not null } funcRef => PureFunctionReference.Apply(
                ResolveFunction(funcRef.TypeArgs, scope, funcRef), funcRef, context),
            FuncSingleTypeRefSingleTypeRefNode funcRef => ResolveFunction(funcRef.TypeArgs, scope, funcRef),
            SpreadSingleTypeRefSingleTypeRefNode spread => ResolveSpread(spread, scope),
            _ => ReportUnsupported(singleRef),
        };

        // A sized rank is left to pass: `TArgs[1]` indexes the pack, where `TArgs[]` is an array of it.
        if(GetArrayRank(singleRef) is not SizedArrayRankArrayRankSuffixNode)
        {
            inner = PackPosition.AsSingleType(inner, scope, singleRef, context.Diagnostics);
        }

        // If the type reference has an array rank suffix, wrap the inner type
        // in a constructed Standard.Array<TValue, Length>.
        if(GetArrayRank(singleRef) is { } rank)
        {
            inner = PackTypePositions.Indexed(inner, rank, singleRef, context.Diagnostics) ?? ResolveArrayType(singleRef, inner);
        }

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

    /// <summary>
    /// Resolves a reserved keyword to its corresponding type in the Standard namespace.
    /// All reserved type aliases (primitives, void) funnel through here.
    /// </summary>
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

    /// <summary>
    /// Resolves a <see cref="WellKnownType"/> by looking up its name in its namespace.
    /// </summary>
    public IMistType LookupWellKnown(WellKnownType wellKnown)
    {
        return context.ProgramModel.FindType(wellKnown.Namespace, wellKnown.Name) ?? ErrorType.Instance;
    }

    /// <summary>
    /// Resolves a named type reference by looking up the name in the scope.
    /// </summary>
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

        // If type arguments are present, resolve them and wrap in a ConstructedType. A variadic
        // family is constructed even when NONE are written, since absorbing nothing is a binding
        // like any other -- a bare `Action` is Action<[]>, not an unconstructed definition.
        var written = typeArguments is { } typeArgList
            ? ResolveTypeArguments(typeArgList, scope)
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
    /// The definition a generic reference names, whether its arity matches a declaration's slot
    /// count or is absorbed by a pack. Shared so that every place naming a generic type agrees --
    /// a type reference, an alias expansion and a `new` all reach a variadic family the same way.
    /// </summary>
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
    /// <c>Box&lt;int&gt;.Wrapper</c> in a type position: the outer instantiation resolved as any generic
    /// reference is, then each further segment looked up as a nested type on the definition reached so
    /// far and carried under the instantiation -- the same carrying a member's declared type gets, so a
    /// local written this way and one inferred from <c>Box&lt;int&gt;.Wrapper.Make()</c> hold one type.
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
        var arguments = BindPack(definition, ResolveTypeArguments(outerArgs, scope), node);
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
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedType, node, null, $"{TypeDisplay.Mist(current)}.{name}");

                return ErrorType.Instance;
            }

            context.ProgramModel.TypeNamings.Record(member, scope, node);
            var substitution = TypeSubstitution.ForPath(instantiation, found.BaseConstructions, interner);

            if(generic)
            {
                var own = BindPack(member, ResolveTypeArguments(typeArgs!, scope), node);
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
        var lookupName = arity > 0 ? BuildArityName(stem, arity) : stem;

        return TryLookupType(lookupName, scope) ?? FindByArity(lookupName, stem, arity, scope);
    }

    public IMistType ResolveGenericDefinition(
        string stem, int arity, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
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
    /// THE TWO SPELLINGS HAVE TO MEET SOMEWHERE, and this is the only place they can: Func is
    /// nominal and <c>func&lt;...&gt;</c> is structural, so without this a lambda assigned to a
    /// Func has no parameter types to read, and a Func-typed value cannot be invoked as a function.
    /// Collapsing needs the pack already bound, which is why it could not be done before packs
    /// existed -- see <see cref="FunctionType"/>, which has said so all along.
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

        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.TooFewTypeArgumentsForPack, locationNode, null,
            MistType.StripArity(baseType.Name), parameters.Count - 1, typeArgs.Count);

        return typeArgs;
    }

    private IMistType ExpandAlias(
        TypeAlias alias, TypeArgumentListNode? typeArguments, Scope scope, Storm.Runtime.SyntaxNode node)
    {
        // Bound before substituting, exactly as a class's arguments are: an alias declaring a pack
        // has the same gap between its slot count and a flat use site, and substituting by ordinal
        // across that gap silently drops the surplus.
        var typeArgs = typeArguments is { } typeArgList
            ? BindPack(alias, ResolveTypeArguments(typeArgList, scope), node)
            : Absorbs(alias) ? BindPack(alias, [], node) : null;

        return ExpandAlias(alias, typeArgs, node, scope);
    }

    /// <summary>
    /// A type alias is transparent: a use names the aliased type, never the alias. Expanding
    /// here, at resolution, is what makes <c>Optional&lt;int&gt;</c> and <c>int?</c> reach the
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
            AliasCompleter?.Invoke(alias);
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
        }

        return TypeSubstitution
            .ForParameters(alias.TypeParametersList, typeArgs)
            .Apply(aliased, context.ProgramModel.TypeInterner);
    }

    /// <summary>
    /// Returns the number of type arguments in a type argument list, or 0 if absent.
    /// </summary>
    internal static int GetTypeArgumentArity(TypeArgumentListNode? typeArgList)
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
    internal static string BuildArityName(string baseName, int arity)
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
        var args = new List<IMistType>();

        ResolveTypeArgument(typeArgList.TypeArgument, scope, args);

        foreach(var arg in typeArgList.TypeArguments)
        {
            ResolveTypeArgument(arg, scope, args);
        }

        return args;
    }

    private void ResolveTypeArgument(TypeArgumentNode arg, Scope scope, List<IMistType> args)
    {
        switch(arg)
        {
            case TypeReferenceTypeArgumentTypeArgumentNode typeRefArg:
                args.Add(Resolve(typeRefArg.Type, scope));
                break;

            case IntegerLiteralTypeArgumentTypeArgumentNode intLit:
                args.Add(LiftIntegerLiteral(intLit, intLit.Literal.Lexeme, negated: false));
                break;

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                args.Add(LiftIntegerLiteral(negLit, negLit.Literal.Lexeme, negated: true));
                break;

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                args.Add(LiftIntegerLiteral(hexLit, hexLit.Literal.Lexeme, negated: false));
                break;

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                args.Add(LiftIntegerLiteral(binLit, binLit.Literal.Lexeme, negated: false));
                break;

            case TrueLiteralTypeArgumentTypeArgumentNode:
                args.Add(LiftBool(true));
                break;

            case FalseLiteralTypeArgumentTypeArgumentNode:
                args.Add(LiftBool(false));
                break;

            case CharLiteralTypeArgumentTypeArgumentNode charLit:
                args.Add(LiftCharLiteral(charLit.Literal.Lexeme));
                break;

            // Brackets supply a pack DIRECTLY, which is what lets a list carry more than one: the
            // argument count then matches the slot count, so nothing has to work out where one pack
            // ends and the next begins.
            case BracketedPackTypeArgumentTypeArgumentNode bracketed:
                args.Add(ResolveBracketedPack(bracketed, scope));
                break;

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, arg, null, arg.GetType().Name);
                args.Add(ErrorType.Instance);
                break;
        }
    }

    /// <summary>
    /// A bracketed pack argument, lifted to the same comptime <c>Type[]</c> value the binding rule
    /// produces from a flat list -- so <c>Foo&lt;[int, string]&gt;</c> and <c>Foo&lt;int, string&gt;</c>
    /// are one type rather than two spellings that happen to agree.
    /// </summary>
    private IMistType ResolveBracketedPack(
        BracketedPackTypeArgumentTypeArgumentNode bracketed, Scope scope)
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

        return context.ProgramModel.TypeInterner.LiftPack(
            ConstructArray(LookupWellKnown(WellKnownTypes.Type), length: null),
            new TypePackValue(elements));
    }

    /// <summary>
    /// Lifts an integral type argument as an <c>int</c>. Every integral value the
    /// TypeArgument grammar admits sits in the int domain — dependent parameters are
    /// constrained by refinements over int, such as <c>ZeroOrPositiveInt</c> — so the
    /// lifted type does not vary with the literal's spelling.
    /// </summary>
    private IMistType LiftInt(long value)
    {
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Int32), value);
    }

    private IMistType LiftBool(bool value)
    {
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Boolean), value);
    }

    private IMistType LiftIntegerLiteral(TypeArgumentNode node, string lexeme, bool negated)
    {
        var magnitude = IntegralConstant.Of(IntegerLiterals.Read(lexeme)?.Value);
        var value = negated ? -magnitude : magnitude;

        if(value is { } lifted && lifted >= long.MinValue && lifted <= long.MaxValue)
        {
            return LiftInt((long)lifted);
        }

        context.Diagnostics.ReportAtNode(
            ProgramModelDiagnostics.NumericLiteralOutOfRange, node, null, negated ? $"-{lexeme}" : lexeme, WellKnownTypes.Int64.Keyword ?? WellKnownTypes.Int64.Name);
        return ErrorType.Instance;
    }

    private IMistType LiftCharLiteral(string lexeme)
    {
        // Strip surrounding quotes to get the char value.
        var charValue = lexeme.Length >= 3 ? lexeme[1] : '\0';
        return context.ProgramModel.TypeInterner.LiftLiteral(LookupWellKnown(WellKnownTypes.Char), charValue);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name Lookup
    //
    // Resolving a bare name is the scopes' own job -- see Scope.Lookup. What remains here
    // are the two callers that need something narrower than a raw symbol: a type reference,
    // which must reject non-types, and a qualification step off a namespace.
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One qualification step off a namespace. Exposed here so binders have a single resolution
    /// facade to talk to; the semantics are <see cref="Model.ProgramModel.LookupQualified"/>'s.
    /// </summary>
    public LookupResult LookupQualified(string namespaceName, string memberName)
    {
        return context.ProgramModel.LookupQualified(namespaceName, memberName);
    }

    /// <summary>
    /// Looks up a type name through the scope chain and filters to types only. Emits
    /// diagnostics for wrong-kind or not-found.
    /// <para>
    /// Requests <see cref="MemberVisibility.None"/>: a type-reference position can never
    /// denote a field, property or method, so members are excluded from consideration rather
    /// than matched and then rejected. Excluding them lets the lookup continue outward, so a
    /// field sharing its name with a type does not mask the type here -- while in an
    /// expression position, where both are legal, the field still wins.
    /// </para>
    /// </summary>
    public IMistType ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        // `object` is a SPELLING of unknown rather than a type of its own. Mist's top type is
        // unknown, which the backend lowers to object, and a program naming the C# spelling means the
        // same thing -- so it resolves here rather than failing to find a type nothing declares.
        if(string.Equals(name, "object", StringComparison.Ordinal))
        {
            return UnknownType.Instance;
        }

        var result = scope.Lookup(new LookupRequest(name, MemberVisibility.None, locationNode));

        switch(result)
        {
            case TypeResult typeResult:
                context.ProgramModel.TypeNamings.Record(typeResult.Type, scope, locationNode);
                return typeResult.Type;

            case AmbiguousResult:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.AmbiguousType, locationNode, null,
                    name, "<scope>", "<scope>");
                return ErrorType.Instance;

            case VariableResult:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedType, locationNode, null, name);
                return ErrorType.Instance;

            default:
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.UnresolvedType, locationNode, null, name);
                return ErrorType.Instance;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ArrayRankSuffixNode? GetArrayRank(SingleTypeRefNode singleRef)
    {
        return singleRef switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode p => p.ArrayRank,
            NamedSingleTypeRefSingleTypeRefNode n => n.ArrayRank,
            NestedGenericSingleTypeRefSingleTypeRefNode nested => nested.ArrayRank,
            ParenthesizedTypeRefSingleTypeRefNode pr => pr.ArrayRank,
            FuncSingleTypeRefSingleTypeRefNode func => func.ArrayRank,
            _ => null,
        };
    }

    /// <summary>
    /// Constructs <c>Standard.Array&lt;elementType, length&gt;</c>. A null
    /// <paramref name="length"/> means the length is not statically known and yields
    /// <c>Array&lt;elementType, unknown&gt;</c>; a known length is lifted into a
    /// <see cref="LiftedType"/> so <c>ArrayBounds</c> can constrain indexing.
    /// <para>
    /// This is the single place an array type is built. Callers that hold array syntax
    /// report their own diagnostic when this returns <see cref="ErrorType"/>, which
    /// happens only when <c>Standard.Array&lt;,&gt;</c> is not registered.
    /// </para>
    /// </summary>
    /// <summary>
    /// Constructs <c>Standard.Task&lt;result&gt;</c>. A method with no result asks for
    /// <c>Task&lt;void&gt;</c> rather than a second type, void being the unit value here.
    /// </summary>
    public IMistType ConstructTask(IMistType result)
    {
        return ConstructTask(WellKnownTypes.TaskGeneric, result);
    }

    /// <summary>
    /// The same construction over either task kind. They differ in what they promise about
    /// allocation and in nothing this layer cares about, so the caller names which.
    /// </summary>
    public IMistType ConstructTask(WellKnownType kind, IMistType result)
    {
        var definition = LookupWellKnown(kind);

        return definition is ErrorType
            ? definition
            : context.ProgramModel.TypeInterner.GetOrCreateConstructedType(definition, new[] { result })
            ;
    }

    public IMistType ConstructArray(IMistType elementType, int? length)
    {
        return ConstructLengthIndexed(WellKnownTypes.Array, elementType, length);
    }

    /// <summary>
    /// The same construction for any of the length-indexed Standard types, the spans included: they
    /// differ in where the memory lives and not in how the length is carried.
    /// </summary>
    public IMistType ConstructLengthIndexed(WellKnownType definition, IMistType elementType, int? length)
    {
        var arrayDefinition = LookupWellKnown(definition);
        if(arrayDefinition is ErrorType)
        {
            return arrayDefinition;
        }

        // Lifted through the same helper as an explicit type argument, so a length
        // written as string[7] and one written as Array<string, 7> reach the same type.
        //
        // An unknown length carries its DOMAIN rather than being the bare top type, so a
        // `arr.Length` read can still be emitted as an int. The domain is int for the same
        // reason LiftInt lifts as int -- a length inhabits a refinement over int -- and it is
        // hardcoded here rather than read off the Length parameter because
        // TypeParameter.Domain is not populated yet.
        var lengthArgument = length is { } known
            ? LiftInt(known)
            : context.ProgramModel.TypeInterner.GetOrCreateUnknownValue(LookupWellKnown(WellKnownTypes.Int32))
            ;

        return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(arrayDefinition, new IMistType[] { elementType, lengthArgument });
    }

    /// <summary>
    /// Wraps an element type in a <c>Standard.Array&lt;,&gt;</c> constructed type,
    /// taking the length from the rank suffix: sized arrays (<c>int[5]</c>) carry the
    /// literal length, unsized arrays (<c>int[]</c>) carry none.
    /// </summary>
    private IMistType ResolveArrayType(SingleTypeRefNode singleRef, IMistType elementType)
    {
        var rank = GetArrayRank(singleRef)!;
        int? length = null;

        if(rank is SizedArrayRankArrayRankSuffixNode sized)
        {
            length = IntegerLiterals.Length(sized.Size.Lexeme);

            if(length is null)
            {
                context.Diagnostics.ReportAtNode(
                    ProgramModelDiagnostics.NumericLiteralOutOfRange, sized, null, sized.Size.Lexeme, WellKnownTypes.Int32.Keyword ?? WellKnownTypes.Int32.Name);
                return ErrorType.Instance;
            }
        }

        var arrayType = ConstructArray(elementType, length);
        if(arrayType is ErrorType)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.UnresolvedType, singleRef, null, WellKnownTypes.Array.FullyQualifiedName);
        }

        return arrayType;
    }

    /// <summary>
    /// Resolves a spread type reference (<c>TArgs...</c>) by looking up the named type
    /// parameter and checking it is a pack. The spread resolves
    /// to the type parameter itself — the semantics of unpacking are handled at the
    /// use site (e.g., type alias expansion).
    /// </summary>
    private IMistType ResolveSpread(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var name = spread.Name.IDENTIFIER.Lexeme;
        var resolved = ResolveNamedType(name, scope, spread);

        if(resolved is TypeParameter { IsPack: true } or LiftedType { Value: TypePackValue })
        {
            return new SpreadType(resolved);
        }

        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, spread, null, name);
        return ErrorType.Instance;
    }

    private IMistType ReportUnsupported(SingleTypeRefNode node)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, node.GetType().Name);
        return ErrorType.Instance;
    }
}