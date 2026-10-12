using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

/// <summary>
/// Walks the AST to register type declarations in the program model.
/// Creates type shells with their scopes and handles nested types recursively.
/// One instance is created per compilation and reused across all files.
/// </summary>
internal sealed class TypeBuilder
{
    private readonly BuilderContext context;

    /// <summary>
    /// The assembly owning the file currently being processed. Held as state for the
    /// duration of one <see cref="Build"/> call so every type kind, nested types
    /// included, can be stamped from the single place they all funnel through.
    /// <para>
    /// Seeded with the synthetic assembly, which is only ever observable if a type were
    /// registered outside a <see cref="Build"/> call -- there is no declaration boundary
    /// in that case, so it is also the right answer.
    /// </para>
    /// </summary>
    private MistAssembly currentAssembly = MistAssembly.Synthetic;
    private readonly List<DeclaredType> registeredTypes = [];

    public TypeBuilder(BuilderContext context)
    {
        this.context = context;
    }

    /// <summary>
    /// Processes all type declarations in a compilation unit, including types
    /// inside block namespaces.
    /// </summary>
    /// <param name="fileScope">
    /// The file's scope. Top-level types are published on it by simple name, which is what makes
    /// them -- and only them -- nameable from the namespace blocks written in the same file.
    /// </param>
    /// <param name="fileNamespace">
    /// The namespace the file's top-level items belong to, explicit or inferred.
    /// </param>
    /// <param name="assembly">
    /// The assembly owning this compilation unit, recorded on every type registered
    /// from it.
    /// </param>
    public TypeBuilderResult Build(FileScope fileScope, string fileNamespace, CompilationUnitNode ast, MistAssembly assembly)
    {
        currentAssembly = assembly;
        registeredTypes.Clear();

        // The file's own namespace level, between its top-level items and the file scope. Block
        // namespaces deliberately do not get it as an ancestor: they are SIBLINGS of this
        // namespace, not children, so they see the file's top-level items through the file scope
        // and the rest of this namespace not at all.
        var fileLevel = new NamespaceScope(fileScope, fileNamespace, context.ProgramModel);

        foreach(var item in ast.Items)
        {
            switch(item)
            {
                case TypeItemTopLevelItemNode typeItem:
                    PublishTopLevel(fileScope, RegisterType(fileLevel, fileNamespace, typeItem.TypeDeclaration, declaringType: null));
                    break;

                case ExtensionItemTopLevelItemNode extensionItem:
                    StashExtension(fileLevel, fileNamespace, extensionItem.ExtensionDeclaration);
                    break;

                case WithItemTopLevelItemNode withItem:
                    StashConformance(fileLevel, fileNamespace, withItem.WithDeclaration);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode blockNs:
                    ProcessBlockNamespace(fileScope, "", blockNs);
                    break;

                case NamespaceItemTopLevelItemNode nsItem
                    when nsItem.NamespaceDeclaration is FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                    ProcessNamespaceBody(fileLevel, fileNamespace, fileScopedNs.Body, fileScope);
                    break;
            }
        }

        // After the item walk, so the statements are collected whatever order they were
        // interleaved with the declarations in.
        var tlsStatements = ast.Items
            .OfType<StatementItemTopLevelItemNode>()
            .Select(s => s.Statement)
            .ToImmutableArray();

        Method? topLevelMethod = null;
        if(!tlsStatements.IsEmpty)
        {
            topLevelMethod = SynthesizeTopLevelProgram(fileLevel, fileNamespace, tlsStatements);
        }

        return new TypeBuilderResult([.. registeredTypes], topLevelMethod);
    }

    /// <summary>
    /// Makes a top-level type nameable from this file's namespace blocks. Accepts null so callers
    /// need not check: an unrecognized declaration registered nothing and has nothing to publish.
    /// </summary>
    private static void PublishTopLevel(FileScope fileScope, MistType? typeDef)
    {
        if(typeDef is not null)
        {
            fileScope.DeclareTopLevelType(typeDef.Name, typeDef);
        }
    }

