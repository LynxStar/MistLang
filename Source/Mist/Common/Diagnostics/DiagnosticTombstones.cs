using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

public static class DiagnosticTombstones
{
    public static readonly ImmutableArray<Tombstone> All =
    [
        new Tombstone(DiagnosticNumber.Mint("MIST0488"), "Runtime slot value not emitted", "5a5b03c3f74bc34dcad5e90822a5b4450d18e62a"),
        new Tombstone(DiagnosticNumber.Mint("MIST0494"), "Runtime closing test under combinator", "ad262e7e9262669d197fedc6e8d0a3b076f7ec63"),
        new Tombstone(DiagnosticNumber.Mint("MIST0496"), "Zeroed storage of a value-routed struct", "ad262e7e9262669d197fedc6e8d0a3b076f7ec63"),
        new Tombstone(DiagnosticNumber.Mint("MIST0503"), "Zeroed storage holds a value-routed struct", "ad262e7e9262669d197fedc6e8d0a3b076f7ec63"),
    ];
}