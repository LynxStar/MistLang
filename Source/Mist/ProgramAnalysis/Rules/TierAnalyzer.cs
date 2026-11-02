using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class TierAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var tiers = new ThisTiers();

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case InvocationExpression invocation:
                    if(invocation.Method is { Modifiers.IsStatic: false } method)
                    {
                        JudgeReceiver(context, tiers, invocation, method);
                    }

                    JudgeArguments(context, tiers, invocation, invocation.Arguments, invocation.Parameters);
                    break;

                case NewExpression creation:
                    JudgeArguments(context, tiers, creation, creation.Arguments, creation.Constructor?.ParametersList);
                    break;

                case LocalDeclarationStatement { Initializer: { } initializer } declaration:
                    JudgeExtraction(context, initializer, declaration.Local.Ownership, declaration.Local.IsRef);
                    if(OwnedProvenance.SharedExtraction(initializer) is null)
                    {
                        var required = declaration.Local.Ownership switch
                        {
                            OwnershipKind.Borrowed => ThisTier.Borrowed,
                            OwnershipKind.Frozen => ThisTier.Frozen,
                            _ => ThisTier.None,
                        };
                        JudgeAccess(context, tiers, initializer, initializer, required, declaration.Local.Name);
                    }

                    break;

                case AssignmentExpression { IsCoalescing: false } assignment:
                    foreach(var (target, value) in Deconstructions.Pairs(assignment))
                    {
                        if(target is LocalReferenceExpression { Local: var written })
                        {
                            JudgeExtraction(context, value, written.IsRef ? OwnershipKind.Unrestricted : written.Ownership, byReference: false);
                        }
                    }

                    break;
            }
        }

        foreach(var root in context.Bodies)
        {
            if(root.Member is Method { DeclaredThisTier: { } declared } method && ReferenceEquals(root.Root, method.Body)
                && tiers.Inferred(method) is var needed && needed > declared)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ThisTierInsufficient, method.DeclarationNode!, [],
                    new NameValue(method.Name), new EnumValue(ThisTiers.Display(declared)), new EnumValue(ThisTiers.Display(needed)));
            }
        }
    }

    private static void JudgeReceiver(AnalysisContext context, ThisTiers tiers, InvocationExpression invocation, Method method)
    {
        var receiver = ThisTiers.ReceiverOf(invocation);

        if(receiver is null || ThisTiers.IsValue(receiver.Type))
        {
            return;
        }

        var required = tiers.Required(method);
        var provided = receiver is ThisExpression ? tiers.Provided(receiver, context.MemberOf(invocation))
            : LocalOwnership.For(context.RootOf(invocation) ?? invocation).Provided(receiver);

        if(required > provided)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ThisTierMismatch, invocation.Node, [invocation, receiver],
                new NameValue(method.Name), new EnumValue(ThisTiers.Display(required)), new NameValue(Display(receiver)), new EnumValue(ThisTiers.Display(provided)));
        }
    }

    private static void JudgeArguments(
        AnalysisContext context, ThisTiers tiers, Expression call, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters)
    {
        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            JudgeExtraction(context, arguments[i], parameters[i].Modifier.Ownership, parameters[i].Modifier.IsRef);
            JudgeLend(context, tiers, call, arguments[i], parameters[i]);
        }
    }

    private static void JudgeLend(AnalysisContext context, ThisTiers tiers, Expression call, Expression argument, Parameter parameter)
    {
        JudgeAccess(context, tiers, call, argument, ThisTiers.LendTier(parameter), parameter.Name);
    }

    private static void JudgeAccess(AnalysisContext context, ThisTiers tiers, Expression call, Expression argument, ThisTier required, string target)
    {
        if(required == ThisTier.None || ThisTiers.IsValue(argument.Type))
        {
            return;
        }

        var given = argument is ThisExpression ? tiers.Provided(argument, context.MemberOf(call))
            : LocalOwnership.For(context.RootOf(call) ?? call).Provided(argument);

        if(given < required)
        {
            context.Report(
                ProgramAnalysisDiagnostics.IncompatibleLend, argument.Node, [argument],
                new EnumValue(ThisTiers.Display(given)), new NameValue(Display(argument)), new EnumValue(ThisTiers.Display(required)), new NameValue(target));
        }
    }

    private static void JudgeExtraction(AnalysisContext context, Expression value, OwnershipKind destination, bool byReference)
    {
        if(OwnedProvenance.SharedExtraction(value) is not var (field, holder))
        {
            return;
        }

        if(LocalOwnership.For(context.RootOf(value) ?? value).Of(holder) > ThisTier.None)
        {
            return;
        }

        var refused = byReference
            || destination is OwnershipKind.Owned or OwnershipKind.Frozen or OwnershipKind.Borrowed
            || (!ThisTiers.IsValue(field.Type) && !Origins.IsUnchangeable(field.Type));

        if(refused)
        {
            context.Report(ProgramAnalysisDiagnostics.SharedExtract, value.Node, [value], new NameValue(field.Name), new NameValue(holder.Name));
        }
    }

    internal static bool RefusesLend(Expression argument, ThisTier required, LocalOwnership? locals = null)
    {
        return !ThisTiers.IsValue(argument.Type) && (locals is not null ? locals.Provided(argument) < required
            : OwnershipAnalyzer.Selected(argument).OfType<LocalReferenceExpression>().Any(reference => Given(reference.Local) < required));
    }

    internal static bool RefusesReceiver(Expression? receiver, ThisTier required, LocalOwnership? locals = null)
    {
        if(receiver is not null && locals is not null)
        {
            return locals.Provided(receiver) < required;
        }

        return receiver switch
        {
            LocalReferenceExpression { Local: var local } => Given(local) < required,
            FieldReferenceExpression { Field: var field } => !ThisTiers.IsOwned(field) && required > ThisTier.None,
            _ => false,
        };
    }

    private static ThisTier Given(LocalVariable local)
    {
        return local.Parameter is null && local.Ownership == OwnershipKind.Unrestricted && !local.IsRef
            ? Exclusive(local.Type) ? ThisTier.Owned : ThisTier.None
            : ThisTiers.Declared(local);
    }

    /// <summary>
    /// A linear or affine binding is the one holder its discipline allows, so it lends with the access an
    /// owned binding does; an unrestricted binding of any other type may be aliased and lends nothing.
    /// </summary>
    private static bool Exclusive(IMistType type)
    {
        return OwnershipAnalyzer.Substructural(type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }

    private static string Display(Expression receiver)
    {
        return receiver switch
        {
            LocalReferenceExpression local => local.Local.Name,
            FieldReferenceExpression field => field.Field.Name,
            ThisExpression => "this",
            _ => receiver.GetType().Name,
        };
    }
}