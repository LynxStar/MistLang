using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Resolves operator declarations into <see cref="Operator"/> instances
/// and adds them to the owning member type.
/// </summary>
internal sealed class OperatorBuilder
{
    private readonly BuilderContext context;
    private readonly TypeReferenceResolver typeResolver;
    private readonly ParameterBuilder parameterBuilder;

    public OperatorBuilder(BuilderContext context, TypeReferenceResolver typeResolver, ParameterBuilder parameterBuilder)
    {
        this.context = context;
        this.typeResolver = typeResolver;
        this.parameterBuilder = parameterBuilder;
    }

    /// <summary>
    /// Resolves an operator declaration and adds it to the member type.
    /// </summary>
    public void Resolve(OperatorDeclarationNode opDecl, MemberType memberType)
    {
        var kind = MapOperatorSymbol(opDecl.Symbol);
        var returnType = typeResolver.Resolve(opDecl.ReturnType, memberType.Scope);
        var modifiers = ModifierBuilder.Build(opDecl.Modifiers, context.Diagnostics, opDecl);
        var parameters = parameterBuilder.Resolve(opDecl.Parameters, memberType.Scope);

        if(!modifiers.IsStatic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorMustBeStatic, opDecl, null);
        }

        ValidateParameterCount(kind, parameters.Count, opDecl);

        Block? body = null;

        if(opDecl.Body is CallableBlockBodyCallableBodyNode blockBody)
        {
            body = new Block(blockBody.Block);
        }

        if(modifiers.IsIntrinsic)
        {
            ValidateIntrinsic(opDecl, memberType, body);
        }
        else if(body is null && memberType is not AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.OperatorMustHaveBody, opDecl, null);
        }

        var op = new Operator(kind, returnType, parameters, modifiers) { Body = body };
        memberType.OperatorsList.Add(op);
    }

    private void ValidateIntrinsic(OperatorDeclarationNode opDecl, MemberType memberType, Block? body)
    {
        if(memberType is AbstractType)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicNotValidOnMember, opDecl, null, "abstract operators");
            return;
        }

        if(body is not null)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicMemberCannotHaveBody, opDecl, null, "operator");
        }

        var typeIsIntrinsic = memberType is ConcreteType concrete && concrete.IsIntrinsic;

        if(!typeIsIntrinsic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.IntrinsicRequiresIntrinsicType, opDecl, null, memberType.Name);
        }
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

    private static OperatorKind MapOperatorSymbol(OperatorSymbolNode symbol)
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