    /// <summary>
    /// Wraps the file's top-level statements in a <c>Main</c> on a synthesized
    /// <c>$TopLevel</c> class, and returns that method.
    /// <para>
    /// The class is registered even though it is never emitted as a declaration, because
    /// registration is what puts it in Phase 3's walk -- and Phase 3 is what binds the
    /// statements. It also owns the scope the method binds against, which a bare method
    /// would have nowhere to get.
    /// </para>
    /// <para>
    /// Deliberately NOT recorded among the assembly's entry points. A declared <c>Main</c>
    /// elsewhere in the project is still a valid method and must still be recorded, so
    /// choosing between the two is left to whoever judges the finished model, decided from
    /// <c>entry_point</c> in <c>mist.toml</c>.
    /// </para>
    /// </summary>
    private Method SynthesizeTopLevelProgram(
        NamespaceScope fileLevel,
        string fileNamespace,
        ImmutableArray<StatementNode> statements)
    {
        var name = topLevelPrograms == 0 ? TopLevelTypeName : $"{TopLevelTypeName}{topLevelPrograms}";
        topLevelPrograms++;

        var syntheticType = new ClassType(
            name, fileNamespace, fileLevel, declaringType: null, declarationNode: null)
        {
            Assembly = currentAssembly,
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true },
        };

        var voidType = context.Resolver.LookupWellKnown(WellKnownTypes.Void);
        var stringType = context.Resolver.LookupWellKnown(WellKnownTypes.String);
        var stringArrayType = context.Resolver.ConstructArray(stringType, null);
        var syntheticBlock = new BlockNode { Statements = statements };

        var argsParam = new Parameter("args", stringArrayType);
        var mainMethod = new Method(
            "Main",
            voidType,
            [argsParam],
            new MemberModifiers { Accessibility = Accessibility.Public, IsStatic = true })
        {
            Body = new Block(syntheticBlock),
            Scope = syntheticType.Scope,
            IsTopLevelProgram = true,
        };

        syntheticType.MethodsList.Add(mainMethod);
        _ = context.ProgramModel.RegisterType(syntheticType);

