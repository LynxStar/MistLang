using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

public sealed class RegistryIndex
{
    private readonly Dictionary<string, MistDiagnostic> byNumber;

    internal RegistryIndex(ImmutableArray<RegistryEntry> entries, ImmutableArray<Tombstone> tombstones)
    {
        Entries = entries;
        Tombstones = tombstones;
        byNumber = new Dictionary<string, MistDiagnostic>(StringComparer.Ordinal);
        foreach(var entry in entries)
        {
            _ = byNumber.TryAdd(entry.Descriptor.Number.Value, entry.Descriptor);
        }
    }

    public ImmutableArray<RegistryEntry> Entries { get; }

    public ImmutableArray<Tombstone> Tombstones { get; }

    public IEnumerable<MistDiagnostic> Descriptors => Entries.Select(entry => entry.Descriptor);

    public bool TryGet(DiagnosticNumber number, out MistDiagnostic descriptor)
    {
        return byNumber.TryGetValue(number.Value ?? "", out descriptor!);
    }

    public MistDiagnostic? Find(string code)
    {
        return byNumber.GetValueOrDefault(code);
    }

    public IEnumerable<MistDiagnostic> InCategory(DiagnosticCategory category)
    {
        return Descriptors.Where(descriptor => descriptor.Category == category);
    }

    public ImmutableArray<string> Validate()
    {
        return RegistryValidator.Validate(Entries, Tombstones);
    }
}