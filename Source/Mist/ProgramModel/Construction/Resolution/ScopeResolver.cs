using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
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
    /// IReadOnlyList{IMistType}?)"/> can expand chains regardless of declaration order. Set by
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
    private IMistType ResolveFunction(FuncSingleTypeRefSingleTypeRefNode funcRef, Scope scope)
    {
        var typeArguments = ResolveTypeArguments(funcRef.TypeArgs, scope);

        if(typeArguments.Count == 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.FunctionTypeNeedsAResult, funcRef, null);
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
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolveReservedAlias(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef, scope),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => Resolve(parenRef.Inner, scope),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
            FuncSingleTypeRefSingleTypeRefNode funcRef => ResolveFunction(funcRef, scope),
            SpreadSingleTypeRefSingleTypeRefNode spread => ResolveSpread(spread, scope),
            _ => ReportUnsupported(singleRef),
        };

        // If the type reference has an array rank suffix, wrap the inner type
        // in a constructed Standard.Array<TValue, Length>.
        if(HasArrayRank(singleRef))
        {
            inner = ResolveArrayType(singleRef, inner);
        }

        if(HasOptionalToken(singleRef) && inner is not ErrorType)
        {
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
    private IMistType ResolveNamed(NamedSingleTypeRefSingleTypeRefNode namedRef, Scope scope)
    {
        // Qualified if dotted, simple otherwise; the arity marker is the resolver's to add.
        var stem = namedRef.Name.IDENTIFIERs.Length > 0
            ? QualifiedNameHelper.Build(namedRef.Name)
            : namedRef.Name.IDENTIFIER.Lexeme;

        var arity = GetTypeArgumentArity(namedRef.TypeArgs);
        var baseType = ResolveGenericDefinition(stem, arity, scope, namedRef);

        if(baseType is ErrorType)
        {
            return ErrorType.Instance;
        }

        if(baseType is TypeAlias alias)
        {
            return ExpandAlias(alias, namedRef, scope);
        }

        // If type arguments are present, resolve them and wrap in a ConstructedType. A variadic
        // family is constructed even when NONE are written, since absorbing nothing is a binding
        // like any other -- a bare `Action` is Action<[]>, not an unconstructed definition.
        var written = namedRef.TypeArgs is { } typeArgList
            ? ResolveTypeArguments(typeArgList, scope)
            : [];

        if(namedRef.TypeArgs is not null || Absorbs(baseType))
        {
            var typeArgs = BindPack(baseType, written, namedRef);

            if(AsFunctionType(baseType, typeArgs) is { } functionType)
            {
                return functionType;
            }

            context.ProgramModel.ConstructorConstraintClosures.RecordType(baseType, typeArgs, namedRef);
            return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(baseType, typeArgs);
        }

        return baseType;
    }

    /// <summary>
    /// The definition a generic reference names, whether its arity matches a declaration's slot
    /// count or is absorbed by a pack. Shared so that every place naming a generic type agrees --
    /// a type reference, an alias expansion and a `new` all reach a variadic family the same way.
    /// </summary>
    public IMistType ResolveGenericDefinition(
        string stem, int arity, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        var lookupName = arity > 0 ? BuildArityName(stem, arity) : stem;

        return FindByArity(lookupName, stem, arity, scope)
            ?? ResolveNamedType(lookupName, scope, locationNode);
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

    private IMistType ExpandAlias(TypeAlias alias, NamedSingleTypeRefSingleTypeRefNode namedRef, Scope scope)
    {
        // Bound before substituting, exactly as a class's arguments are: an alias declaring a pack
        // has the same gap between its slot count and a flat use site, and substituting by ordinal
        // across that gap silently drops the surplus.
        var typeArgs = namedRef.TypeArgs is { } typeArgList
            ? BindPack(alias, ResolveTypeArguments(typeArgList, scope), namedRef)
            : Absorbs(alias) ? BindPack(alias, [], namedRef) : null;

        return ExpandAlias(alias, typeArgs);
    }

    /// <summary>
    /// A type alias is transparent: a use names the aliased type, never the alias. Expanding
    /// here, at resolution, is what makes <c>Optional&lt;int&gt;</c> and <c>int?</c> reach the
    /// same interned union, so every later phase sees one type. An alias whose own target is
    /// not resolved yet -- an alias naming another alias, ahead of it in the Phase 2a pass --
    /// is completed on demand through <see cref="AliasCompleter"/>, so an expansion never
    /// returns another alias: a chain bottoms out and a cycle resolves to an error type.
    /// </summary>
    internal IMistType ExpandAlias(TypeAlias alias, IReadOnlyList<IMistType>? typeArgs)
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
                args.Add(LiftIntegerLiteral(intLit.Literal.Lexeme));
                break;

            case NegatedIntegerTypeArgumentTypeArgumentNode negLit:
                args.Add(LiftNegatedIntegerLiteral(negLit.Literal.Lexeme));
                break;

            case HexLiteralTypeArgumentTypeArgumentNode hexLit:
                args.Add(LiftHexLiteral(hexLit.Literal.Lexeme));
                break;

            case BinaryLiteralTypeArgumentTypeArgumentNode binLit:
                args.Add(LiftBinaryLiteral(binLit.Literal.Lexeme));
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

    private IMistType LiftIntegerLiteral(string lexeme)
    {
        return LiftInt(long.TryParse(lexeme, out var parsed) ? parsed : 0L);
    }

    private IMistType LiftNegatedIntegerLiteral(string lexeme)
    {
        return LiftInt(long.TryParse(lexeme, out var parsed) ? -parsed : 0L);
    }

    private IMistType LiftHexLiteral(string lexeme)
    {
        var hexStr = lexeme.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? lexeme[2..] : lexeme;
        return LiftInt(long.TryParse(hexStr, NumberStyles.HexNumber, null, out var parsed) ? parsed : 0L);
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

        return LiftInt(value);
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
        var result = scope.Lookup(new LookupRequest(name, MemberVisibility.None, locationNode));

        switch(result)
        {
            case TypeResult typeResult:
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
        var definition = LookupWellKnown(WellKnownTypes.TaskGeneric);

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
        var length = rank is SizedArrayRankArrayRankSuffixNode sized
            ? (int.TryParse(sized.Size.Lexeme, out var parsed) ? parsed : 0)
            : (int?)null;

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

        if(resolved is TypeParameter { IsPack: true })
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