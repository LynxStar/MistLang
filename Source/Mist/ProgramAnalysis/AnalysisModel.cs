using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Scopes;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal sealed class AnalysisModel
{
    public AnalysisModel(ProgramModelClass model, AnalysisScope scope, IReadOnlySet<string> sourcePaths)
    {
        TypeInterner = model.TypeInterner;
        TypesByFQN = model.TypesByFQN;

        bool includes(SyntaxNode node, Scope? boundIn)
        {
            return scope.ContainsBinding(boundIn)
                && sourcePaths.Contains(DiagnosticHelper.ResolveLocation(node).Path)
                && (scope.Boundary?.Contains(node) ?? true);
        }

        var registry = model.ConstructorConstraintClosures;
        ConstructorConstraintClosures = new ScopedConstraintClosures(
            [.. registry.Closings.Where(closing => includes(closing.Node, closing.BoundIn))],
            [.. registry.OpenClosings.Where(closing => includes(closing.Node, closing.BoundIn))]);
        OptionalApplications = new ScopedOptionalApplications(
            [.. model.OptionalApplications.Applications.Where(application => includes(application.Node, application.BoundIn))]);
        CompositeApplications = [.. model.CompositeApplications.Applications.Where(application => includes(application.Node, application.BoundIn))];
    }

    public TypeInterner TypeInterner { get; }

    public IReadOnlyDictionary<string, IMistType> TypesByFQN { get; }

    public ScopedConstraintClosures ConstructorConstraintClosures { get; }

    public ScopedOptionalApplications OptionalApplications { get; }

    public IReadOnlyList<CompositeApplication> CompositeApplications { get; }
}

internal sealed record ScopedConstraintClosures(IReadOnlyList<TypeArgumentClosing> Closings, IReadOnlyList<TypeArgumentClosing> OpenClosings)
{
    public IEnumerable<TypeArgumentClosing> StampableOpenClosings => OpenClosings.Where(closing =>
        closing.Parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable));
}

internal sealed record ScopedOptionalApplications(IReadOnlyList<OptionalApplication> Applications);