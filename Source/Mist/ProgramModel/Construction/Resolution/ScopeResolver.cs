using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
            SpreadSingleTypeRefSingleTypeRefNode spread => ResolveSpread(spread, scope),
            _ => ReportUnsupported(singleRef),
        };

        // If the type reference has an array rank suffix, wrap the inner type
        // in a constructed Standard.Array<TValue, Length>.
        if(HasArrayRank(singleRef))
        {
            return ResolveArrayType(singleRef, inner);
        }

        return inner;
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
        return LookupInNamespace(wellKnown.Namespace, wellKnown.Name) ?? ErrorType.Instance;
    }

    /// <summary>
    /// Resolves a named type reference by looking up the name in the scope.
    /// </summary>
    private IMistType ResolveNamed(NamedSingleTypeRefSingleTypeRefNode namedRef, Scope scope)
    {
        var name = namedRef.Name.IDENTIFIER.Lexeme;

        // Compute arity from type arguments so we can look up the arity-bearing name.
        var arity = GetTypeArgumentArity(namedRef.TypeArgs);
        var lookupName = arity > 0 ? BuildArityName(name, arity) : name;

        // Build the final lookup key: qualified if dotted, simple otherwise.
        if(namedRef.Name.IDENTIFIERs.Length > 0)
        {
            var qualifiedName = QualifiedNameHelper.Build(namedRef.Name);
            lookupName = arity > 0 ? BuildArityName(qualifiedName, arity) : qualifiedName;
        }

        // Single entry point: resolve the name through the unified three-tier lookup.
        var baseType = ResolveNamedType(lookupName, scope, namedRef);
        if(baseType is ErrorType)
        {
            return ErrorType.Instance;
        }

        // If type arguments are present, resolve them and wrap in a ConstructedType.
        if(namedRef.TypeArgs is { } typeArgList)
        {
            var typeArgs = ResolveTypeArguments(typeArgList, scope);
            return context.ProgramModel.TypeInterner.GetOrCreateConstructedType(baseType, typeArgs);
        }

        return baseType;
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

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, arg, null, arg.GetType().Name);
                args.Add(ErrorType.Instance);
                break;
        }
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
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves a name to whatever it is — variable, type, or not found.
    /// Walks the scope chain checking localVariables and localTypes at each level,
    /// then falls back to namespace-based resolution.
    /// </summary>
    public LookupResult Lookup(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        var current = scope;
        while(current is not null)
        {
            var candidates = new List<LookupResult>();

            if(current.LocalVariables.TryGetValue(name, out var local))
            {
                candidates.Add(new VariableResult(local));
            }

            if(current.LocalTypes.TryGetValue(name, out var localType))
            {
                candidates.Add(new TypeResult(localType));
            }

            if(current.LocalMethods.TryGetValue(name, out var methods))
            {
                candidates.Add(new MethodGroupResult(methods));
            }

            if(candidates.Count == 1)
            {
                return candidates[0];
            }

            if(candidates.Count > 1)
            {
                return new AmbiguousResult(candidates);
            }

            current = current.Parent;
        }

        // Scope chain exhausted — try namespace-based resolution.
        var namespaceType = ResolveByNamespace(name, scope, locationNode);
        if(namespaceType is not null)
        {
            return new TypeResult(namespaceType);
        }

        return new NotFoundResult();
    }

    /// <summary>
    /// Looks up a type name by walking the scope chain. Delegates to <see cref="Lookup"/>
    /// and filters to types only. Emits diagnostics for wrong-kind or not-found.
    /// </summary>
    public IMistType ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        var result = Lookup(name, scope, locationNode);

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

    /// <summary>
    /// Namespace-based type resolution (Tiers 2-4):
    /// 2. Own namespace — shadows imports.
    /// 3. Imports — flat pool with ambiguity check.
    /// 4. FQN fallback.
    /// Returns null if not found (caller decides whether to emit a diagnostic).
    /// </summary>
    private IMistType? ResolveByNamespace(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        // Tier 2: Check own namespace at each scope level (shadows imports).
        var current = scope;
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
    public IMistType ConstructArray(IMistType elementType, int? length)
    {
        var arrayDefinition = LookupWellKnown(WellKnownTypes.Array);
        if(arrayDefinition is ErrorType)
        {
            return arrayDefinition;
        }

        // Lifted through the same helper as an explicit type argument, so a length
        // written as string[7] and one written as Array<string, 7> reach the same type.
        var lengthArgument = length is { } known ? LiftInt(known) : UnknownType.Instance;

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
    /// parameter that has <see cref="TypeParameter.HasParams"/> set. The spread resolves
    /// to the type parameter itself — the semantics of unpacking are handled at the
    /// use site (e.g., type alias expansion).
    /// </summary>
    private IMistType ResolveSpread(SpreadSingleTypeRefSingleTypeRefNode spread, Scope scope)
    {
        var name = spread.Name.IDENTIFIER.Lexeme;
        var resolved = ResolveNamedType(name, scope, spread);

        if(resolved is TypeParameter { HasParams: true })
        {
            return resolved;
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