using Mist.Common.Diagnostics;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class EmissionDiagnostics
{
    [ThreadStatic]
    private static Scope? current;

    public static IDisposable Enter(DiagnosticBag diagnostics, SyntaxNode? node = null)
    {
        var enclosing = current;
        var scope = new Scope(diagnostics, node ?? enclosing?.Node, enclosing);
        current = scope;
        return scope;
    }

    public static string Fallback(string kind, string detail, string comment)
    {
        if(current is { } scope)
        {
            Report(scope.Diagnostics, kind, detail, scope.Node);
        }

        return comment;
    }

    public static void Report(DiagnosticBag diagnostics, string kind, string detail, SyntaxNode? node = null)
    {
        node ??= current?.Node;
        var origin = node is null
            ? new OriginResolution(new Absent(AbsenceReason.SyntheticWithoutRoot), [])
            : DiagnosticHelper.ResolveLocation(node, node.Span);
        var operation = new EnumValue(kind);
        var subject = new NameValue(detail);
        diagnostics.Admit(DiagnosticFactory.Create(BackendDiagnostics.EmissionFallback,
            origin with { Frames = [.. origin.Frames, new InvariantFrame(operation, subject)] }, PipelineStage.Backend,
            [operation, subject]));
    }

    private sealed class Scope(DiagnosticBag diagnostics, SyntaxNode? node, Scope? enclosing) : IDisposable
    {
        public DiagnosticBag Diagnostics { get; } = diagnostics;

        public SyntaxNode? Node { get; } = node;

        public void Dispose()
        {
            current = enclosing;
        }
    }
}