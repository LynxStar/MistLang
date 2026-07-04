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
    private readonly ParameterBuilder parameterBuilder;

    public ConstructorBuilder(BuilderContext context, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves a constructor declaration on a concrete type.
    /// </summary>
    public void Resolve(ConstructorDeclarationNode ctorDecl, ConcreteType concreteType)
    {
        var modifiers = ModifierBuilder.Build(ctorDecl.Modifiers, context.Diagnostics, ctorDecl);
        var parameters = parameterBuilder.Resolve(ctorDecl.Parameters, concreteType.Scope);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, ctorDecl, null);
        }

        Block? body = null;

        if(ctorDecl.Body is MethodBlockBodyMethodBodyNode blockBody)
        {
            body = new Block(blockBody.Block);
        }

        if(modifiers.IsIntrinsic)
        {
            if(body is not null)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, ctorDecl, null, concreteType.Name);
            }

            if(!concreteType.IsIntrinsic)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, ctorDecl, null, concreteType.Name);
            }
        }
        else if(body is null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.MethodMustHaveBody, ctorDecl, null, concreteType.Name);
        }

        var constructor = new Constructor(concreteType.Name, concreteType, parameters, modifiers)
        {
            Body = body,
        };

        concreteType.ConstructorsList.Add(constructor);
    }
}