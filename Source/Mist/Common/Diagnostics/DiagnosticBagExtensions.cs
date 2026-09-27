using System.Globalization;
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
        DiagnosticDescriptor descriptor,
        SyntaxNode node,
        string? analyzerName,
        params ReadOnlySpan<object?> args)
    {
        var (path, location, _) = DiagnosticHelper.ResolveLocation(node);

        var message = args.Length > 0
            ? string.Format(CultureInfo.InvariantCulture, descriptor.MessageTemplate, args.ToArray())
            : descriptor.MessageTemplate;

        bag.Add(new Diagnostic(
            descriptor.Id,
            descriptor.DefaultSeverity,
            descriptor.Stage,
            message,
            location,
            path,
            analyzerName));
    }
}