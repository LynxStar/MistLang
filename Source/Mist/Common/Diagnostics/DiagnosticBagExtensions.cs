using SourceSpan = Storm.Runtime.SourceSpan;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Extension methods for reporting diagnostics with AST node location resolution.
/// Replaces the per-analyzer MakeDiagnostic helper pattern.
/// </summary>
public static class DiagnosticBagExtensions
{
    /// <summary>
    /// Reports a diagnostic using a descriptor, resolving location from an AST node.
    /// </summary>
    public static void ReportAtNode(
        this DiagnosticBag bag,
        MistDiagnostic descriptor,
        SyntaxNode node,
        string? analyzerName,
        params ReadOnlySpan<object?> args)
    {
        bag.ReportAtNode(descriptor, node, node.Span, analyzerName, args);
    }

    public static void ReportAtNode(
        this DiagnosticBag bag,
        MistDiagnostic descriptor,
        SyntaxNode node,
        SourceSpan span,
        string? analyzerName,
        params ReadOnlySpan<object?> args)
    {
        bag.Admit(DiagnosticFactory.Create(
            descriptor, DiagnosticHelper.ResolveLocation(node, span), bag.Stage,
            LegacyArguments.For(descriptor, args).AsSpan(), analyzerName: analyzerName));
    }
}