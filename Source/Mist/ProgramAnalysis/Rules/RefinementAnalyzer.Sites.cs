using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class RefinementAnalyzer
{
    private sealed partial class Proving
    {
        // Every site judged by Judge must participate here; nested bodies own independent entry facts.
        private bool NeedsFacts(FlowGraph graph)
        {
            return graph.Blocks.SelectMany(block => block.Steps).Any(step => (step.Node is Expression met && sites.TargetOf(met) is { } target && SlotIdentity.Owes(met, target)) || step.Node switch
            {
                ForEachStatement loop => loop.ElementConversion is null && SlotIdentity.Owes(EnumerableElement.Of(loop.Collection.Type), loop.Variable.Type),
                TupleDeconstructionStatement deconstruction => Deconstructed(deconstruction).Any(pair => SlotIdentity.Owes(pair.Element, pair.Target.Type)),
                LocalDeclarationStatement declaration => Refined(declaration.Local.Type) || SlotIdentity.Owes(declaration.Initializer, declaration.Local.Type),
                AssignmentExpression assignment => Refined(assignment.Target.Type) || assignment.Target is IndexExpression || SlotIdentity.Owes(assignment.Value, assignment.Target.Type)
                    || SlotIdentity.OwesThrough(Through(assignment.Target), assignment.Target.Type),
                CompoundAssignmentExpression compound => Refined(compound.Target.Type) || compound.Target is IndexExpression,
                ReturnStatement returned => Refined(returned.TargetType) || SlotIdentity.Owes(returned.Value, returned.TargetType),
                IndexExpression => true,
                InvocationExpression call => Targets(call.ParameterTypes) || Owed(call.Arguments, call.ParameterTypes)
                    || call.ParameterTypes?.Any(type => SlotIdentity.OwesThrough((call.Receiver as MemberAccessExpression)?.Receiver, type)) == true,
                NewExpression creation => Targets(creation.ParameterTypes) || Owed(creation.Arguments, creation.ParameterTypes),
                ObjectInitializerAssignment entry => Refined(entry.TargetType) || Targets(entry.AddParameterTypes)
                    || SlotIdentity.Owes(entry.Value, entry.TargetType) || Owed([entry.Value], entry.AddParameterTypes),
                CastExpression cast => Refined(cast.ParameterType ?? cast.TargetType) || SlotIdentity.Owes(cast.Operand, cast.ParameterType ?? cast.TargetType),
                FunctionReferenceExpression { Type: IFunctionType function } reference => function.ParameterTypes.Zip(reference.ClosedParameterTypes).Any(pair => SlotIdentity.Owes(pair.First, pair.Second))
                    || SlotIdentity.Owes(reference.ClosedReturnType, function.ReturnType),
                _ => false,
            });
        }

        public void NeedArguments(
            FactState state, IReadOnlyList<Expression> arguments,
            IReadOnlyList<IMistType>? parameterTypes, IReadOnlyList<Parameter>? parameters, Expression? receiver = null, Method? callee = null,
            IReadOnlyCollection<SlotCapture>? exact = null)
        {
            if(parameterTypes is null)
            {
                return;
            }

            var flattened = parameters is not null && ValuePack.FlattenedSlot(parameters) is not null;
            var reading = Reading.Of(state, arguments, callee?.SourceDeclaration ?? callee, flattened, this, receiver);
            var targets = flattened
                ? ArgumentTargets.Of(
                    parameters, parameterTypes, [.. arguments.Select(argument => argument is SpreadExpression ? null : argument.Type)],
                    context.Model.TypeInterner)
                : parameterTypes;

            for(var i = 0; targets is not null && i < arguments.Count && i < targets.Count; i++)
            {
                var slot = flattened ? ValuePack.SlotOf(parameters!, arguments.Count, i) : i;
                var parameter = parameters is not null && slot < parameters.Count ? parameters[slot] : null;

                if(targets[i] is not { } target || (flattened && arguments[i] is SpreadExpression))
                {
                    continue;
                }

                if(parameter is { Modifier.Direction: ByRefDirection.Out })
                {
                    NeedClosing(state, arguments[i].Type, target, arguments[i].Node, [arguments[i]], SlotMeeting.Equal);
                    continue;
                }

                Need(
                    state, arguments[i], target, arguments[i], onConstraint: parameter?.Name ?? RefinementFindings.Display(arguments[i]), measured: Measured(receiver),
                    reading: reading,
                    meeting: parameter is { Modifier.Direction: ByRefDirection.Ref } ? SlotMeeting.Equal with { Exact = exact ?? [], Receiver = receiver } : new SlotMeeting(exact ?? [], Receiver: receiver));
            }
        }

        // The value a written member is reached through, whose own slots the written value meets.
        private static Expression? Through(Expression target)
        {
            return target switch
            {
                MemberAccessExpression { Member: FieldResult or PropertyResult } member => member.Receiver,
                IndexExpression { Indexer: not null } index => index.Receiver,
                _ => null,
            };
        }

        private static bool Owed(IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType>? types)
        {
            return types is not null && arguments.Any(argument => types.Any(type => SlotIdentity.Owes(argument, type)));
        }

        private static bool Targets(IReadOnlyList<IMistType>? types)
        {
            return types?.Any(Refined) == true;
        }

        private static bool Refined(IMistType? type)
        {
            return type is UnionType union ? union.Constituents.Any(Refined) : type is not null && Requirement.Refined(type) is not null;
        }
    }
}