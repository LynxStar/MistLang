using System.Collections.Immutable;
using System.Reflection;

namespace Mist.Common.Diagnostics;

/// <summary>
/// Built once per process by reflection over the registry classes, so the catalog, the JSON
/// document, the policy parser and the renderer all read one index and cannot disagree about which
/// descriptors exist. Entries are ordered by declaring type and declaration position.
/// </summary>
public static class DiagnosticRegistry
{
    private static readonly Lazy<RegistryIndex> Built = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static RegistryIndex Index => Built.Value;

    private static RegistryIndex Build()
    {
        var entries = typeof(DiagnosticRegistry).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(DiagnosticRegistry).Namespace && type.IsClass)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsInitOnly && field.FieldType == typeof(MistDiagnostic))
                .OrderBy(field => field.MetadataToken)
                .Select(field => new RegistryEntry(type, field.Name, (MistDiagnostic)field.GetValue(null)!)))
            .ToImmutableArray();

        return new RegistryIndex(entries, DiagnosticTombstones.All);
    }
}