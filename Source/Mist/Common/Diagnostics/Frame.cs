using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// Provenance stays separate from message text; <see cref="TruncatedFrame"/> marks chains cut by a cycle or depth limit.
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

public sealed record InvariantFrame(EnumValue Operation, NameValue Detail) : Frame;