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
/// on the namespace.
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
        var parameterBuilder = new ParameterBuilder(scopeResolver, context.Attributes, context.Diagnostics);
        var delegateResolver = new DelegateResolver(context, parameterBuilder, scopeResolver);
        var accessorBuilder = new AccessorBuilder(context);
        methodBuilder = new MethodBuilder(context, scopeResolver, delegateResolver, constraintResolver);
        propertyBuilder = new PropertyBuilder(context, scopeResolver, accessorBuilder);
    }

    /// <summary>
    /// Processes all pending extensions: resolves receiver types, builds member
    /// signatures, and registers on namespaces.
    /// </summary>
    public void BuildAll()
    {
        foreach(var pending in context.PendingExtensions)
        {
            Build(pending);
        }
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
                extensionBlock, extensionScope, null, ast.TypeParams, context.Diagnostics, context.Domains);
        }

        // Resolve the receiver type reference.
        var receiverType = scopeResolver.Resolve(ast.ReceiverType, extensionScope);
        extensionBlock.ResolvedReceiverType = receiverType;

        // The receiver becomes nameable HERE, before members resolve, because a member signature may
        // already mention it. A local rather than a parameter: the emitted `this` parameter does not
        // exist until the carrier is written, and the scope chain answers locals.
        extensionBlock.Receiver = new LocalVariable(receiverName, receiverType);

        extensionBlock.CarrierName = CarrierNames.For(
            receiverType, pending.Unit?.SourcePath, pending.Unit?.Extensions.Count ?? 0);

        pending.Unit?.Extensions.Add(extensionBlock);

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
}