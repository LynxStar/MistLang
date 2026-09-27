using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Ownership-v2 Requirement 6.9, where the binder left it: an owned binding, an owned field or
/// property, and an owned parameter each promise a value nothing else holds, so what moves into one
/// must yield ownership exactly as an owned return must. The binder clones the source of an owned
/// binding or parameter where its type declares Clone and leaves any other alone, and every rule that
/// reads an owned slot -- the purity judge's Own origin above all -- trusts that nothing unowned got in,
/// in every scope and not only a judged one.
/// </summary>
internal static class OwnedDestinations
{
    public static void Judge(AnalysisContext context, SourceRoot root)
    {
        var method = root.Member as Method;
        var origins = new Origins(root.Root, root.Member, ScopeKind.Readonly);

        switch(root.Member)
        {
            case Field field when ReferenceEquals(field.Initializer, root.Root) && ThisTiers.IsOwned(field):
                Require(context, field.Initializer, $"owned field '{field.Name}'", method, origins);
                break;

            case Property { Initializer: { } initializer, Modifiers: { IsOwned: true, IsStatic: false } } property when ReferenceEquals(initializer, root.Root):
                Require(context, initializer, $"owned property '{property.Name}'", method, origins);
                break;

            case Constructor { ChainedConstructor: { } chained } constructor when root.Root is Expression argument:
                var index = constructor.ChainArgumentsList.IndexOf(argument);

                if(index >= 0 && index < chained.ParametersList.Count && IsOwned(chained.ParametersList[index]))
                {
                    Require(context, argument, $"owned parameter '{chained.ParametersList[index].Name}'", method, origins);
                }

                break;
        }

        Visit(context, root.Root, method, origins, storesJudgedByPurity: Promised(root));
    }

