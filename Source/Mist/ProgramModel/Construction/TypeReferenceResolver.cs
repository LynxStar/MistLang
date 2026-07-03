using System.Collections.Frozen;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.TypeReferences;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves AST type reference nodes into <see cref="ITypeReference"/> instances
/// by performing scope-based name lookup against the program model.
/// </summary>
internal sealed class TypeReferenceResolver
{
    /// <summary>
    /// Hardcoded keyword → fully-qualified Standard Library type name mapping.
    /// These are always in scope and cannot be shadowed.
    /// </summary>
    private static readonly FrozenDictionary<string, string> PrimitiveAliases =
        new Dictionary<string, string>
        {
            ["int"] = "Standard.Int32",
            ["uint"] = "Standard.UInt32",
            ["long"] = "Standard.Int64",
            ["ulong"] = "Standard.UInt64",
            ["short"] = "Standard.Int16",
            ["ushort"] = "Standard.UInt16",
            ["byte"] = "Standard.Byte",
            ["sbyte"] = "Standard.SByte",
            ["float"] = "Standard.Single",
            ["double"] = "Standard.Double",
            ["decimal"] = "Standard.Decimal",
            ["bool"] = "Standard.Boolean",
            ["char"] = "Standard.Char",
            ["string"] = "Standard.String",
            ["regex"] = "Standard.Regex",
        }.ToFrozenDictionary();

    private readonly BuilderContext context;

    public TypeReferenceResolver(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Resolves a <see cref="TypeReferenceNode"/> to an <see cref="ITypeReference"/>.
    /// Handles union types (multiple pipe-separated constituents).
    /// </summary>
    public ITypeReference Resolve(TypeReferenceNode typeRef, Scope scope)
    {
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            var constituents = new List<ITypeReference>(typeRef.SingleTypeRefs.Length + 1)
            {
                ResolveSingle(typeRef.First, scope)
            };

            foreach(var constituent in typeRef.SingleTypeRefs)
            {
                constituents.Add(ResolveSingle(constituent, scope));
            }

            return new UnionTypeReference(constituents);
        }

        return ResolveSingle(typeRef.First, scope);
    }

    /// <summary>
    /// Resolves a single (non-union) type reference node.
    /// </summary>
    private ITypeReference ResolveSingle(SingleTypeRefNode singleRef, Scope scope)
    {
        var inner = singleRef switch
        {
            VoidSingleTypeRefSingleTypeRefNode => VoidTypeReference.Instance,
            UnknownSingleTypeRefSingleTypeRefNode => UnknownTypeReference.Instance,
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => ResolvePrimitive(primitiveRef),
            NamedSingleTypeRefSingleTypeRefNode namedRef => ResolveNamed(namedRef, scope),
            TupleSingleTypeRefSingleTypeRefNode tupleRef => ResolveTuple(tupleRef, scope),
            FuncSingleTypeRefSingleTypeRefNode funcRef => ResolveFunc(funcRef, scope),
            ParenthesizedTypeRefSingleTypeRefNode parenRef => Resolve(parenRef.Inner, scope),
            _ => ReportUnknownTypeRefNode(singleRef),
        };

        // Wrap in array if there's an array rank suffix.
        if(HasArrayRank(singleRef))
        {
            return new ArrayTypeReference(inner);
        }

        return inner;
    }

    /// <summary>
    /// Resolves a primitive type keyword to a <see cref="NamedTypeReference"/>
    /// pointing at the stdlib type definition.
    /// </summary>
    private ITypeReference ResolvePrimitive(PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef)
    {
        var keyword = GetPrimitiveKeyword(primitiveRef.PrimitiveType);

        if(keyword is not null && PrimitiveAliases.TryGetValue(keyword, out var qualifiedName))
        {
            var typeDef = LookupQualifiedName(qualifiedName);
            if(typeDef is not null)
            {
                return new NamedTypeReference(typeDef);
            }
        }

        // Stdlib type not loaded — produce error.
        return ErrorTypeReference.Instance;
    }

