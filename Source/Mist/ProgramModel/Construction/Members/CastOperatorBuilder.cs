using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

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
        var castScope = new MethodScope(typeDef.Scope);

        var common = delegateResolver.ResolveDelegate(castOpDecl, castScope, typeDef, "cast operator");
        castScope.IsStatic = common.Modifiers.IsStatic;

        var isImplicit = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode;
        var targetType = scopeResolver.Resolve(castOpDecl.TargetType, castScope);

        if(!common.Modifiers.IsStatic)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.OperatorMustBeStatic, castOpDecl, null);
        }

        if(common.Modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AsyncOnlyOnMethods, castOpDecl, null);
        }

        if(common.Parameters.Count == 0)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.CastOperatorMissingParameter, castOpDecl, null);
            return;
        }

        var sourceParameter = common.Parameters[0];

        if(common.Parameters.Count > 1)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.CastOperatorTooManyParameters, castOpDecl, null);
        }

        var castOperator = new CastOperator(targetType, sourceParameter, isImplicit, common.Modifiers)
        {
            Body = common.Body,
            Scope = castScope,
            DeclaringType = typeDef,
            DeclarationNode = castOpDecl,
        };
        typeDef.CastOperatorsList.Add(castOperator);
    }
}