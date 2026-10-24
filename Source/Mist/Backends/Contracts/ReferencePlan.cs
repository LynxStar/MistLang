using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Backends.Contracts;

internal sealed record ReferencePlan(ImmutableArray<string> Paths, ImmutableArray<ComposedProject> Facades, bool Failed);