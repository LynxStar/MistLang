using Mist.Common.Diagnostics;
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
    IReadOnlySet<string> sourcePaths,
    DiagnosticBag diagnostics,
    string analyzerId,
    IReadOnlyDictionary<CastOperator, ConversionRefusal> conversionRefusals)
{
    public ProgramModelClass Model { get; } = model;

    public IReadOnlyList<MistType> Types { get; } = scope.Types;

    public IReadOnlyList<SourceRoot> Bodies { get; } = scope.Bodies;

    public IReadOnlyList<ExtensionBlock> Extensions { get; } = scope.Extensions;

    public IReadOnlyList<object> Nodes { get; } = nodes;

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

    public MistAssembly? AssemblyOf(object node)
    {
        return roots.TryGetValue(node, out var root) ? root.Assembly : null;
    }

    public bool IsInSource(SyntaxNode node)
    {
        return sourcePaths.Contains(DiagnosticHelper.ResolveLocation(node).Path);
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
        if(about.Any(expression => expression.Type is ErrorType))
        {
            return;
        }

        diagnostics.ReportAtNode(descriptor, at, analyzerId, args);
    }
}