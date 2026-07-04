using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves cast operator declarations into <see cref="CastOperator"/> instances
/// and adds them to the owning concrete type.
/// </summary>
internal sealed class CastOperatorBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly ParameterBuilder parameterBuilder;

    public CastOperatorBuilder(BuilderContext context, TypeReferenceResolver typeResolver, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves a cast operator declaration and adds it to the concrete type.
    /// </summary>
    public void Resolve(CastOperatorDeclarationNode castOpDecl, ConcreteType typeDef)
    {
        var isImplicit = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode;
        var targetType = typeResolver.Resolve(castOpDecl.TargetType, typeDef.Scope);
        var modifiers = ModifierBuilder.Build(castOpDecl.Modifiers, context.Diagnostics, castOpDecl);
        var parameters = parameterBuilder.Resolve(castOpDecl.Parameters, typeDef.Scope);
        var body = CallableValidation.ResolveBody(castOpDecl.Body);

        if(!modifiers.IsStatic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorMustBeStatic, castOpDecl, null);
        }

        if(parameters.Count == 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.CastOperatorMissingParameter, castOpDecl, null);
            return;
        }

        var sourceParameter = parameters[0];

        if(parameters.Count > 1)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.CastOperatorTooManyParameters, castOpDecl, null);
        }

        CallableValidation.ValidateIntrinsicOrBody(castOpDecl, typeDef, "cast operator", modifiers, body, context.Diagnostics);

        var castOperator = new CastOperator(targetType, sourceParameter, isImplicit, modifiers) { Body = body };
        typeDef.CastOperatorsList.Add(castOperator);
    }
}