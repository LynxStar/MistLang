using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Walks the AST to register type declarations in the program model.
/// Creates type shells with their scopes and handles nested types recursively.
/// One instance is created per compilation and reused across all files.
/// </summary>
internal sealed class TypeBuilder
{
    private readonly BuilderContext context;

    public TypeBuilder(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Processes all type declarations in a compilation unit, including types
    /// inside block namespaces.
    /// </summary>
    public void Build(Scope fileScope, CompilationUnitNode ast)
    {
        foreach(var item in ast.Items)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    RegisterType(fileScope, typeItem.TypeDeclaration, declaringType: null);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode blockNs:
                    ProcessBlockNamespace(fileScope, blockNs);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                    ProcessNamespaceBody(fileScope, fileScopedNs.Body);
                    break;
            }
        }
    }

    /// <summary>
    /// Processes types inside a block namespace. Creates a nested scope for
    /// the block and registers types under the block's namespace.
    /// </summary>
    private void ProcessBlockNamespace(Scope parentScope, BlockNamespaceNamespaceDeclarationNode blockNs)
    {
        var blockNamespace = ExtractNameFromNode(blockNs.Name);

        var blockScope = new Scope(parentScope);
        blockScope.SetNamespace(blockNamespace);

        ProcessNamespaceBody(blockScope, blockNs.Body);
    }

    /// <summary>
    /// Processes namespace body items, registering any type declarations found.
    /// </summary>
    private void ProcessNamespaceBody(Scope parentScope, ImmutableArray<NamespaceBodyItemNode> body)
    {
        foreach(var bodyItem in body)
        {
            switch(bodyItem)
            {
                case NamespaceTypeItemNamespaceBodyItemNode typeItem:
                    RegisterType(parentScope, typeItem.TypeDeclaration, declaringType: null);
                    break;

                case NamespaceNamespaceItemNamespaceBodyItemNode nestedNsItem
                    when nestedNsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlockNs:
                    ProcessBlockNamespace(parentScope, nestedBlockNs);
                    break;
            }
        }
    }

    /// <summary>
    /// Registers a single type declaration: creates the type shell, its scope,
    /// registers it in the namespace, and recurses into nested types.
    /// Newtypes and type aliases have no member bodies, so nested type
    /// registration is skipped for them.
    /// </summary>
    private void RegisterType(
        Scope parentScope,
        TypeDeclarationNode typeDecl,
        MistType? declaringType)
    {
        var typeDef = CreateMistType(typeDecl, parentScope, declaringType);
        if(typeDef is null)
        {
            return;
        }

        // Register in the namespace with a qualified key for nested types.
        var ns = context.ProgramModel.GetOrAddNamespace(typeDef.NamespaceName);
        var registrationName = declaringType is not null
            ? $"{declaringType.Name}.{typeDef.Name}"
            : typeDef.Name;
        ns.Types[registrationName] = typeDef;

        // If nested, add to the declaring type and make visible by simple name
        // in the parent type's scope.
        if(declaringType is not null)
        {
            declaringType.NestedTypes.Add(typeDef);
            declaringType.Scope.AddLocalType(typeDef.Name, typeDef);
        }

        // Newtypes and type aliases don't have member bodies — skip nested
        // type registration for them.
        if(typeDef is Newtype or TypeAlias)
        {
            return;
        }

        // Recurse into nested types within the type body.
        RegisterNestedTypes(typeDef.Scope, typeDecl, typeDef);
    }

    /// <summary>
    /// Iterates over the members of a type declaration and registers any
    /// nested type declarations found.
    /// </summary>
    private void RegisterNestedTypes(
        Scope typeScope,
        TypeDeclarationNode typeDecl,
        MistType declaringType)
    {
        var members = GetMembers(typeDecl);
        if(members.IsDefault)
        {
            return;
        }

        foreach(var member in members)
        {
            if(member is NestedTypeMemberMemberDeclarationNode nestedTypeMember)
            {
                RegisterType(typeScope, nestedTypeMember.NestedType, declaringType);
            }
        }
    }

    /// <summary>
    /// Creates the appropriate concrete MistType subclass for a type declaration node.
    /// Reports a diagnostic and returns null for unrecognized node types.
    /// </summary>
    private MistType? CreateMistType(
        TypeDeclarationNode typeDecl,
        Scope parentScope,
        MistType? declaringType)
    {
        var namespaceName = parentScope.Namespace!;

        var typeScope = new Scope(parentScope);
        typeScope.SetNamespace(namespaceName);

        switch(typeDecl)
        {
            case BodyTypeTypeDeclarationNode body:
                var name = body.TypeBody.Name.Lexeme;
                return body.TypeBody.Keyword switch
                {
                    ClassKeywordTypeKeywordNode => new ClassType(name, namespaceName, typeScope, declaringType, typeDecl),
                    StructKeywordTypeKeywordNode => new StructType(name, namespaceName, typeScope, declaringType, typeDecl),
                    AbstractKeywordTypeKeywordNode => new AbstractType(name, namespaceName, typeScope, declaringType, typeDecl),
                    _ => throw new InvalidOperationException($"Unexpected type keyword: {body.TypeBody.Keyword.GetType().Name}")
                };

            case EnumTypeTypeDeclarationNode et:
                return new EnumType(et.EnumDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType, et.EnumDeclaration);

            case NewtypeTypeTypeDeclarationNode nt:
                return new Newtype(nt.NewtypeDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType, nt.NewtypeDeclaration);

            case TypeAliasTypeTypeDeclarationNode ta:
                return new TypeAlias(ta.TypeAliasDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType, ta.TypeAliasDeclaration);

            default:
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.UnknownTypeDeclaration, typeDecl, null, typeDecl.GetType().Name);
                return null;
        }
    }

    /// <summary>
    /// Gets the members array from a type declaration, or default if the
    /// type kind does not support nested type declarations (e.g., enums, newtypes, type aliases).
    /// </summary>
    private static ImmutableArray<MemberDeclarationNode> GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            BodyTypeTypeDeclarationNode body => body.TypeBody.Members,
            _ => default
        };
    }

    /// <summary>
    /// Extracts a qualified name from an AST node by reading from its origin source.
    /// Falls back to the first identifier token's lexeme if source is unavailable.
    /// </summary>
    private static string ExtractNameFromNode(QualifiedIdentifierNode qualifiedId)
    {
        var sourceText = GetSourceFromOrigin(qualifiedId);
        if(sourceText is not null)
        {
            var span = qualifiedId.Span;
            if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
            {
                var extracted = sourceText.Contents.Substring(span.Start, span.Length);
                if(IsValidQualifiedIdentifier(extracted))
                {
                    return extracted;
                }
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
    }

    /// <summary>
    /// Retrieves the source text from a node's origin, unwrapping synthetic origins.
    /// Returns null for test origins or nodes without origin.
    /// </summary>
    private static SourceText? GetSourceFromOrigin(Storm.Runtime.SyntaxNode node)
    {
        var origin = node.Origin;
        while(origin is not null)
        {
            switch(origin)
            {
                case ParsedOrigin parsed:
                    return parsed.Source;
                case SyntheticOrigin synthetic:
                    origin = synthetic.DerivedFrom;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    private static bool IsValidQualifiedIdentifier(string text)
    {
        if(string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach(var ch in text)
        {
            if(!char.IsLetterOrDigit(ch) && ch != '_' && ch != '.')
            {
                return false;
            }
        }

        return true;
    }
}