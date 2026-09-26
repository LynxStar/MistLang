using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramModel.Construction.Members;

/// <summary>
/// Shared accessor construction and validation logic used by both
/// <see cref="PropertyBuilder"/> and <see cref="IndexerBuilder"/>.
/// </summary>
internal sealed class AccessorBuilder
{
    private readonly BuilderContext context;

    public AccessorBuilder(BuilderContext context)
    {
        this.context = context;
    }

    public PropertyAccessor BuildGetter(
        ModifierNode? modifier, string memberName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isAuto, BlockNode? body, ExpressionNode? expression)
    {
        if(modifier is not null && !RefusesPromise(modifier, locationNode))
        {
            if(isAbstract)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, memberName);
            }
            else
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.GetterCannotHaveModifier, locationNode, null, memberName);
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new UnresolvedExpression(expression) : null,
        };
    }

    public PropertyAccessor BuildSetter(
        ModifierNode? modifier, string memberName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isAuto, bool isInit, BlockNode? body, ExpressionNode? expression)
    {
        var accessibility = Accessibility.Public;

        if(modifier is not null && !RefusesPromise(modifier, locationNode))
        {
            if(isAbstract)
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, memberName);
            }
            else if(modifier is PrivateModifierModifierNode)
            {
                accessibility = Accessibility.Private;
            }
            else
            {
                context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.SetterModifierMustBePrivate, locationNode, null, memberName);
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            IsInit = isInit,
            Accessibility = accessibility,
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new UnresolvedExpression(expression) : null,
        };
    }

    public PropertyAccessor? BuildInitAccessor(
        ModifierNode? modifier, string memberName, Storm.Runtime.SyntaxNode locationNode,
        bool isAbstract, bool isStatic, bool isAuto, BlockNode? body, ExpressionNode? expression)
    {
        if(isAbstract)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.AbstractPropertyInitAccessor, locationNode, null, memberName);
            return null;
        }

        if(isStatic)
        {
            context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.StaticPropertyInitAccessor, locationNode, null, memberName);
        }

        // Built as a plain setter once refused, rather than dropped as the abstract case is, so a write to it is
        // not refused again: the mistake is the declaration's, and it has been reported there.
        return BuildSetter(modifier, memberName, locationNode, isAbstract: false, isAuto, isInit: !isStatic, body, expression);
    }

    private bool RefusesPromise(ModifierNode modifier, Storm.Runtime.SyntaxNode locationNode)
    {
        var written = modifier switch
        {
            PureModifierModifierNode => "pure",
            LocalModifierModifierNode => "local",
            _ => null,
        };

        if(written is null)
        {
            return false;
        }

        context.Diagnostics.ReportAtNode(ProgramModelDiagnostics.PurityModifierPlacement, locationNode, null, written, "an accessor", string.Empty);
        return true;
    }
}