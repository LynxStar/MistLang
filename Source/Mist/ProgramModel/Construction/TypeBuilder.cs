using System.Collections.Immutable;
using Mist.Common.Diagnostics;
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

                case ExtensionItemTopLevelItemNode extensionItem:
                    StashExtension(fileScope, extensionItem.ExtensionDeclaration);
                    break;

                case WithItemTopLevelItemNode withItem:
                    StashConformance(fileScope, withItem.WithDeclaration);
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
        var blockNamespace = QualifiedNameHelper.Build(blockNs.Name);

        var blockScope = new Scope(parentScope)
        {
            Namespace = blockNamespace
        };

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

                case NamespaceExtensionItemNamespaceBodyItemNode extensionItem:
                    StashExtension(parentScope, extensionItem.ExtensionDeclaration);
                    break;

                case NamespaceNamespaceItemNamespaceBodyItemNode nestedNsItem
                    when nestedNsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlockNs:
                    ProcessBlockNamespace(parentScope, nestedBlockNs);
                    break;
            }
        }
    }

    /// <summary>
    /// Stashes an extension block AST for deferred processing in Phase 2.
    /// The receiver type cannot be resolved until all types are registered.
    /// </summary>
    private void StashExtension(Scope scope, ExtensionBlockDeclarationNode extensionDecl)
    {
        var namespaceName = scope.Namespace ?? "";
        context.PendingExtensions.Add(new PendingExtension(extensionDecl, scope, namespaceName));
    }

    private void StashConformance(Scope scope, WithDeclarationNode withDecl)
    {
        var namespaceName = scope.Namespace ?? "";
        context.PendingConformances.Add(new PendingConformance(withDecl, scope, namespaceName));
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

        FinalizeRegistration(typeDef, declaringType);

        // Newtypes and type aliases don't have member bodies — skip nested
        // type registration for them.
        if(typeDef is Newtype or TypeAlias)
        {
            return;
        }

        // Recurse into nested types within the type body.
        RegisterNestedTypes(typeDef.Scope, typeDecl, typeDef);
    }

    private void RegisterNestedBodyType(Scope parentScope, TypeBodyDeclarationNode bodyDecl, MistType declaringType)
    {
        var namespaceName = parentScope.Namespace!;
        var typeScope = new Scope(parentScope)
        {
            Namespace = namespaceName
        };

        var name = BuildGenericName(bodyDecl.Name.Lexeme, bodyDecl.TypeParams);
        // Wrap in a synthetic TypeDeclarationNode for CreateMistType compatibility
        var wrappedDecl = new BodyTypeTypeDeclarationNode { TypeBody = bodyDecl };
        MemberType? typeDef = bodyDecl.Keyword switch
        {
            ClassKeywordTypeKeywordNode => new ClassType(name, namespaceName, typeScope, declaringType, wrappedDecl),
            StructKeywordTypeKeywordNode => new StructType(name, namespaceName, typeScope, declaringType, wrappedDecl),
            AbstractKeywordTypeKeywordNode => new AbstractType(name, namespaceName, typeScope, declaringType, wrappedDecl),
            _ => null
        };

        if(typeDef is null)
        {
            return;
        }

        PopulateTypeParameters(typeDef, bodyDecl.TypeParams);

        FinalizeRegistration(typeDef, declaringType);
        RegisterNestedTypes(typeDef.Scope, wrappedDecl, typeDef);
    }

    private void RegisterNestedEnum(Scope parentScope, EnumDeclarationNode enumDecl, MistType declaringType)
    {
        var namespaceName = parentScope.Namespace!;
        var typeScope = new Scope(parentScope)
        {
            Namespace = namespaceName
        };

        var typeDef = new EnumType(enumDecl.Name.Lexeme, namespaceName, typeScope, declaringType, enumDecl);
        FinalizeRegistration(typeDef, declaringType);
    }

    private void RegisterNestedNewtype(Scope parentScope, NewtypeDeclarationNode newtypeDecl, MistType declaringType)
    {
        var namespaceName = parentScope.Namespace!;
        var typeScope = new Scope(parentScope)
        {
            Namespace = namespaceName
        };

        var name = BuildGenericName(newtypeDecl.Name.Lexeme, newtypeDecl.TypeParams);
        var typeDef = new Newtype(name, namespaceName, typeScope, declaringType, newtypeDecl);
        PopulateTypeParameters(typeDef.TypeParametersList, typeScope, typeDef, newtypeDecl.TypeParams);
        FinalizeRegistration(typeDef, declaringType);
    }

    private void RegisterNestedTypeAlias(Scope parentScope, TypeAliasDeclarationNode aliasDecl, MistType declaringType)
    {
        var namespaceName = parentScope.Namespace!;
        var typeScope = new Scope(parentScope)
        {
            Namespace = namespaceName
        };

        var name = BuildGenericName(aliasDecl.Name.Lexeme, aliasDecl.TypeParams);
        var typeDef = new TypeAlias(name, namespaceName, typeScope, declaringType, aliasDecl);
        PopulateTypeParameters(typeDef.TypeParametersList, typeScope, typeDef, aliasDecl.TypeParams);
        FinalizeRegistration(typeDef, declaringType);
    }

    /// <summary>
    /// Shared registration logic: adds the type to the flat FQN dictionary,
    /// the declaring type, and the scope.
    /// Reports a diagnostic if a type with the same FQN already exists.
    /// </summary>
    private void FinalizeRegistration(MistType typeDef, MistType? declaringType)
    {
        // Register in the flat FQN dictionary.
        if(!context.ProgramModel.RegisterType(typeDef))
        {
            context.Diagnostics.Report(ProgramModelDiagnostics.DuplicateTypeName, default, "", typeDef.Name, typeDef.NamespaceName);
        }

        // Ensure the namespace exists (for extensions/conformances that reference it).
        _ = context.ProgramModel.GetOrAddNamespace(typeDef.NamespaceName);

        // If nested, add to the declaring type and make visible by simple name
        // in the parent type's scope.
        if(declaringType is not null)
        {
            declaringType.NestedTypes.Add(typeDef);
            declaringType.Scope.AddLocalType(typeDef.Name, typeDef);
        }
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
            switch(member)
            {
                case NestedBodyTypeMemberMemberDeclarationNode nestedBody:
                    RegisterNestedBodyType(typeScope, nestedBody.NestedBody, declaringType);
                    break;
                case NestedEnumMemberMemberDeclarationNode nestedEnum:
                    RegisterNestedEnum(typeScope, nestedEnum.NestedEnum, declaringType);
                    break;
                case NestedNewtypeMemberMemberDeclarationNode nestedNewtype:
                    RegisterNestedNewtype(typeScope, nestedNewtype.NestedNewtype, declaringType);
                    break;
                case NestedTypeAliasMemberMemberDeclarationNode nestedAlias:
                    RegisterNestedTypeAlias(typeScope, nestedAlias.NestedTypeAlias, declaringType);
                    break;
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

        var typeScope = new Scope(parentScope)
        {
            Namespace = namespaceName
        };

        switch(typeDecl)
        {
            case BodyTypeTypeDeclarationNode body:
                var name = BuildGenericName(body.TypeBody.Name.Lexeme, body.TypeBody.TypeParams);
                MemberType typeDef = body.TypeBody.Keyword switch
                {
                    ClassKeywordTypeKeywordNode => new ClassType(name, namespaceName, typeScope, declaringType, typeDecl),
                    StructKeywordTypeKeywordNode => new StructType(name, namespaceName, typeScope, declaringType, typeDecl),
                    AbstractKeywordTypeKeywordNode => new AbstractType(name, namespaceName, typeScope, declaringType, typeDecl),
                    _ => throw new InvalidOperationException($"Unexpected type keyword: {body.TypeBody.Keyword.GetType().Name}")
                };

                PopulateTypeParameters(typeDef, body.TypeBody.TypeParams);
                return typeDef;

            case EnumTypeTypeDeclarationNode et:
                return new EnumType(et.EnumDeclaration.Name.Lexeme, namespaceName, typeScope, declaringType, et.EnumDeclaration);

            case NewtypeTypeTypeDeclarationNode nt:
                var ntName = BuildGenericName(nt.NewtypeDeclaration.Name.Lexeme, nt.NewtypeDeclaration.TypeParams);
                var newtype = new Newtype(ntName, namespaceName, typeScope, declaringType, nt.NewtypeDeclaration);
                PopulateTypeParameters(newtype.TypeParametersList, typeScope, newtype, nt.NewtypeDeclaration.TypeParams);
                return newtype;

            case TypeAliasTypeTypeDeclarationNode ta:
                var taName = BuildGenericName(ta.TypeAliasDeclaration.Name.Lexeme, ta.TypeAliasDeclaration.TypeParams);
                var alias = new TypeAlias(taName, namespaceName, typeScope, declaringType, ta.TypeAliasDeclaration);
                PopulateTypeParameters(alias.TypeParametersList, typeScope, alias, ta.TypeAliasDeclaration.TypeParams);
                return alias;

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
    /// Builds the arity-bearing name for a type. Non-generic types get their plain name;
    /// generic types get the angle-bracket arity suffix (e.g., <c>Foo&lt;&gt;</c> for 1 param,
    /// <c>Foo&lt;,&gt;</c> for 2, <c>Foo&lt;,,&gt;</c> for 3).
    /// </summary>
    private static string BuildGenericName(string simpleName, TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return simpleName;
        }

        // The Storm list shape splits into .TypeParameter (first) and .TypeParameters (rest).
        // N items need N-1 separating commas, and .TypeParameters.Length IS exactly N-1.
        var commas = new string(',', typeParams.TypeParameters.Length);
        return $"{simpleName}<{commas}>";
    }

    /// <summary>
    /// Creates <see cref="TypeParameter"/> instances from the AST type parameter list
    /// and registers them on the member type and in its scope.
    /// </summary>
    private static void PopulateTypeParameters(MemberType type, TypeParameterListNode? typeParams)
    {
        TypeParameterPopulator.Populate(type.TypeParametersList, type.Scope, type, typeParams);
    }

    /// <summary>
    /// Creates <see cref="TypeParameter"/> instances from the AST type parameter list,
    /// adds them to the provided list, and registers them in the given scope.
    /// Works for any type that declares type parameters (member types, aliases, newtypes).
    /// </summary>
    private static void PopulateTypeParameters(
        List<TypeParameter> target, Scope scope, MistType declaringType, TypeParameterListNode? typeParams)
    {
        TypeParameterPopulator.Populate(target, scope, declaringType, typeParams);
    }
}