        return mainMethod;
    }

    /// <summary>
    /// The name of the synthesized top-level statement type. The <c>$</c> makes it
    /// unspellable as a Mist identifier, so it cannot collide with a declared type.
    /// </summary>
    private const string TopLevelTypeName = "$TopLevel";

    /// <summary>
    /// Numbers every synthesized type after the first, because two files of top-level statements can
    /// share a namespace -- a project and one it references, or two files the entry-point rule is
    /// about to refuse -- and a type that fails to register is never walked, so its statements would
    /// go unbound and be emitted as nothing.
    /// </summary>
    private int topLevelPrograms;

    /// <summary>
    /// Processes types inside a block namespace. Creates a namespace level for the block and
    /// registers types under it.
    /// </summary>
    private void ProcessBlockNamespace(Scope parentScope, string parentNamespace, BlockNamespaceNamespaceDeclarationNode blockNs)
    {
        var blockNamespace = BlockNamespaceName.Build(blockNs, parentNamespace);
        var blockScope = new NamespaceScope(parentScope, blockNamespace, context.ProgramModel);

        ProcessNamespaceBody(blockScope, blockNamespace, blockNs.Body, publishTo: null);
    }

    /// <summary>
    /// Processes namespace body items, registering any type declarations found.
    /// </summary>
    /// <param name="publishTo">
    /// The file scope when this body IS the file's top level (a file-scoped namespace), null when
    /// it is a block. Only the former's types are nameable from the file's namespace blocks.
    /// </param>
    private void ProcessNamespaceBody(
        Scope parentScope,
        string namespaceName,
        ImmutableArray<NamespaceBodyItemNode> body,
        FileScope? publishTo)
    {
        foreach(var bodyItem in body)
        {
            switch(bodyItem)
            {
                case NamespaceTypeItemNamespaceBodyItemNode typeItem:
                    var typeDef = RegisterType(parentScope, namespaceName, typeItem.TypeDeclaration, declaringType: null);
                    if(publishTo is not null)
                    {
                        PublishTopLevel(publishTo, typeDef);
                    }

                    break;

                case NamespaceExtensionItemNamespaceBodyItemNode extensionItem:
                    StashExtension(parentScope, namespaceName, extensionItem.ExtensionDeclaration);
                    break;

                case NamespaceNamespaceItemNamespaceBodyItemNode nestedNsItem
                    when nestedNsItem.NamespaceDeclaration is BlockNamespaceNamespaceDeclarationNode nestedBlockNs:
                    ProcessBlockNamespace(parentScope, namespaceName, nestedBlockNs);
                    break;
            }
        }
    }

    /// <summary>
    /// Stashes an extension block AST for deferred processing in Phase 2.
    /// The receiver type cannot be resolved until all types are registered.
    /// </summary>
    private void StashExtension(Scope scope, string namespaceName, ExtensionBlockDeclarationNode extensionDecl)
    {
        RegisterDeferredNamespace(namespaceName);
        context.PendingExtensions.Add(new PendingExtension(extensionDecl, scope, namespaceName));
    }

    private void StashConformance(Scope scope, string namespaceName, WithDeclarationNode withDecl)
    {
        RegisterDeferredNamespace(namespaceName);
        context.PendingConformances.Add(new PendingConformance(withDecl, scope, namespaceName));
    }

    /// <summary>
    /// Registered at the stash rather than when Phase 2 builds the declaration, because a using is
    /// judged before Phase 2 and a namespace holding nothing but extension blocks or <c>with</c>
    /// declarations is one a file may import.
    /// </summary>
    private void RegisterDeferredNamespace(string namespaceName)
    {
        _ = context.ProgramModel.GetOrAddNamespace(namespaceName);
    }

    /// <summary>
    /// Registers a single type declaration: creates the type shell, its scope,
    /// registers it in the namespace, and recurses into nested types.
    /// Newtypes and type aliases have no member bodies, so nested type
    /// registration is skipped for them.
    /// </summary>
    private DeclaredType? RegisterType(
        Scope parentScope,
        string namespaceName,
        TypeDeclarationNode typeDecl,
        MistType? declaringType)
    {
        var typeDef = CreateMistType(typeDecl, parentScope, namespaceName, declaringType);
        if(typeDef is null)
        {
            return null;
        }

        FinalizeRegistration(typeDef, declaringType);

        // Newtypes and type aliases don't have member bodies — skip nested
        // type registration for them.
        if(typeDef is Newtype or TypeAlias)
        {
            return typeDef;
        }

        // Recurse into nested types within the type body.
        RegisterNestedTypes(typeDef.Scope, typeDecl, typeDef);

        return typeDef;
    }

    private void RegisterNestedBodyType(Scope parentScope, TypeBodyDeclarationNode bodyDecl, DeclaredType declaringType)
    {
        var namespaceName = declaringType.NamespaceName;

        var name = BuildGenericName(bodyDecl.Name.Lexeme, bodyDecl.TypeParams);
        // Wrap in a synthetic TypeDeclarationNode for CreateMistType compatibility
        var wrappedDecl = new BodyTypeTypeDeclarationNode { TypeBody = bodyDecl };
        MemberType? typeDef = bodyDecl.Keyword switch
        {
            ClassKeywordTypeKeywordNode => new ClassType(name, namespaceName, parentScope, declaringType, wrappedDecl),
            StructKeywordTypeKeywordNode => new StructType(name, namespaceName, parentScope, declaringType, wrappedDecl),
            AbstractKeywordTypeKeywordNode => new AbstractType(name, namespaceName, parentScope, declaringType, wrappedDecl),
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

    private void RegisterNestedEnum(Scope parentScope, EnumDeclarationNode enumDecl, DeclaredType declaringType)
    {
        var namespaceName = declaringType.NamespaceName;

        var typeDef = new EnumType(enumDecl.Name.Lexeme, namespaceName, parentScope, declaringType, enumDecl);
        FinalizeRegistration(typeDef, declaringType);
    }

    private void RegisterNestedNewtype(Scope parentScope, NewtypeDeclarationNode newtypeDecl, DeclaredType declaringType)
    {
        var namespaceName = declaringType.NamespaceName;

        var name = BuildGenericName(newtypeDecl.Name.Lexeme, newtypeDecl.TypeParams);
        var typeDef = new Newtype(name, namespaceName, parentScope, declaringType, newtypeDecl);
        PopulateTypeParameters(typeDef, newtypeDecl.TypeParams);
        FinalizeRegistration(typeDef, declaringType);
    }

    private void RegisterNestedTypeAlias(Scope parentScope, TypeAliasDeclarationNode aliasDecl, DeclaredType declaringType)
    {
        var namespaceName = declaringType.NamespaceName;

        var name = BuildGenericName(aliasDecl.Name.Lexeme, aliasDecl.TypeParams);
        var typeDef = new TypeAlias(name, namespaceName, parentScope, declaringType, aliasDecl);
        PopulateTypeParameters(typeDef, aliasDecl.TypeParams);
        FinalizeRegistration(typeDef, declaringType);
    }

    /// <summary>
    /// Shared registration logic: adds the type to the flat FQN dictionary,
    /// the declaring type, and the scope.
    /// Reports a diagnostic if a type with the same FQN already exists.
    /// </summary>
    private void FinalizeRegistration(DeclaredType typeDef, MistType? declaringType)
    {
        typeDef.Assembly = currentAssembly;

        // Register in the flat FQN dictionary.
        if(!context.ProgramModel.RegisterType(typeDef))
        {
            var declared = typeDef switch
            {
                MemberType member => (Storm.Runtime.SyntaxNode?)member.DeclarationNode,
                TypeAlias alias => alias.DeclarationNode,
                Newtype newtype => newtype.DeclarationNode,
                EnumType enumType => enumType.DeclarationNode,
                _ => null,
            };

            if(declared is null)
            {
                context.Diagnostics.Report(ProgramModelDiagnostics.DuplicateTypeName, default, "", typeDef.Name, typeDef.NamespaceName);
            }
            else
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.DuplicateTypeName, declared, null, typeDef.Name, typeDef.NamespaceName);
            }
        }

        // Ensure the namespace exists (for extensions/conformances that reference it).
        _ = context.ProgramModel.GetOrAddNamespace(typeDef.NamespaceName);

        // If nested, add to the declaring type. That is also what makes it visible by simple name
        // inside that type, since the declaring type's scope reads NestedTypes rather than keeping
        // a table of its own.
        if(declaringType is not null)
        {
            declaringType.NestedTypes.Add(typeDef);
        }
        else
        {
            // Only the top level is collected: a nested type is emitted inside its declaring
            // type's body, so listing it again would emit it twice.
            registeredTypes.Add(typeDef);
        }
    }

    /// <summary>
    /// Iterates over the members of a type declaration and registers any
    /// nested type declarations found.
    /// </summary>
    private void RegisterNestedTypes(Scope typeScope, TypeDeclarationNode typeDecl, DeclaredType declaringType)
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
    private DeclaredType? CreateMistType(TypeDeclarationNode typeDecl, Scope parentScope, string namespaceName, MistType? declaringType)
    {
        switch(typeDecl)
        {
            case BodyTypeTypeDeclarationNode body:
                var name = BuildGenericName(body.TypeBody.Name.Lexeme, body.TypeBody.TypeParams);
                MemberType typeDef = body.TypeBody.Keyword switch
                {
                    ClassKeywordTypeKeywordNode => new ClassType(name, namespaceName, parentScope, declaringType, typeDecl),
                    StructKeywordTypeKeywordNode => new StructType(name, namespaceName, parentScope, declaringType, typeDecl),
                    AbstractKeywordTypeKeywordNode => new AbstractType(name, namespaceName, parentScope, declaringType, typeDecl),
                    _ => throw new InvalidOperationException($"Unexpected type keyword: {body.TypeBody.Keyword.GetType().Name}")
                };

                PopulateTypeParameters(typeDef, body.TypeBody.TypeParams);
                return typeDef;

            case EnumTypeTypeDeclarationNode et:
                return new EnumType(et.EnumDeclaration.Name.Lexeme, namespaceName, parentScope, declaringType, et.EnumDeclaration);

            case NewtypeTypeTypeDeclarationNode nt:
                var ntName = BuildGenericName(nt.NewtypeDeclaration.Name.Lexeme, nt.NewtypeDeclaration.TypeParams);
                var newtype = new Newtype(ntName, namespaceName, parentScope, declaringType, nt.NewtypeDeclaration);
                PopulateTypeParameters(newtype, nt.NewtypeDeclaration.TypeParams);
                return newtype;

            case TypeAliasTypeTypeDeclarationNode ta:
                var taName = BuildGenericName(ta.TypeAliasDeclaration.Name.Lexeme, ta.TypeAliasDeclaration.TypeParams);
                var alias = new TypeAlias(taName, namespaceName, parentScope, declaringType, ta.TypeAliasDeclaration);
                PopulateTypeParameters(alias, ta.TypeAliasDeclaration.TypeParams);
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
    /// Creates <see cref="TypeParameter"/> instances from the AST type parameter list and adds them
    /// to the declaring type's own list. One overload for every type kind now that the namespace
    /// comes off the declaring type rather than the scope it was declared in.
    /// </summary>
    private void PopulateTypeParameters<T>(T declaringType, TypeParameterListNode? typeParams)
        where T : MistType, IHasTypeParameters
    {
        TypeParameterPopulator.Populate(
            declaringType, declaringType.Scope, declaringType, typeParams, context.Diagnostics);
    }
}