namespace Mist.Common.Diagnostics;

internal sealed class DiagnosticOwnership
{
    private readonly Dictionary<object, Dictionary<MistDiagnostic, List<Diagnostic>>> sources = new(ReferenceEqualityComparer.Instance);
    private ReplayScope? replay;

    public IDisposable Replaying(HashSet<int> admitted)
    {
        var scope = new ReplayScope(this, replay, admitted);
        replay = scope;
        return scope;
    }

    public Diagnostic? Previous(object owner, MistDiagnostic descriptor)
    {
        if(replay is null || !sources.TryGetValue(owner, out var findings) || !findings.TryGetValue(descriptor, out var occurrences))
        {
            return null;
        }

        return replay.Next(owner, descriptor, occurrences);
    }

    public void Record(object owner, Diagnostic occurrence)
    {
        if(replay is not null || occurrence.Severity != DiagnosticSeverity.Error)
        {
            return;
        }

        if(!sources.TryGetValue(owner, out var findings))
        {
            findings = [];
            sources.Add(owner, findings);
        }

        if(!findings.TryGetValue(occurrence.Descriptor, out var occurrences))
        {
            occurrences = [];
            findings.Add(occurrence.Descriptor, occurrences);
        }

        if(!occurrences.Any(existing => existing.Ordinal == occurrence.Ordinal))
        {
            occurrences.Add(occurrence);
        }
    }

    private sealed class ReplayScope(DiagnosticOwnership ownership, ReplayScope? enclosing, HashSet<int> admitted) : IDisposable
    {
        private readonly Dictionary<object, Dictionary<MistDiagnostic, int>> positions = new(ReferenceEqualityComparer.Instance);

        public Diagnostic? Next(object owner, MistDiagnostic descriptor, List<Diagnostic> occurrences)
        {
            if(!positions.TryGetValue(owner, out var descriptors))
            {
                descriptors = [];
                positions.Add(owner, descriptors);
            }

            var position = descriptors.GetValueOrDefault(descriptor);
            while(position < occurrences.Count && !admitted.Contains(occurrences[position].Ordinal))
            {
                position++;
            }

            descriptors[descriptor] = position + 1;
            return position < occurrences.Count ? occurrences[position] : null;
        }

        public void Dispose()
        {
            ownership.replay = enclosing;
        }
    }
}