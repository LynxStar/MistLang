using System.Globalization;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using RoslynApi = Microsoft.CodeAnalysis;

namespace Mist.Backends.CSharpTranspiler;

internal static class RoslynDiagnosticAdapter
{
    public static NativeDiagnostic Snapshot(RoslynApi.Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var location = new GeneratedLocation(span.Path ?? string.Empty,
            span.IsValid ? new LinePositionSpan(
                new LinePosition(span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1),
                new LinePosition(span.EndLinePosition.Line + 1, span.EndLinePosition.Character + 1)) : default, null);
        return new NativeDiagnostic("roslyn", diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.GetMessage(CultureInfo.InvariantCulture), location);
    }

    public static void Admit(NativeDiagnostic diagnostic, DiagnosticBag diagnostics)
    {
        if(diagnostic.Severity != "error")
        {
            return;
        }

        diagnostics.Admit(DiagnosticFactory.Create(
            BackendDiagnostics.CSharpError, diagnostic.Location, PipelineStage.Backend, [new TextValue(diagnostic.Message)],
            native: new ProviderCode(diagnostic.Provider, diagnostic.Code, DiagnosticSeverity.Error)));
    }
}