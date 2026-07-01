using System.Collections.Immutable;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Model;

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
    public void Build(Scope fileScope, CompilationUnitNode ast, SourceText sourceText)
    {
        var fileNamespace = fileScope.Namespace!;

        // Process top-level items (file-scoped namespace or no namespace).
        foreach(var item in ast.Items)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    RegisterType(fileScope, typeItem.TypeDeclaration, sourceText, fileNamespace, declaringType: null);
                    break;

                case NewtypeItemTopLevelItemNode newtypeItem:
                    RegisterNewtype(fileScope, newtypeItem.NewtypeDeclaration, fileNamespace);
                    break;

                case TypeAliasItemTopLevelItemNode aliasItem:
                    RegisterTypeAlias(fileScope, aliasItem.TypeAliasDeclaration, fileNamespace);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode blockNs:
                    ProcessBlockNamespace(fileScope, blockNs, sourceText);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                    ProcessFileScopedNamespaceBody(fileScope, fileScopedNs.Body, sourceText, fileNamespace);
                    break;
            }
        }
    }

    /// <summary>
    /// Processes types inside a block namespace. Creates a nested scope for
    /// the block and registers types under the block's namespace.
    /// </summary>
    private void ProcessBlockNamespace(
        Scope parentScope,
        BlockNamespaceNamespaceDeclarationNode blockNs,
        SourceText sourceText)
    {
        var blockNamespace = ExtractNameFromBlockNamespace(blockNs, sourceText);

        var blockScope = new Scope(parent: parentScope);
        blockScope.SetNamespace(blockNamespace);

        ProcessNamespaceBody(blockScope, blockNs.Body, sourceText, blockNamespace);
    }

    /// <summary>
    /// Processes types inside a file-scoped namespace body.
    /// </summary>
    private void ProcessFileScopedNamespaceBody(
        Scope fileScope,
        ImmutableArray<NamespaceBodyItemNode> body,
        SourceText sourceText,
        string namespaceName)
    {
        ProcessNamespaceBody(fileScope, body, sourceText, namespaceName);
    }

    /// <summary>
    /// Processes namespace body items, registering any type declarations found.
    /// </summary>
    private void ProcessNamespaceBody(
        Scope parentScope,
        ImmutableArray<NamespaceBodyItemNode> body,
        SourceText sourceText,
        string namespaceName)
    {
        foreach(var bodyItem in body)
        {
            switch(bodyItem)
            {
                case NamespaceTypeItemNamespaceBodyItemNode typeItem:
                    RegisterType(parentScope, typeItem.TypeDeclaration, sourceText, namespaceName, declaringType: null);
                    break;

                case NamespaceNewtypeItemNamespaceBodyItemNode newtypeItem:
                    RegisterNewtype(parentScope, newtypeItem.NewtypeDeclaration, namespaceName);
                    break;

                case NamespaceTypeAliasItemNamespaceBodyItemNode aliasItem:
                    RegisterTypeAlias(parentScope, aliasItem.TypeAliasDeclaration, namespaceName);
                    break;

                case NamespaceNamespaceItemNamespaceBodyItemNode nestedNsItem
                    when nestedNsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlockNs:
                    ProcessBlockNamespace(parentScope, nestedBlockNs, sourceText);
                    break;
            }
        }
    }

    /// <summary>
    /// Registers a single type declaration: creates the type shell, its scope,
    /// registers it in the namespace, and recurses into nested types.
    /// </summary>
    private void RegisterType(
        Scope parentScope,
        TypeDeclarationNode typeDecl,
        SourceText sourceText,
        string namespaceName,
        TypeDefinition? declaringType)
    {
        var typeDef = CreateTypeDefinition(typeDecl, sourceText, namespaceName, parentScope, declaringType);
        if(typeDef is null)
        {
            return;
        }

        // Register in the namespace.
        var ns = context.ProgramModel.GetOrAddNamespace(namespaceName);
        ns.Types[typeDef.Name] = typeDef;

        // If nested, add to the declaring type.
        declaringType?.NestedTypes.Add(typeDef);

        // Recurse into nested types within the type body.
        RegisterNestedTypes(typeDef.Scope, typeDecl, sourceText, namespaceName, typeDef);
    }

    /// <summary>
    /// Iterates over the members of a type declaration and registers any
    /// nested type declarations found.
    /// </summary>
    private void RegisterNestedTypes(
        Scope typeScope,
        TypeDeclarationNode typeDecl,
        SourceText sourceText,
        string namespaceName,
        TypeDefinition declaringType)
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
                RegisterType(typeScope, nestedTypeMember.NestedType, sourceText, namespaceName, declaringType);
            }
        }
    }

    /// <summary>
    /// Registers a newtype declaration in the program model.
    /// </summary>
    private void RegisterNewtype(
        Scope parentScope,
        NewtypeDeclarationNode newtypeDecl,
        string namespaceName)
    {
        var name = newtypeDecl.Name.Lexeme;
        var typeScope = new Scope(parentScope);
        typeScope.SetNamespace(namespaceName);

        var typeDef = new NewtypeDefinition(name, namespaceName, typeScope, declaringType: null);

        var ns = context.ProgramModel.GetOrAddNamespace(namespaceName);
        ns.Types[name] = typeDef;
    }

    /// <summary>
    /// Registers a type alias (including refinement types) in the program model.
    /// </summary>
    private void RegisterTypeAlias(
        Scope parentScope,
        TypeAliasDeclarationNode aliasDecl,
        string namespaceName)
    {
        var name = aliasDecl.Name.Lexeme;
        var typeScope = new Scope(parentScope);
        typeScope.SetNamespace(namespaceName);

        var typeDef = new TypeAliasDefinition(name, namespaceName, typeScope, declaringType: null);

        var ns = context.ProgramModel.GetOrAddNamespace(namespaceName);
        ns.Types[name] = typeDef;
    }

    /// <summary>
    /// Creates the appropriate concrete TypeDefinition subclass for a type declaration node.
    /// Reports a diagnostic and returns null for unrecognized node types.
    /// </summary>
    private TypeDefinition? CreateTypeDefinition(
        TypeDeclarationNode typeDecl,
        SourceText sourceText,
        string namespaceName,
        Scope parentScope,
        TypeDefinition? declaringType)
    {
        var typeScope = new Scope(parentScope);
        typeScope.SetNamespace(namespaceName);

        switch(typeDecl)
        {
            case ClassTypeTypeDeclarationNode ct:
                return new ClassDefinition(ct.ClassDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType);

            case StructTypeTypeDeclarationNode st:
                return new StructDefinition(st.StructDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType);

            case AbstractTypeTypeDeclarationNode at:
                return new AbstractDefinition(at.AbstractDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType);

            case EnumTypeTypeDeclarationNode et:
                return new EnumDefinition(et.EnumDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType);

            default:
                context.ReportError(
                    "MIST_INTERNAL_UNKNOWN_TYPE_DECL",
                    $"Internal compiler error: unrecognized type declaration node '{typeDecl.GetType().Name}'.",
                    typeDecl,
                    sourceText);
                return null;
        }
    }

    /// <summary>
    /// Gets the members array from a type declaration, or default if the
    /// type kind does not support nested type declarations (e.g., enums, abstracts).
    /// </summary>
    private static ImmutableArray<MemberDeclarationNode> GetMembers(TypeDeclarationNode typeDecl)
    {
        return typeDecl switch
        {
            ClassTypeTypeDeclarationNode ct => ct.ClassDeclaration.Members,
            StructTypeTypeDeclarationNode st => st.StructDeclaration.Members,
            _ => default
        };
    }

    /// <summary>
    /// Extracts the namespace name from a block namespace declaration node.
    /// </summary>
    private static string ExtractNameFromBlockNamespace(
        BlockNamespaceNamespaceDeclarationNode blockNs,
        SourceText sourceText)
    {
        var qualifiedId = blockNs.Name;
        var span = qualifiedId.Span;

        if(span.Length > 0 && span.Start + span.Length <= sourceText.Contents.Length)
        {
            var extracted = sourceText.Contents.Substring(span.Start, span.Length);
            if(IsValidQualifiedIdentifier(extracted))
            {
                return extracted;
            }
        }

        return qualifiedId.IDENTIFIER.Lexeme;
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