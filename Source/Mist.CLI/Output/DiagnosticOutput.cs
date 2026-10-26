using System.Collections.Immutable;
using System.Text;
using Mist.Common.Diagnostics;

namespace Mist.CLI.Output;

public enum DiagnosticFormat { Text, Json }

public sealed class DiagnosticOutput(
    TextWriter error,
    DiagnosticFormat format = DiagnosticFormat.Text,
    DiagnosticSeverity minimum = DiagnosticSeverity.Info,
    string command = "build",
    string project = "",
    Action<byte[]>? documentFile = null)
{
    private readonly HashSet<int> collected = [];
    private readonly HashSet<int> written = [];
    private readonly List<Diagnostic> diagnostics = [];
    private bool completed;
    private bool fileFailed;

    public DiagnosticOperation Operation { get; } = new();

    public DiagnosticCounts Counts => DiagnosticCounts.From(diagnostics, minimum);

    public bool HasErrors => Counts.Errors > 0;

    public void Collect(IEnumerable<Diagnostic> occurrences)
    {
        foreach(var diagnostic in occurrences)
        {
            if(collected.Add(diagnostic.Ordinal))
            {
                diagnostics.Add(diagnostic);
            }
        }
    }

    public void CollectResult(IEnumerable<Diagnostic> occurrences)
    {
        var final = occurrences.DistinctBy(diagnostic => diagnostic.Ordinal).ToArray();
        var ordinals = final.Select(diagnostic => diagnostic.Ordinal).ToHashSet();
        _ = diagnostics.RemoveAll(diagnostic => ordinals.Contains(diagnostic.Ordinal));
        diagnostics.AddRange(final);
        collected.UnionWith(ordinals);
    }

    public void Stream(Diagnostic diagnostic)
    {
        Collect([diagnostic]);
        WriteText(diagnostic);
    }

    public void ReportHostFailure(Diagnostic diagnostic)
    {
        Collect([diagnostic]);
        // A completed document cannot be amended after child launch; late host failures use stderr text.
        if(completed && written.Add(diagnostic.Ordinal))
        {
            error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }
    }

    public void Complete(bool succeeded, ImmutableArray<NativeDiagnostic> native = default, string? outcome = null)
    {
        if(completed)
        {
            return;
        }

        foreach(var diagnostic in diagnostics)
        {
            WriteText(diagnostic);
        }

        if(!fileFailed && (format == DiagnosticFormat.Json || documentFile is not null))
        {
            var document = new DiagnosticDocument("mist 0.318.0", command, project,
                outcome ?? (succeeded && !HasErrors ? "succeeded" : "failed"), [.. diagnostics], native.IsDefault ? [] : native, minimum);
            var bytes = DiagnosticJsonWriter.Serialize(document);
            if(documentFile is not null)
            {
                fileFailed = true;
                documentFile(bytes);
                fileFailed = false;
            }
            else
            {
                error.WriteLine(Encoding.UTF8.GetString(bytes));
            }
        }

        completed = true;
    }

    private void WriteText(Diagnostic diagnostic)
    {
        if((format == DiagnosticFormat.Text || documentFile is not null)
            && DiagnosticCounts.IsVisible(diagnostic, minimum) && written.Add(diagnostic.Ordinal))
        {
            error.WriteLine(DiagnosticFormatter.Format(diagnostic));
        }
    }
}