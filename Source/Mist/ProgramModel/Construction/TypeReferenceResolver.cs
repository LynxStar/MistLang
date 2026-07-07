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
            _ => ReportUnsupported(singleRef),
        };

        // Array types are not yet supported in the simplified model.
        if(HasArrayRank(singleRef))
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, singleRef, null, "ArrayType");
            return ErrorType.Instance;
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

        // Try qualified name (has dots — could be namespace.type or parent.nested).
        if(namedRef.Name.IDENTIFIERs.Length > 0)
        {
            var qualifiedName = QualifiedNameHelper.Build(namedRef.Name);

            // First, try as a fully-qualified namespace.type.
            baseType = LookupQualifiedName(qualifiedName);

            // Second, try as a nested type key in any reachable namespace.
            baseType ??= LookupNestedQualifiedName(qualifiedName, scope);

            if(baseType is null)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, namedRef, null, qualifiedName);
                return ErrorType.Instance;
            }
        }
        else
        {
            // Simple name lookup through scope chain.
            baseType = ResolveNamedType(name, scope, namedRef);
            if(baseType is null)
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
    /// Resolves a TypeArgumentList into a list of resolved types.
    /// Non-type arguments (integer literals, etc.) are reported as unsupported
    /// and replaced with ErrorType.
    /// </summary>
    private IReadOnlyList<IMistType> ResolveTypeArguments(TypeArgumentListNode typeArgList, Scope scope)
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
        if(arg is TypeReferenceTypeArgumentTypeArgumentNode typeRefArg)
        {
            args.Add(Resolve(typeRefArg.Type, scope));
        }
        else
        {
            // Dependent type argument (integer, bool, char literal) — not yet resolved.
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, arg, null, "DependentTypeArgument");
            args.Add(ErrorType.Instance);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Name Lookup
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Looks up a simple (unqualified) name by walking the scope chain.
    /// Checks local types, then current namespace, then imported namespaces.
    /// Emits a <c>MIST_PM_UNRESOLVED_TYPE</c> diagnostic if not found.
    /// </summary>
    public IMistType? ResolveNamedType(string name, Scope scope, Storm.Runtime.SyntaxNode locationNode)
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

        return null;
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
    /// </summary>
    private IMistType? LookupInNamespace(string namespaceName, string typeName)
    {
        var namespaces = context.ProgramModel.Namespaces;
        if(namespaces.TryGetValue(namespaceName, out var ns) && ns.Types.TryGetValue(typeName, out var typeDef))
        {
            return typeDef;
        }

        return null;
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
        return singleRef switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode p => p.ArrayRank is not null,
            NamedSingleTypeRefSingleTypeRefNode n => n.ArrayRank is not null,
            ParenthesizedTypeRefSingleTypeRefNode pr => pr.ArrayRank is not null,
            _ => false,
        };
    }

    private IMistType ReportUnsupported(SingleTypeRefNode node)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, node.GetType().Name);
        return ErrorType.Instance;
    }
}