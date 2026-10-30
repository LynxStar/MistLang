using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

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
        AccessorPromiseNode? promise, ModifierNode? modifier, AccessorSite site, Storm.Runtime.SyntaxNode locationNode,
        bool isAuto, BlockNode? body, ExpressionNode? expression)
    {
        if(modifier is not null && !RefusesPromise(modifier, locationNode))
        {
            if(site.IsAbstract)
            {
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, new NameValue(site.MemberName));
            }
            else
            {
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.GetterCannotHaveModifier, locationNode, null, new NameValue(site.MemberName));
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            OwnModifiers = OwnModifiers(promise, site, locationNode),
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new UnresolvedExpression(expression) : null,
        };
    }

    public PropertyAccessor BuildSetter(
        AccessorPromiseNode? promise, ModifierNode? modifier, AccessorSite site, Storm.Runtime.SyntaxNode locationNode,
        bool isAuto, bool isInit, BlockNode? body, ExpressionNode? expression)
    {
        var accessibility = Accessibility.Public;

        if(modifier is not null && !RefusesPromise(modifier, locationNode))
        {
            if(site.IsAbstract)
            {
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AbstractAccessorCannotHaveModifier, locationNode, null, new NameValue(site.MemberName));
            }
            else if(modifier is PrivateModifierModifierNode)
            {
                accessibility = Accessibility.Private;
            }
            else
            {
                context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.SetterModifierMustBePrivate, locationNode, null, new NameValue(site.MemberName));
            }
        }

        return new PropertyAccessor
        {
            IsAuto = isAuto,
            IsInit = isInit,
            Accessibility = accessibility,
            OwnModifiers = OwnModifiers(promise, site, locationNode),
            Body = body is not null ? new Block(body) : null,
            ExpressionBody = expression is not null ? new UnresolvedExpression(expression) : null,
        };
    }

    public PropertyAccessor? BuildInitAccessor(
        AccessorPromiseNode? promise, ModifierNode? modifier, AccessorSite site, Storm.Runtime.SyntaxNode locationNode,
        bool isAuto, BlockNode? body, ExpressionNode? expression)
    {
        if(site.IsAbstract)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.AbstractPropertyInitAccessor, locationNode, null, new NameValue(site.MemberName));
            return null;
        }

        var isStatic = site.Owner.IsStatic;

        if(isStatic)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.StaticPropertyInitAccessor, locationNode, null, new NameValue(site.MemberName));
        }

        // Built as a plain setter once refused, rather than dropped as the abstract case is, so a write to it is
        // not refused again: the mistake is the declaration's, and it has been reported there.
        return BuildSetter(promise, modifier, site with { IsAbstract = false }, locationNode, isAuto, isInit: !isStatic, body, expression);
    }

    /// <summary>
    /// The promise an accessor states for itself replaces its declaration's, and combines with its
    /// TYPE's as a member's own would (purity spec Req 1.5 and 15.5): a pure type refuses a local
    /// accessor, a local type yields to a pure one, and a readonly type keeps every accessor readonly.
    /// </summary>
    private MemberModifiers? OwnModifiers(AccessorPromiseNode? promise, AccessorSite site, Storm.Runtime.SyntaxNode locationNode)
    {
        if(promise is null)
        {
            return null;
        }

        var pure = promise is PureAccessorPromiseAccessorPromiseNode;
        var local = promise is LocalAccessorPromiseAccessorPromiseNode or LocalReadonlyAccessorPromiseAccessorPromiseNode;
        var isReadonly = promise is LocalReadonlyAccessorPromiseAccessorPromiseNode;

        if(site.DeclaringType is { } type)
        {
            if(type.Modifiers.IsPure)
            {
                if(local)
                {
                    context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PureAndLocal, locationNode, null);
                }

                pure = true;
            }

            isReadonly |= type.IsReadonly;
            local |= type.Modifiers.IsLocal && !pure && !site.Owner.IsStatic;
        }

        if(local && site.Owner.IsStatic)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.LocalRequiresInstance, locationNode, null);
        }

        return site.Owner with { IsPure = pure, IsReadonly = isReadonly || pure, IsLocal = local };
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

        context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.PurityModifierPlacement, locationNode, null, new EnumValue(written), new TextValue("an accessor"), new TextValue(string.Empty));
        return true;
    }
}

internal readonly record struct AccessorSite(string MemberName, MemberModifiers Owner, MemberType? DeclaringType, bool IsAbstract);