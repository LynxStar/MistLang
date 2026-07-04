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
        var body = CallableValidation.ResolveBody(ctorDecl.Body);

        if(modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, ctorDecl, null);
        }

        CallableValidation.ValidateIntrinsicOrBody(ctorDecl, concreteType, concreteType.Name, modifiers, body, context.Diagnostics);

        var constructor = new Constructor(concreteType.Name, concreteType, parameters, modifiers)
        {
            Body = body,
        };

        concreteType.ConstructorsList.Add(constructor);
    }
}