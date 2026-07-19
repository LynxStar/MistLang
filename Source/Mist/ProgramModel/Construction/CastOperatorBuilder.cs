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
    private readonly ScopeResolver scopeResolver;
    private readonly DelegateResolver delegateResolver;

    public CastOperatorBuilder(BuilderContext context, ScopeResolver scopeResolver, DelegateResolver delegateResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        this.delegateResolver = delegateResolver;
    }

    /// <summary>
    /// Resolves a cast operator declaration and adds it to the concrete type.
    /// </summary>
    public void Resolve(CastOperatorDeclarationNode castOpDecl, ConcreteType typeDef)
    {
        var common = delegateResolver.ResolveDelegate(castOpDecl, typeDef.Scope, typeDef, "cast operator");
        var isImplicit = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode;
        var targetType = scopeResolver.Resolve(castOpDecl.TargetType, typeDef.Scope);

        if(!common.Modifiers.IsStatic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorMustBeStatic, castOpDecl, null);
        }

        if(common.Parameters.Count == 0)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.CastOperatorMissingParameter, castOpDecl, null);
            return;
        }

        var sourceParameter = common.Parameters[0];

        if(common.Parameters.Count > 1)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.CastOperatorTooManyParameters, castOpDecl, null);
        }

        var castOperator = new CastOperator(targetType, sourceParameter, isImplicit, common.Modifiers) { Body = common.Body };
        typeDef.CastOperatorsList.Add(castOperator);
    }
}