using System.Collections.Immutable;

namespace Mist.Common.Diagnostics;

/// <summary>
/// What the origin resolver found for a node: where it points, the synthesis frames it walked
/// through, and, when the producer's span did not fit its file, the fault the factory reports as
/// an internal compiler error beside the occurrence.
/// </summary>
public readonly record struct OriginResolution(Location Location, ImmutableArray<Frame> Frames, string? BoundsFault = null);