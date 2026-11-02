using System.Collections.Immutable;

namespace Mist.Backends.Contracts;

public interface IBackend
{
    BackendId Id { get; }
    ImmutableArray<ISourceTranslator> SourceTranslators { get; }
    ImmutableArray<string> ClaimedExtensions => [.. SourceTranslators.SelectMany(translator => translator.Extensions).Distinct(StringComparer.OrdinalIgnoreCase)];
}