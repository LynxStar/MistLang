namespace Mist.ProgramAnalysis;

/// <summary>
/// Keyed by analyzer instance rather than by <see cref="IProgramAnalyzer.Id"/>: several distinct rules
/// share an Id, and summing them under it would report one rule's time as another's.
/// </summary>
internal sealed class AnalysisTimings
{
    private readonly List<AnalyzerTiming> analyzers = [];
    private readonly Dictionary<IProgramAnalyzer, int> positions = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<AnalyzerTiming> Analyzers => analyzers;

    public int ProgramScopes { get; private set; }

    public int CloneConversionScopes { get; private set; }

    public int ClonedTypeScopes { get; private set; }

    public int Scopes => ProgramScopes + CloneConversionScopes + ClonedTypeScopes;

    public TimeSpan InAnalyzers => analyzers.Aggregate(TimeSpan.Zero, (sum, analyzer) => sum + analyzer.Elapsed);

    public void RecordScope(AnalysisScopeKind kind)
    {
        switch(kind)
        {
            case AnalysisScopeKind.Program:
                ProgramScopes++;
                break;
            case AnalysisScopeKind.CloneConversion:
                CloneConversionScopes++;
                break;
            case AnalysisScopeKind.ClonedType:
                ClonedTypeScopes++;
                break;
        }
    }

    public void Record(IProgramAnalyzer analyzer, TimeSpan elapsed)
    {
        if(positions.TryGetValue(analyzer, out var position))
        {
            analyzers[position] = analyzers[position] with { Elapsed = analyzers[position].Elapsed + elapsed };
            return;
        }

        positions[analyzer] = analyzers.Count;
        analyzers.Add(new AnalyzerTiming(analyzer.Id, analyzer.GetType().Name, elapsed));
    }
}

internal enum AnalysisScopeKind
{
    Program,
    CloneConversion,
    ClonedType,
}

internal sealed record AnalyzerTiming(string Id, string Name, TimeSpan Elapsed);