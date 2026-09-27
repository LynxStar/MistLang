using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
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
                case InvocationExpression { Method: { Modifiers.IsStatic: false } method } invocation:
                    JudgeReceiver(context, tiers, invocation, method);
                    JudgeLends(context, invocation.Arguments, method.ParametersList);
                    break;

                case InvocationExpression { Method: { } method } invocation:
                    JudgeLends(context, invocation.Arguments, method.ParametersList);
                    break;

                case NewExpression { Constructor: { } constructor } creation:
                    JudgeLends(context, creation.Arguments, constructor.ParametersList);
                    break;

                case LocalDeclarationStatement
                {
                    Local.Ownership: OwnershipKind.Owned,
                    Initializer: MemberAccessExpression
                    {
                        Receiver: LocalReferenceExpression { Local: { Ownership: OwnershipKind.Unrestricted, IsRef: false } holder },
                        Member: FieldResult { Field: { } extracted },
                    } access,
                } when ThisTiers.IsOwned(extracted):
                    context.Report(ProgramAnalysisDiagnostics.SharedExtract, access.Node, [access], extracted.Name, holder.Name);
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
                    method.Name, ThisTiers.Display(declared), ThisTiers.Display(needed));
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
        var provided = tiers.Provided(receiver, context.MemberOf(invocation));

        if(required > provided)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ThisTierMismatch, invocation.Node, [invocation, receiver],
                method.Name, ThisTiers.Display(required), Display(receiver), ThisTiers.Display(provided));
        }
    }

    private static void JudgeLends(AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter> parameters)
    {
        for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
        {
            var required = parameters[i].Modifier.Ownership switch
            {
                OwnershipKind.Borrowed => ThisTier.Borrowed,
                OwnershipKind.Frozen => ThisTier.Frozen,
                _ => ThisTier.None,
            };

            if(required == ThisTier.None || !RefusesLend(arguments[i], required) || arguments[i] is not LocalReferenceExpression argument)
            {
                continue;
            }

            context.Report(
                ProgramAnalysisDiagnostics.IncompatibleLend, argument.Node, [argument],
                ThisTiers.Display(Given(argument.Local)), argument.Local.Name, ThisTiers.Display(required), parameters[i].Name);
        }
    }

    internal static bool RefusesLend(Expression argument, ThisTier required)
    {
        return argument is LocalReferenceExpression { Local: var local } && !ThisTiers.IsValue(argument.Type) && Given(local) < required;
    }

    internal static bool RefusesReceiver(Expression? receiver, ThisTier required)
    {
        return receiver switch
        {
            LocalReferenceExpression { Local: var local } => ThisTiers.Declared(local) < required,
            FieldReferenceExpression { Field: var field } => !ThisTiers.IsOwned(field) && required > ThisTier.None,
            _ => false,
        };
    }

    private static ThisTier Given(LocalVariable local)
    {
        return local.Parameter is null && local.Ownership == OwnershipKind.Unrestricted && !local.IsRef
            ? ThisTier.None
            : ThisTiers.Declared(local);
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