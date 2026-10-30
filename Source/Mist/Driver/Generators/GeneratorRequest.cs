using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Driver.Generators;

internal sealed record GeneratorRequest(
    string ToolchainIdentity,
    string CompileKey,
    string ConsumerName,
    string ConsumerManifest,
    GeneratorReference Reference,
    ProjectConfiguration Generator,
    ImmutableArray<ContentHash> Inputs);