using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
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
                AssignmentExpression assignment => Refined(assignment.Target.Type) || assignment.Target is IndexExpression || SlotIdentity.Owes(assignment.Value, assignment.Target.Type),
                CompoundAssignmentExpression compound => Refined(compound.Target.Type) || compound.Target is IndexExpression,
                ReturnStatement returned => Refined(returned.TargetType) || SlotIdentity.Owes(returned.Value, returned.TargetType),
                IndexExpression => true,
                InvocationExpression call => Targets(call.ParameterTypes) || Owed(call.Arguments, call.ParameterTypes),
                NewExpression creation => Targets(creation.ParameterTypes) || Owed(creation.Arguments, creation.ParameterTypes),
                ObjectInitializerAssignment entry => Refined(entry.TargetType) || Targets(entry.AddParameterTypes)
                    || SlotIdentity.Owes(entry.Value, entry.TargetType) || Owed([entry.Value], entry.AddParameterTypes),
                CastExpression cast => Refined(cast.ParameterType ?? cast.TargetType) || SlotIdentity.Owes(cast.Operand, cast.ParameterType ?? cast.TargetType),
                _ => false,
            });
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