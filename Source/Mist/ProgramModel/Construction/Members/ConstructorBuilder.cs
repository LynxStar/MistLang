using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves constructor declarations into <see cref="Constructor"/> instances.
/// Constructors are methods whose return type is the declaring type.
/// </summary>
internal sealed class ConstructorBuilder
{
    private readonly BuilderContext context;
    private readonly DelegateResolver delegateResolver;

    public ConstructorBuilder(BuilderContext context, DelegateResolver delegateResolver)
    {
        this.context = context;
        this.delegateResolver = delegateResolver;
    }

    /// <summary>
    /// Resolves a constructor declaration on a concrete type.
    /// </summary>
    public void Resolve(ConstructorDeclarationNode ctorDecl, ConcreteType concreteType)
    {
        // A constructor needs a member scope of its own for the same reason a method does: it
        // is the parent of the body scope, so without it the body chains to nothing and no
        // bare name in a constructor can reach the declaring type.
        var constructorScope = new MethodScope(concreteType.Scope);

        var common = delegateResolver.ResolveFullDelegate(ctorDecl, constructorScope, concreteType, concreteType.Name);
        constructorScope.IsStatic = common.Modifiers.IsStatic;

        if(common.Modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, ctorDecl, null);
        }

        if(common.DeclaredThisTier is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.ThisTierOnConstructor, ctorDecl, null);
        }

        var constructor = new Constructor(concreteType.Name, concreteType, common.Parameters, common.Modifiers)
        {
            DeclaringType = concreteType,
            DeclarationNode = ctorDecl,
            Body = common.Body,
            Scope = constructorScope,
            ChainKind = ResolveChainKind(ctorDecl.Chain),
            ChainNode = ctorDecl.Chain,
        };

        constructor.ContextBindingsList.AddRange(common.ContextBindings);

        context.Attributes.Build(ctorDecl.Attributes, constructor.DeclaredAttributesList, concreteType.Scope);
        concreteType.ConstructorsList.Add(constructor);
    }

    /// <summary>
    /// Maps the chain syntax to its kind. The arguments are left for Phase 3, since they
    /// reference the constructor's own parameters.
    /// </summary>
    private static ConstructorChainKind ResolveChainKind(ConstructorChainNode? chain)
    {
        return chain switch
        {
            ThisChainConstructorChainNode => ConstructorChainKind.This,
            BaseChainConstructorChainNode => ConstructorChainKind.Base,
            _ => ConstructorChainKind.None,
        };
    }
}