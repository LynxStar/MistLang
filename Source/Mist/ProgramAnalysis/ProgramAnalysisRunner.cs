using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.ProgramAnalysis;

internal static class ProgramAnalysisRunner
{
    private static readonly IProgramAnalyzer[] Analyzers =
    [
        new ExhaustivenessAnalyzer(),
        new CastAnalyzer(),
        new AssignabilityAnalyzer(),
        new AccessibilityAnalyzer(),
        new DeclarationShapeAnalyzer(),
        new ConstraintAnalyzer(),
        new DefaultabilityAnalyzer(),
        new ConformanceAnalyzer(),
        new PatternAnalyzer(),
        new OperatorAnalyzer(),
        new PositionAnalyzer(),
        new AccessorAnalyzer(),
        new DefiniteAssignmentAnalyzer(),
        new StampabilityAnalyzer(),
        new OwnershipAnalyzer(),
        new TierAnalyzer(),
        new DisciplineAnalyzer(),
        new ReadonlyAnalyzer(),
        new EscapeAnalyzer(),
        new RefinementAnalyzer(),
    ];

    public static void Run(ProgramModelClass model, DiagnosticBag diagnostics)
    {
        var sourcePaths = SourceTypes.UnitsOf(model).Select(source => source.Unit.SourcePath).ToHashSet(StringComparer.Ordinal);
        var bodies = SourceBodies.Of(model).ToList();
        var clones = SourceTypes.Of(model).OfType<ConcreteType>().Where(IsClone).ToList();
        var conversions = bodies.Where(root => root.Member is CastOperator { ClonesThrough: not null }).ToList();

        var refusals = CloneConversionRefusals.Judge(conversions, root =>
        {
            var findings = new DiagnosticBag();
            Analyze(model, new AnalysisScope([], [root], []), sourcePaths, findings, CloneConversionRefusals.None);

            return findings.ToImmutableArray();
        });

        var program = new AnalysisScope(
            [.. SourceTypes.Of(model).Where(type => !IsClone(type))],
            [.. bodies.Where(root => !IsClone(root.Owner))],
            [.. SourceTypes.ExtensionsOf(model)]);

        Analyze(model, program, sourcePaths, diagnostics, refusals);

        CloneDiagnostics.Run(
            clones,
            clone => Analyze(
                model,
                new AnalysisScope([clone], [.. bodies.Where(root => root.Owner == clone).Except(conversions)], []),
                sourcePaths,
                diagnostics,
                refusals),
            diagnostics);
    }

    private static void Analyze(
        ProgramModelClass model,
        AnalysisScope scope,
        IReadOnlySet<string> sourcePaths,
        DiagnosticBag diagnostics,
        IReadOnlyDictionary<CastOperator, ConversionRefusal> refusals)
    {
        var nodes = new List<object>();
        var roots = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);
        var assignedBy = new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance);

        foreach(var root in scope.Bodies)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                nodes.Add(node);
                roots[node] = root;

                if(node is AssignmentExpression assignment)
                {
                    assignedBy[assignment.Target] = assignment;
                }
            }
        }

        foreach(var analyzer in Analyzers)
        {
            analyzer.Analyze(new AnalysisContext(model, scope, nodes, roots, assignedBy, sourcePaths, diagnostics, analyzer.Id, refusals));
        }
    }

    private static bool IsClone(MistType? type)
    {
        return type is ConcreteType { CloneSource: not null };
    }
}

internal sealed record AnalysisScope(
    IReadOnlyList<MistType> Types,
    IReadOnlyList<SourceRoot> Bodies,
    IReadOnlyList<ExtensionBlock> Extensions);