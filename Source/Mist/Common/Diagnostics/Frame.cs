using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// One unit of provenance kept on an occurrence rather than appended to its message: how a
/// synthetic node came to be, or which operation the compiler was inside. A chain cut by the depth
/// limit or by a cycle ends in a <see cref="TruncatedFrame"/> so the reader knows it is incomplete.
/// </summary>
public abstract record Frame
{
    private protected Frame()
    {
    }
}

public sealed record LoweringFrame(string Reason) : Frame;

public sealed record SynthesisFrame(string Reason) : Frame;

public sealed record OperationFrame(string Project, ImmutableArray<string> ReferenceChain, string? Package) : Frame;

public sealed record TruncatedFrame(int Depth, bool Cyclic) : Frame;

public sealed record WithholdingFrame(SymbolValue Member, EnumValue Reason, TypeValue? Offending = null) : Frame;

public sealed record StormEntryFrame(int Offset, int Length, string Spelling) : Frame;