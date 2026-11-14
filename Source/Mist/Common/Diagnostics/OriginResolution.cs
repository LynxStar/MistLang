using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// Bounds faults travel beside the resolved location so the factory can report a compiler error alongside the occurrence.
public readonly record struct OriginResolution(Location Location, ImmutableArray<Frame> Frames, string? BoundsFault = null);