    private static void Visit(AnalysisContext context, object node, Method? method, Origins origins, bool storesJudgedByPurity)
    {
        switch(node)
        {
            case LocalDeclarationStatement { Local: { Ownership: OwnershipKind.Owned } local, Initializer: { } value }:
                Require(context, value, $"owned binding '{local.Name}'", method, origins, extracts: true);
                break;

            case AssignmentExpression assignment:
                foreach(var (target, value) in Deconstructions.Pairs(assignment))
                {
                    if(target is LocalReferenceExpression { Local: { Ownership: OwnershipKind.Owned } local })
                    {
                        Require(context, value, $"owned binding '{local.Name}'", method, origins, extracts: true);
                    }
                    else if(!storesJudgedByPurity && Destination(target, origins) is { } destination)
                    {
                        Require(context, value, destination, method, origins);
                    }
                }

                break;

            case ObjectInitializerAssignment { Add: null } entry when !storesJudgedByPurity && Destination(entry.Member) is { } destination:
                Require(context, entry.Value, destination, method, origins);
                break;

            case InvocationExpression invocation:
                Arguments(context, invocation.Arguments, invocation.Method?.ParametersList, method, origins);
                break;

            case NewExpression allocation:
                Arguments(context, allocation.Arguments, allocation.Constructor?.ParametersList, method, origins);
                break;
        }

        var nested = storesJudgedByPurity && node is not (LambdaExpression or LocalFunctionStatement);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            Visit(context, child, method, origins, nested);
        }
    }

    private static bool Promised(SourceRoot root)
    {
        var modifiers = root.Member switch
        {
            Method member when ReferenceEquals(member.Body, root.Root) => member.Modifiers,
            Property property => PurityInference.AccessorOf(property.Getter, property.Setter, root.Root) is { } accessor ? accessor.OwnModifiers ?? property.Modifiers : null,
            Indexer indexer => PurityInference.AccessorOf(indexer.Getter, indexer.Setter, root.Root) is { } accessor ? accessor.OwnModifiers ?? indexer.Modifiers : null,
            _ => (MemberModifiers?)null,
        };

        return modifiers is { IsPure: true } or { IsLocal: true };
    }

    public static void JudgeDefaults(AnalysisContext context, ConcreteType type)
    {
        if(type.DefaultBlock is { } block)
        {
            JudgeEntries(context, block.Entries, type);
        }
    }

    private static void JudgeEntries(AnalysisContext context, IReadOnlyList<DefaultEntry> entries, MemberType? holder)
    {
        foreach(var entry in entries)
        {
            var field = (holder as ConcreteType)?.FieldsList.Find(candidate => candidate.Name == entry.FieldName && !candidate.Modifiers.IsStatic);

            switch(entry)
            {
                case DefaultValueEntry valued when field is not null && ThisTiers.IsOwned(field):
                    Require(context, valued.Value, $"owned field '{field.Name}'", null, new Origins(valued.Value, null, ScopeKind.Readonly));
                    break;

                case DefaultBindingEntry binding:
                    JudgeEntries(context, binding.NestedEntries, PurityJudge.DefinitionOf(binding.BoundType ?? field?.Type ?? UnknownType.Instance) as MemberType);
                    break;
            }
        }
    }

    private static void Arguments(
        AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<Parameter>? parameters, Method? method, Origins origins)
    {
        for(var i = 0; parameters is not null && i < arguments.Count && i < parameters.Count; i++)
        {
            if(IsOwned(parameters[i]))
            {
                Require(context, arguments[i], $"owned parameter '{parameters[i].Name}'", method, origins);
            }
        }
    }

    private static bool IsOwned(Parameter parameter)
    {
        return parameter.Modifier.Ownership == OwnershipKind.Owned && !parameter.IsFlattened;
    }

    private static string? Destination(Expression target, Origins origins)
    {
        return target switch
        {
            FieldReferenceExpression { Field: var field } when ThisTiers.IsOwned(field) => $"owned field '{field.Name}'",
            BackingFieldReferenceExpression { Property: { Modifiers: { IsOwned: true, IsStatic: false } } backed } => $"owned property '{backed.Name}'",
            MemberAccessExpression { Member: var member } => Destination(member),
            PropertyReferenceExpression { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } => $"owned property '{property.Name}'",
            LocalReferenceExpression { Local: { IsRef: true } local } when origins.ReferentOf(local) is { } referent && origins.IsOwnedSlot(referent) => $"the owned slot '{local.Name}' refers to",
            InvocationExpression { Method: { Modifiers.IsRef: true } called } => $"the slot '{called.Name}' returns a reference to",
            _ => null,
        };
    }

    private static string? Destination(LookupResult? member)
    {
        return member switch
        {
            FieldResult { Field: var field } when ThisTiers.IsOwned(field) => $"owned field '{field.Name}'",
            PropertyResult { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } => $"owned property '{property.Name}'",
            _ => null,
        };
    }

    private static void Require(AnalysisContext context, Expression value, string destination, Method? method, Origins origins, bool extracts = false)
    {
        if(!OwnershipAnalyzer.YieldsOwned(value, method, origins))
        {
            context.Report(ProgramAnalysisDiagnostics.OwnedSourceNotOwned, value.Node, [value], destination, OwnershipAnalyzer.Unowned(value));
        }
        else if(!extracts && OwnershipAnalyzer.HeldByAnotherOwner(value, method) is { } field)
        {
            context.Report(
                ProgramAnalysisDiagnostics.OwnedSourceNotOwned, value.Node, [value], destination,
                $"owned field '{field}' still holds it, and only an owned binding may take a value out of one");
        }
        else if(Unwrapped(value) is LocalReferenceExpression { Local: var local } && !OwnershipAnalyzer.Tracked(local) && !Origins.IsUnchangeable(local.Type))
        {
            context.Report(
                ProgramAnalysisDiagnostics.OwnedSourceNotOwned, value.Node, [value], destination,
                $"local '{local.Name}' is not an owned binding, so nothing stops it being used after the move");
        }
    }

    private static Expression Unwrapped(Expression expression)
    {
        return expression switch
        {
            ImplicitConversionExpression { Operator: null } conversion => Unwrapped(conversion.Operand),
            CastExpression { Operator: null } cast => Unwrapped(cast.Operand),
            AsExpression converted => Unwrapped(converted.Operand),
            _ => expression,
        };
    }
}