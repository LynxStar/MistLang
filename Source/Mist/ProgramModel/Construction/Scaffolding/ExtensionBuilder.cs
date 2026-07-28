using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Phase 2 builder for extension blocks. Processes all <see cref="PendingExtension"/>
/// entries stashed during Phase 1: resolves receiver types, builds members, registers
/// on the namespace, and validates duplicate members across blocks targeting the same type.
/// </summary>
internal sealed class ExtensionBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;
    private readonly MethodBuilder methodBuilder;
    private readonly PropertyBuilder propertyBuilder;
    private readonly ConstraintResolver constraintResolver;

    public ExtensionBuilder(BuilderContext context, ScopeResolver scopeResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        constraintResolver = new ConstraintResolver(context, scopeResolver);
        var parameterBuilder = new ParameterBuilder(scopeResolver);
        var delegateResolver = new DelegateResolver(context, parameterBuilder, scopeResolver);
        var accessorBuilder = new AccessorBuilder(context);
        methodBuilder = new MethodBuilder(scopeResolver, delegateResolver, constraintResolver);
        propertyBuilder = new PropertyBuilder(context, scopeResolver, accessorBuilder);
    }

    /// <summary>
    /// Processes all pending extensions: resolves receiver types, builds member
    /// signatures, registers on namespaces, and checks for duplicate members.
    /// </summary>
    public void BuildAll()
    {
        foreach(var pending in context.PendingExtensions)
        {
            Build(pending);
        }

        ValidateDuplicateMembers();
    }

    /// <summary>
    /// Processes a single pending extension block.
    /// </summary>
    private void Build(PendingExtension pending)
    {
        var ast = pending.Ast;
        var namespaceName = pending.NamespaceName;

        // Create the extension block's own scope, beneath the namespace level it was written in.
        var extensionScope = new ExtensionScope(pending.DeclaringScope);

        var receiverName = ast.ReceiverName.Lexeme;

        var extensionBlock = new ExtensionBlock(receiverName, namespaceName, extensionScope, ast);

        // Populate extension-level type parameters (if generic extension).
        if(ast.TypeParams is not null)
        {
            TypeParameterPopulator.Populate(
                extensionBlock.TypeParametersList, extensionScope, null, namespaceName, ast.TypeParams);
        }

        // Resolve the receiver type reference.
        var receiverType = scopeResolver.Resolve(ast.ReceiverType, extensionScope);
        extensionBlock.ResolvedReceiverType = receiverType;

        // Resolve constraints on extension-level type parameters.
        // Extension blocks don't have where clauses in the current grammar,
        // but this is forward-compatible if they're added.

        // Resolve members.
        foreach(var member in ast.Members)
        {
            switch(member)
            {
                case ExtensionMethodMemberExtensionMemberDeclarationNode methodMember:
                    methodBuilder.Resolve(methodMember.Method, extensionBlock);
                    break;

                case ExtensionPropertyMemberExtensionMemberDeclarationNode propertyMember:
                    propertyBuilder.Resolve(propertyMember.Property, extensionBlock);
                    break;

                default:
                    throw new InvalidOperationException($"Unrecognized extension member declaration node: {member.GetType().Name}");
            }
        }

        // Register on the namespace.
        var ns = context.ProgramModel.GetOrAddNamespace(namespaceName);

        if(!ns.Extensions.TryGetValue(receiverType, out var list))
        {
            list = [];
            ns.Extensions[receiverType] = list;
        }

        list.Add(extensionBlock);
    }

    /// <summary>
    /// After all extension blocks are built, checks for duplicate member names
    /// across blocks targeting the same receiver type within the same namespace.
    /// </summary>
    private void ValidateDuplicateMembers()
    {
        foreach(var ns in context.ProgramModel.Namespaces.Values)
        {
            foreach(var (_, blocks) in ns.Extensions)
            {
                if(blocks.Count < 2)
                {
                    continue;
                }

                CheckDuplicateMethods(blocks);
                CheckDuplicateProperties(blocks);
            }
        }
    }

    private void CheckDuplicateMethods(List<ExtensionBlock> blocks)
    {
        var seen = new Dictionary<string, ExtensionBlock>();

        foreach(var block in blocks)
        {
            // Skip ambient extension blocks — no source location to report against.
            if(block.DeclarationNode is null)
            {
                continue;
            }

            foreach(var method in block.MethodsList)
            {
                if(seen.TryGetValue(method.Name, out _))
                {
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.DuplicateExtensionMember,
                        block.DeclarationNode,
                        null,
                        method.Name,
                        block.ResolvedReceiverType?.Name ?? "?");
                }
                else
                {
                    seen[method.Name] = block;
                }
            }
        }
    }

    private void CheckDuplicateProperties(List<ExtensionBlock> blocks)
    {
        var seen = new Dictionary<string, ExtensionBlock>();

        foreach(var block in blocks)
        {
            // Skip ambient extension blocks — no source location to report against.
            if(block.DeclarationNode is null)
            {
                continue;
            }

            foreach(var property in block.PropertiesList)
            {
                if(seen.TryGetValue(property.Name, out _))
                {
                    context.Diagnostics.ReportAtNode(
                        ProgramModelDiagnostics.DuplicateExtensionMember,
                        block.DeclarationNode,
                        null,
                        property.Name,
                        block.ResolvedReceiverType?.Name ?? "?");
                }
                else
                {
                    seen[property.Name] = block;
                }
            }
        }
    }

}