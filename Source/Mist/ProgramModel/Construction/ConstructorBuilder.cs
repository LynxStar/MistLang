using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

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
        var common = delegateResolver.ResolveFullDelegate(ctorDecl, concreteType.Scope, concreteType, concreteType.Name);

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
            Body = common.Body,
        };

        constructor.ContextBindingsList.AddRange(common.ContextBindings);

        concreteType.ConstructorsList.Add(constructor);
    }
}