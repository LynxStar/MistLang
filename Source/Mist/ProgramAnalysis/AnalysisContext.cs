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
    AnalysisModel? scopedModel = null,
    AnalysisScope? program = null,
    ExpressionFailures? failures = null)
{
    private readonly ExpressionFailures failures = failures ?? new();

    private readonly DiagnosticBag diagnostics = Announce(diagnostics);

    public AnalysisModel Model { get; } = scopedModel ?? new AnalysisModel(model, scope, sourcePaths);

    /// <summary>
    /// Every type and body with Mist source, whatever this scope judges. A rule that follows calls reads
    /// it, because the callers of a body in scope sit wherever they sit: a clone's body is called from the
    /// program, and the program's from a clone.
    /// </summary>
    public AnalysisScope Program { get; } = program ?? scope;

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

    private static DiagnosticBag Announce(DiagnosticBag diagnostics)
    {
        diagnostics.Stage = PipelineStage.Analysis;
        return diagnostics;
    }

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
        return DiagnosticHelper.ResolveLocation(node).Location.Path is { } path
            && sourcePaths.Contains(path)
            && (scope.Boundary?.Contains(node) ?? true);
    }

    /// <summary>
    /// Reports a finding about values whose types can be judged. The indeterminate ErrorType guard
    /// does not establish ownership: deferred fallbacks use only positive evidence from
    /// <see cref="ExpressionFailures"/> and report when no owner exists.
    /// </summary>
    public void Report(
        MistDiagnostic descriptor,
        SyntaxNode at,
        IReadOnlyList<Expression> about,
        params ReadOnlySpan<object?> args)
    {
        if(about.Any(expression => expression.Type is ErrorType || failures.Of(expression)) || (scope.Boundary is { } boundary && !boundary.Contains(at)))
        {
            return;
        }

        diagnostics.ReportAtNode(descriptor, at, analyzerId, args);
    }

    public bool HasFailure(Expression expression)
    {
        return failures.Of(expression);
    }

    public void Refuse(
        Expression expression,
        MistDiagnostic descriptor,
        IReadOnlyList<Expression> about,
        params ReadOnlySpan<object?> args)
    {
        if(about.Any(value => value.Type is ErrorType || failures.Of(value))
            || (scope.Boundary is { } boundary && !boundary.Contains(expression.Node)))
        {
            return;
        }

        diagnostics.ReportAtNode(descriptor, expression.Node, analyzerId, args);
        failures.Record(expression);
    }

    public void ResolveFailure(Expression expression)
    {
        if(scope.Boundary is null || scope.Boundary.Contains(expression.Node))
        {
            failures.Resolve(expression, diagnostics);
        }
    }

    public void ReportAtProject(MistDiagnostic descriptor, MistAssembly project, params ReadOnlySpan<object?> args)
    {
        diagnostics.Admit(DiagnosticFactory.Create(
            descriptor, LegacyLocation.Map(project.ProjectPath ?? project.Name), diagnostics.Stage,
            LegacyArguments.For(descriptor, args).AsSpan(), analyzerName: analyzerId));
    }
}