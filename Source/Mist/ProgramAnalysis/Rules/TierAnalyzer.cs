using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
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
                    _ = JudgeExtraction(context, initializer, declaration.Local.Ownership, declaration.Local.IsRef);
                    var required = declaration.Local.Ownership switch
                    {
                        OwnershipKind.Borrowed => ThisTier.Borrowed,
                        OwnershipKind.Frozen => ThisTier.Frozen,
                        _ => ThisTier.None,
                    };
                    JudgeAccess(context, tiers, initializer, initializer, required, declaration.Local.Name);
                    break;

                case AssignmentExpression { IsCoalescing: false } assignment:
                    foreach(var (target, value) in Deconstructions.Pairs(assignment))
                    {
                        if(target is LocalReferenceExpression { Local: var written })
                        {
                            _ = JudgeExtraction(context, value, written.IsRef ? OwnershipKind.Unrestricted : written.Ownership, byReference: false);
                        }
                    }

                    break;
            }
        }

        foreach(var root in context.Bodies)
        {
            if(ChainedParameter(root) is { } chained)
            {
                var argument = (Expression)root.Root;
                JudgeArgument(context, tiers, argument, argument, chained);
            }

            if(root.Member is Method { DeclaredThisTier: { } declared } method && ReferenceEquals(root.Root, method.Body)
                && tiers.Inferred(method) is var needed && needed > declared)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ThisTierInsufficient, method.DeclarationNode!, [],
                    new NameValue(method.Name), new EnumValue(ThisTiers.Display(declared)), new EnumValue(ThisTiers.Display(needed)));
            }
        }
    }

    internal static Parameter? ChainedParameter(SourceRoot root)
    {
        return root is { Member: Constructor { ChainedConstructor: { } chained } constructor, Root: Expression argument }
            && constructor.ChainArgumentsList.IndexOf(argument) is var index and >= 0
            ? index < chained.ParametersList.Count ? chained.ParametersList[index] : chained.ParametersList is [.., { IsFlattened: true } last] ? last : null
            : null;
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
            JudgeArgument(context, tiers, call, arguments[i], parameters[i]);
        }
    }

    private static void JudgeArgument(AnalysisContext context, ThisTiers tiers, Expression call, Expression argument, Parameter parameter)
    {
        if(!JudgeExtraction(context, argument, parameter.Modifier.Ownership, parameter.Modifier.IsRef)
            && PassedByBareRef(argument, parameter.Modifier, context.Model.Clones, context.RootOf(argument)) is var (kind, name))
        {
            context.Report(ProgramAnalysisDiagnostics.OwnedPassedByRef, argument.Node, [argument], new EnumValue(kind), new NameValue(name), new NameValue(parameter.Name));
        }

        JudgeLend(context, tiers, call, argument, parameter);
    }

    /// <summary>
    /// Enforces <see cref="ProgramAnalysisDiagnostics.OwnedPassedByRef"/> (ownership-v2 Requirement 9.9) through <see cref="RefAliases"/>.
    /// </summary>
    internal static (string Kind, string Name)? PassedByBareRef(Expression argument, ParameterModifier parameter, CloneDerivation clones, object? root)
    {
        if(!BareRef(parameter) || clones.Judge(argument.Type).Kind == CloneKind.Unchangeable)
        {
            return null;
        }

        return RefAliases.Of(argument, root).Select(Owned).FirstOrDefault(found => found is not null);
    }

    private static (string Kind, string Name)? Owned(RefAliases.Aliased place)
    {
        if(!place.Path.IsEmpty)
        {
            return ThisTiers.IsOwned(place.Path[^1]) ? ("owned field", place.Spelled()!) : null;
        }

        return place.Root switch
        {
            LocalReferenceExpression { Local: { IsRef: false, Ownership: OwnershipKind.Owned } local } => ("owned binding", local.Name),
            FieldReferenceExpression { Field: var field } when ThisTiers.IsOwned(field) => ("owned field", field.Name),
            MemberAccessExpression { Member: FieldResult { Field: var field } } access when ThisTiers.IsOwned(field) => ("owned field", OwnershipFlow.Spelled(access) ?? field.Name),
            _ => null,
        };
    }

    private static void JudgeLend(AnalysisContext context, ThisTiers tiers, Expression call, Expression argument, Parameter parameter)
    {
        JudgeAccess(context, tiers, call, argument, ThisTiers.LendTier(parameter), parameter.Name);
    }

    private static void JudgeAccess(AnalysisContext context, ThisTiers tiers, Expression call, Expression argument, ThisTier required, string target)
    {
        if(required == ThisTier.None || ThisTiers.IsValue(argument.Type) || OwnedProvenance.SharedExtraction(argument) is not null)
        {
            return;
        }

        var locals = LocalOwnership.For(context.RootOf(call) ?? call);
        var given = argument is ThisExpression ? tiers.Provided(argument, context.MemberOf(call)) : locals.Lends(argument);

        if(given >= required)
        {
            return;
        }

        if(OwnershipAnalyzer.Selected(argument).OfType<NewExpression>().FirstOrDefault(LocalOwnership.IsLeaky) is { } creation)
        {
            context.Report(
                ProgramAnalysisDiagnostics.LentConstructionNotFresh, creation.Node, [creation],
                new EnumValue(ThisTiers.Display(required)), new NameValue(target), DiagnosticSnapshot.Type(creation.Type));
            return;
        }

        var lender = OwnershipAnalyzer.Selected(argument).FirstOrDefault(leaf => locals.Lends(leaf) == given) ?? argument;
        context.Report(
            ProgramAnalysisDiagnostics.IncompatibleLend, argument.Node, [argument],
            new EnumValue(ThisTiers.Display(given)), new NameValue(Lender(lender)), new EnumValue(ThisTiers.Display(required)), new NameValue(target));
    }

    internal static bool BareRef(ParameterModifier parameter)
    {
        return parameter is { IsRef: true, IsReadonly: false, Ownership: OwnershipKind.Unrestricted };
    }

    private static bool JudgeExtraction(AnalysisContext context, Expression value, OwnershipKind destination, bool byReference)
    {
        if(OwnedProvenance.SharedExtraction(value) is not var (field, holder))
        {
            return false;
        }

        if(LocalOwnership.For(context.RootOf(value) ?? value).Of(holder) > ThisTier.None)
        {
            return false;
        }

        var refused = byReference
            || destination is OwnershipKind.Owned or OwnershipKind.Frozen or OwnershipKind.Borrowed
            || (!ThisTiers.IsValue(field.Type) && !Origins.IsUnchangeable(field.Type));

        if(refused)
        {
            context.Report(ProgramAnalysisDiagnostics.SharedExtract, value.Node, [value], new NameValue(field.Name), new NameValue(holder.Name));
        }

        return refused;
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
    /// Linear and affine bindings have one holder, so they lend with owned access despite an unrestricted binding modifier.
    /// </summary>
    private static bool Exclusive(IMistType type)
    {
        return OwnershipAnalyzer.Substructural(type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }

    private static string Lender(Expression source)
    {
        return OwnershipAnalyzer.Unwrapped(source) switch
        {
            MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: true } } access => $"static field '{Path(access)}'",
            MemberAccessExpression { Member: FieldResult } access => $"field '{Path(access)}'",
            MemberAccessExpression { Member: PropertyResult } access => $"property '{Path(access)}'",
            PropertyReferenceExpression property => $"property '{property.Property.Name}'",
            ExtensionPropertyAccessExpression property => $"property '{property.Property.Name}'",
            IndexExpression index => $"element of '{Path(index.Receiver)}'",
            InvocationExpression { Signature: { } method } => $"result of '{method.Name}'",
            InvocationExpression { Receiver: var callee } => OwnershipFlow.Spelled(callee) is { } named ? $"result of '{named}'" : "result of a function value",
            var other => Display(other),
        };
    }

    private static string Path(Expression expression)
    {
        return OwnershipFlow.Spelled(expression) ?? Display(expression);
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