    /// <summary>
    /// Resolves a named type reference by looking up the name in the scope.
    /// </summary>
    private ITypeReference ResolveNamed(NamedSingleTypeRefSingleTypeRefNode namedRef, Scope scope)
    {
        var name = namedRef.Name.IDENTIFIER.Lexeme;

        // Try qualified name (has dots — could be namespace.type or parent.nested).
        if(namedRef.Name.IDENTIFIERs.Length > 0)
        {
            var qualifiedName = BuildQualifiedName(namedRef.Name);

            // First, try as a fully-qualified namespace.type.
            var typeDef = LookupQualifiedName(qualifiedName);
            if(typeDef is not null)
            {
                return new NamedTypeReference(typeDef);
            }

            // Second, try as a nested type key in any reachable namespace.
            var nestedType = LookupNestedQualifiedName(qualifiedName, scope);
            if(nestedType is not null)
            {
                return new NamedTypeReference(nestedType);
            }

            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnresolvedType, namedRef, null, qualifiedName);
            return ErrorTypeReference.Instance;
        }

        // Simple name lookup through scope chain.
        var resolved = ResolveNamedType(name, scope, namedRef);
        if(resolved is not null)
        {
            return new NamedTypeReference(resolved);
        }

        return ErrorTypeReference.Instance;
    }

    /// <summary>
    /// Resolves a tuple type reference.
    /// </summary>
    private ITypeReference ResolveTuple(TupleSingleTypeRefSingleTypeRefNode tupleRef, Scope scope)
    {
        var elements = new List<ITypeReference>
        {
            Resolve(tupleRef.First.Type, scope),
            Resolve(tupleRef.Rest.Type, scope)
        };

        foreach(var additional in tupleRef.TupleTypeElements)
        {
            elements.Add(Resolve(additional.Type, scope));
        }

        return new TupleTypeReference(elements);
    }

    /// <summary>
    /// Resolves a func type reference (e.g., func&lt;int, string&gt;).
    /// The last type argument is the return type; all preceding are parameters.
    /// </summary>
    private ITypeReference ResolveFunc(FuncSingleTypeRefSingleTypeRefNode funcRef, Scope scope)
    {
        if(funcRef.TypeArgs is null)
        {
            return new DelegateTypeReference([], VoidTypeReference.Instance);
        }

        var typeArgs = new List<ITypeReference> { Resolve(funcRef.TypeArgs.GetTypeReference(), scope) };
        foreach(var arg in funcRef.TypeArgs.GetTypeReferences())
        {
            typeArgs.Add(Resolve(arg, scope));
        }

        if(typeArgs.Count == 0)
        {
            return new DelegateTypeReference([], VoidTypeReference.Instance);
        }

        // Last type argument is the return type.
        var returnType = typeArgs[^1];
        var paramTypes = typeArgs.GetRange(0, typeArgs.Count - 1);
        return new DelegateTypeReference(paramTypes, returnType);
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

    private static string? GetPrimitiveKeyword(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => "int",
            UintTypePrimitiveTypeNode => "uint",
            LongTypePrimitiveTypeNode => "long",
            UlongTypePrimitiveTypeNode => "ulong",
            ShortTypePrimitiveTypeNode => "short",
            UshortTypePrimitiveTypeNode => "ushort",
            ByteTypePrimitiveTypeNode => "byte",
            SbyteTypePrimitiveTypeNode => "sbyte",
            FloatTypePrimitiveTypeNode => "float",
            DoubleTypePrimitiveTypeNode => "double",
            DecimalTypePrimitiveTypeNode => "decimal",
            BoolTypePrimitiveTypeNode => "bool",
            CharTypePrimitiveTypeNode => "char",
            StringTypePrimitiveTypeNode => "string",
            RegexTypePrimitiveTypeNode => "regex",
            _ => null,
        };
    }

    private static string BuildQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        if(qualifiedId.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return qualifiedId.IDENTIFIER.Lexeme;
        }

        var sb = new System.Text.StringBuilder(qualifiedId.IDENTIFIER.Lexeme);
        foreach(var token in qualifiedId.IDENTIFIERs)
        {
            if(!string.IsNullOrEmpty(token.Lexeme))
            {
                _ = sb.Append('.');
                _ = sb.Append(token.Lexeme);
            }
        }

        return sb.ToString();
    }

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

    private ITypeReference ReportUnknownTypeRefNode(SingleTypeRefNode node)
    {
        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeRefNode, node, null, node.GetType().Name);
        return ErrorTypeReference.Instance;
    }
}