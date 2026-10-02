using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Resolves operator declarations into <see cref="Operator"/> instances
/// and adds them to the owning member type.
/// </summary>
internal sealed class OperatorBuilder
{
    private readonly BuilderContext context;
    private readonly ScopeResolver scopeResolver;
    private readonly DelegateResolver delegateResolver;

    public OperatorBuilder(BuilderContext context, ScopeResolver scopeResolver, DelegateResolver delegateResolver)
    {
        this.context = context;
        this.scopeResolver = scopeResolver;
        this.delegateResolver = delegateResolver;
    }

    /// <summary>
    /// Resolves an operator declaration and adds it to the member type.
    /// </summary>
    public void Resolve(OperatorDeclarationNode opDecl, MemberType memberType)
    {
        var operatorScope = new MethodScope(memberType.Scope);

        var common = delegateResolver.ResolveDelegate(opDecl, operatorScope, memberType, "operator");
        operatorScope.IsStatic = common.Modifiers.IsStatic;

        var kind = MapOperatorSymbol(opDecl.Symbol);
        var returnType = scopeResolver.Resolve(opDecl.ReturnType, operatorScope);

        if(!common.Modifiers.IsStatic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorMustBeStatic, opDecl, null);
        }

        if(common.Modifiers.IsAsync)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AsyncOnlyOnMethods, opDecl, null);
        }

        ValidateParameterCount(kind, common.Parameters.Count, opDecl);

        var op = new Operator(kind, returnType, common.Parameters, common.Modifiers)
        {
            Body = common.Body,
            Scope = operatorScope,
            DeclaringType = memberType,
            DeclarationNode = opDecl,
        };
        memberType.OperatorsList.Add(op);
    }

    private void ValidateParameterCount(OperatorKind kind, int count, OperatorDeclarationNode opDecl)
    {
        var isUnaryOnly = kind is OperatorKind.Exclamation;
        var canBeUnary = isUnaryOnly || kind is OperatorKind.Minus;

        if(count == 1 && !canBeUnary)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorWrongParameterCount, opDecl, null, "2", count.ToString());
        }
        else if(count == 2 && isUnaryOnly)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorWrongParameterCount, opDecl, null, "1", count.ToString());
        }
        else if(count is 0 or > 2)
        {
            var expected = canBeUnary ? "1 or 2" : "2";
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorWrongParameterCount, opDecl, null, expected, count.ToString());
        }
    }

    internal static OperatorKind MapOperatorSymbol(OperatorSymbolNode symbol)
    {
        return symbol switch
        {
            PlusOpOperatorSymbolNode => OperatorKind.Plus,
            MinusOpOperatorSymbolNode => OperatorKind.Minus,
            StarOpOperatorSymbolNode => OperatorKind.Star,
            SlashOpOperatorSymbolNode => OperatorKind.Slash,
            ModuloOpOperatorSymbolNode => OperatorKind.Modulo,
            EqualEqualOpOperatorSymbolNode => OperatorKind.EqualEqual,
            NotEqualOpOperatorSymbolNode => OperatorKind.NotEqual,
            LessThanOrEqualOpOperatorSymbolNode => OperatorKind.LessThanOrEqual,
            GreaterThanOrEqualOpOperatorSymbolNode => OperatorKind.GreaterThanOrEqual,
            LessThanOpOperatorSymbolNode => OperatorKind.LessThan,
            GreaterThanOpOperatorSymbolNode => OperatorKind.GreaterThan,
            LogicalAndOpOperatorSymbolNode => OperatorKind.LogicalAnd,
            LogicalOrOpOperatorSymbolNode => OperatorKind.LogicalOr,
            ExclamationOpOperatorSymbolNode => OperatorKind.Exclamation,
            _ => throw new InvalidOperationException($"Unrecognized operator symbol node: {symbol.GetType().Name}"),
        };
    }
}