using System.Collections.Immutable;

namespace Mist.Driver.Contracts;

public sealed record GeneratorReference(
    string Name,
    string ProjectPath,
    ImmutableArray<string> Inputs,
    ImmutableArray<string> Arguments,
    TimeSpan TimeLimit,
    bool Seed = false);