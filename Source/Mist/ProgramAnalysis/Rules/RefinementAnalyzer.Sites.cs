using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
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
        private static bool NeedsFacts(FlowGraph graph)
        {
            return graph.Blocks.SelectMany(block => block.Steps).Any(step => step.Node switch
            {
                LocalDeclarationStatement declaration => Refined(declaration.Local.Type),
                AssignmentExpression assignment => Refined(assignment.Target.Type) || assignment.Target is IndexExpression,
                CompoundAssignmentExpression compound => Refined(compound.Target.Type) || compound.Target is IndexExpression,
                ReturnStatement returned => Refined(returned.TargetType),
                IndexExpression => true,
                InvocationExpression call => Targets(call.ParameterTypes),
                NewExpression creation => Targets(creation.ParameterTypes),
                ObjectInitializerAssignment entry => Refined(entry.TargetType) || Targets(entry.AddParameterTypes),
                CastExpression cast => Refined(cast.ParameterType ?? cast.TargetType),
                _ => false,
            });
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