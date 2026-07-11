using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts;
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

    private readonly BuilderContext context;
    private readonly IAmbientTypeProvider ambientProvider;

    public TypeReferenceResolver(BuilderContext context)
    {
        this.context = context;
        ambientProvider = context.AmbientProvider;
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
            VoidSingleTypeRefSingleTypeRefNode => ResolveReservedAlias("Void"),
            UnknownSingleTypeRefSingleTypeRefNode => UnknownType.Instance,
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolveReservedAlias(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef, scope),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => Resolve(parenRef.Inner, scope),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
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
        var name = namedRef.Name.IDENTIFIER.Lexeme;
        IMistType? baseType;

        // Compute arity from type arguments so we can look up the arity-bearing name.
        var arity = GetTypeArgumentArity(namedRef.TypeArgs);
        var lookupName = arity > 0 ? BuildArityName(name, arity) : name;

        // Try qualified name (has dots — could be namespace.type or parent.nested).
        if(namedRef.Name.IDENTIFIERs.Length > 0)
        {
            var qualifiedName = QualifiedNameHelper.Build(namedRef.Name);
            var qualifiedLookup = arity > 0 ? BuildArityName(qualifiedName, arity) : qualifiedName;

            // First, try as a fully-qualified namespace.type.
            baseType = LookupQualifiedName(qualifiedLookup);

            // Second, try as a nested type key in any reachable namespace.
            baseType ??= LookupNestedQualifiedName(qualifiedLookup, scope);

            if(baseType is null)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, namedRef, null, qualifiedName);
                return ErrorType.Instance;
            }
        }
        else
        {
            // Simple name lookup through scope chain.
            baseType = ResolveNamedType(lookupName, scope, namedRef);
            if(baseType is ErrorType)
            {
                return ErrorType.Instance;
            }
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
                args.Add(context.ProgramModel.TypeInterner.GetOrCreateLiftedType(true, "true"));
                break;

            case FalseLiteralTypeArgumentTypeArgumentNode:
                args.Add(context.ProgramModel.TypeInterner.GetOrCreateLiftedType(false, "false"));
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
    /// Looks up a simple (unqualified) name by walking the scope chain.
    /// Checks local types, then current namespace, then imported namespaces.
    /// Emits a <c>MIST_PM_UNRESOLVED_TYPE</c> diagnostic if not found.
    /// </summary>
    public IMistType ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
    {
        var current = scope;
        while(current is not null)
        {
            // Check types directly visible in this scope (e.g., nested types).
            if(current.LocalTypes.TryGetValue(name, out var localType))
            {
                return localType;
            }

            // Check current namespace.
            if(current.Namespace is not null)
            {
                var found = LookupInNamespace(current.Namespace, name);
                if(found is not null)
                {
                    return found;
                }
            }

            // Check imported namespaces.
            foreach(var imported in current.ImportedNamespaces)
            {
                var found = LookupInNamespace(imported, name);
                if(found is not null)
                {
                    return found;
                }
            }

            current = current.Parent;
        }

        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, locationNode, null, name);

        return ErrorType.Instance;
    }

    /// <summary>
    /// Looks up a qualified name (e.g., "Outer.Inner") as a nested type key
    /// in any namespace reachable from the scope. Does not report diagnostics.
    /// </summary>
    private IMistType? LookupNestedQualifiedName(string qualifiedName, Scope scope)
    {
        var current = scope;
        while(current is not null)
        {
            if(current.Namespace is not null)
            {
                var found = LookupInNamespace(current.Namespace, qualifiedName);
                if(found is not null)
                {
                    return found;
                }
            }

            foreach(var imported in current.ImportedNamespaces)
            {
                var found = LookupInNamespace(imported, qualifiedName);
                if(found is not null)
                {
                    return found;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// Looks up a type by simple name within a specific namespace.
    /// Queries both the namespace registry and the ambient provider, deduplicating
    /// by reference equality. Returns null if no candidates exist, the single
    /// candidate if exactly one is found, or <see cref="ErrorType"/> if two distinct
    /// candidates produce an ambiguity.
    /// </summary>
    private IMistType? LookupInNamespace(string namespaceName, string typeName)
    {
        IMistType? registryCandidate = null;
 
        var namespaces = context.ProgramModel.Namespaces;
        if(namespaces.TryGetValue(namespaceName, out var ns) && ns.Types.TryGetValue(typeName, out var typeDef))
        {
            registryCandidate = typeDef;
        }

        var ambientCandidate = ambientProvider.ResolveType(namespaceName, typeName, context.ProgramModel);

        // Deduplicate by reference equality (alias-mapped types collapse to one)
        if(registryCandidate is null && ambientCandidate is null)
        {
            return null;
        }

        if(registryCandidate is not null && ambientCandidate is null)
        {
            return registryCandidate;
        }

        if(registryCandidate is null && ambientCandidate is not null)
        {
            return ambientCandidate;
        }

        // Both non-null — check reference equality
        if(ReferenceEquals(registryCandidate, ambientCandidate))
        {
            return registryCandidate;
        }

        // Two distinct candidates → ambiguity
        context.Diagnostics.Report(
            ProgramModelDiagnostics.AmbiguousType,
            default,
            string.Empty,
            typeName,
            namespaceName);
        return ErrorType.Instance;
    }

    /// <summary>
    /// Looks up a fully-qualified type name (e.g., "Standard.Int32").
    /// Splits into namespace + simple name and does a direct lookup.
    /// </summary>
    private IMistType? LookupQualifiedName(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        if(lastDot < 0)
        {
            return null;
        }

        var namespacePart = qualifiedName[..lastDot];
        var typeName = qualifiedName[(lastDot + 1)..];

        return LookupInNamespace(namespacePart, typeName);
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
        var arrayDef = LookupInNamespace("Standard", "Array<,>");
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

    private IMistType ReportUnsupported(SingleTypeRefNode node)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, node.GetType().Name);
        return ErrorType.Instance;
    }
}