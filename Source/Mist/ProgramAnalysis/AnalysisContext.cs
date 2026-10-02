using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal sealed class AnalysisContext(
    ProgramModelClass model,
    AnalysisScope scope,
    IReadOnlyList<object> nodes,
    IReadOnlyDictionary<object, SourceRoot> roots,
    IReadOnlyDictionary<object, AssignmentExpression> assignedBy,
    IReadOnlySet<string> sourcePaths,
    DiagnosticBag diagnostics,
    string analyzerId,
    IReadOnlyDictionary<CastOperator, ConversionRefusal> conversionRefusals,
    AnalysisModel? scopedModel = null)
{
    public AnalysisModel Model { get; } = scopedModel ?? new AnalysisModel(model, scope, sourcePaths);

    public IReadOnlyList<MistType> Types { get; } = scope.Types;

    public IReadOnlyList<SourceRoot> Bodies { get; } = scope.Bodies;

    public IReadOnlyList<ExtensionBlock> Extensions { get; } = scope.Extensions;

    public IReadOnlyList<TypeNaming> Namings { get; } = scope.Namings;

    public IReadOnlyList<object> Nodes { get; } = nodes;

    /// <summary>
    /// The projects the compilation is made of, in the program pass only: a rule about a project as a
    /// whole has nothing to say again for each cloned type or conversion the runner re-analyzes.
    /// </summary>
    public IReadOnlyList<MistAssembly> Projects => scope.RestrictToScope
        ? []
        : [.. model.Assemblies.Where(assembly => assembly.Kind is MistAssemblyKind.Self or MistAssemblyKind.Reference)];

    public IReadOnlyDictionary<CastOperator, ConversionRefusal> ConversionRefusals { get; } = conversionRefusals;

    /// <summary>
    /// The type whose declaration a body node sits inside, or null for top-level statements, an
    /// extension block, or a node that was never walked. What a rule about ACCESS needs and the node
    /// cannot say: a body reaches its type only through a scope chain, which is the binder's, not the
    /// model's.
    /// </summary>
    public MistType? OwnerOf(object node)
    {
        return roots.TryGetValue(node, out var root) ? root.Owner : null;
    }

    public object? MemberOf(object node)
    {
        return roots.TryGetValue(node, out var root) ? root.Member : null;
    }

    public object? RootOf(object node)
    {
        return roots.TryGetValue(node, out var root) ? root.Root : null;
    }

    public MistAssembly? AssemblyOf(object node)
    {
        return roots.TryGetValue(node, out var root) ? root.Assembly : null;
    }

    /// <summary>
    /// Answered from the assignment holding the node as its target, and read for everything else. The
    /// model records no direction on an expression that can be a target -- see
    /// <see cref="PropertyReferenceExpression"/> -- so this is the one place it is derived, and a
    /// <c>??=</c> target is both, since it is read before it is written.
    /// </summary>
    public AccessDirection DirectionOf(object node)
    {
        if(!assignedBy.TryGetValue(node, out var assignment))
        {
            return AccessDirection.Read;
        }

        return assignment.IsCoalescing ? AccessDirection.ReadWrite : AccessDirection.Write;
    }

    public bool IsInSource(SyntaxNode node)
    {
        return sourcePaths.Contains(DiagnosticHelper.ResolveLocation(node).Path)
            && (scope.Boundary?.Contains(node) ?? true);
    }

    /// <summary>
    /// Reports a finding ABOUT the given expressions, and stays silent when any of them already failed.
    /// Binding and analysis gather into one bag, so a rule runs over a model that may hold ErrorType --
    /// and a complaint about a value that did not bind is a second complaint about one mistake. Decided
    /// here, where every report passes, so that no rule has to remember it.
    /// </summary>
    public void Report(
        DiagnosticDescriptor descriptor,
        SyntaxNode at,
        IReadOnlyList<Expression> about,
        params ReadOnlySpan<object?> args)
    {
        if(about.Any(expression => expression.Type is ErrorType) || (scope.Boundary is { } boundary && !boundary.Contains(at)))
        {
            return;
        }

        diagnostics.ReportAtNode(descriptor, at, analyzerId, args);
    }

    public void ReportAtProject(DiagnosticDescriptor descriptor, MistAssembly project, params ReadOnlySpan<object?> args)
    {
        diagnostics.Add(new Diagnostic(
            descriptor.Id,
            descriptor.DefaultSeverity,
            descriptor.Stage,
            string.Format(CultureInfo.InvariantCulture, descriptor.MessageTemplate, args.ToArray()),
            Location: default,
            CompilationUnitPath: project.ProjectPath ?? project.Name,
            AnalyzerName: analyzerId));
    }
}