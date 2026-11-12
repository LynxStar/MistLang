using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

public static class DiagnosticTombstones
{
    public static readonly ImmutableArray<Tombstone> All =
    [
        new Tombstone(DiagnosticNumber.Mint("MIST0488"), "Runtime slot value not emitted", "5a5b03c3f74bc34dcad5e90822a5b4450d18e62a"),
    ];
}