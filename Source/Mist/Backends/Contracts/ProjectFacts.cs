using System.Collections.Immutable;

namespace Mist.Backends.Contracts;

public sealed record ProjectFacts(string NamespaceRoot, string ProjectFolder, ImmutableArray<string> GlobalUsings);